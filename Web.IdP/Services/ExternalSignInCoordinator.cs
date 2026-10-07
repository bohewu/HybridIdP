using System.Security.Claims;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Interfaces;
using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Web.IdP.Helpers;

namespace Web.IdP.Services;

public partial class ExternalSignInCoordinator : IExternalSignInCoordinator
{
    private readonly Web.IdP.Services.ICurrentUserLifecycleEligibility _lifecycleEligibility;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILoginService _loginService;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly IPasskeyService _passkeyService;
    private readonly IMigrationIssuanceGuard _migrationIssuanceGuard;
    private readonly ILogger<ExternalSignInCoordinator> _logger;
    private readonly TimeProvider _timeProvider;

    public ExternalSignInCoordinator(
        Web.IdP.Services.ICurrentUserLifecycleEligibility lifecycleEligibility,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILoginService loginService,
        ISecurityPolicyService securityPolicyService,
        IPasskeyService passkeyService,
        IMigrationIssuanceGuard migrationIssuanceGuard,
        ILogger<ExternalSignInCoordinator> logger,
        TimeProvider? timeProvider = null)
    {
        _lifecycleEligibility = lifecycleEligibility;
        _signInManager = signInManager;
        _userManager = userManager;
        _loginService = loginService;
        _securityPolicyService = securityPolicyService;
        _passkeyService = passkeyService;
        _migrationIssuanceGuard = migrationIssuanceGuard;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ExternalSignInCompletionResult> CompleteAsync(
        HttpContext httpContext,
        ApplicationUser user,
        CancellationToken cancellationToken = default)
        => await CompleteCoreAsync(httpContext, user, null, cancellationToken);

    public Task<ExternalSignInCompletionResult> LinkAsync(HttpContext httpContext,
        ApplicationUser user, UserLoginInfo login, CancellationToken cancellationToken = default)
        => CompleteCoreAsync(httpContext, user, login, cancellationToken);

    private async Task<ExternalSignInCompletionResult> CompleteCoreAsync(HttpContext httpContext,
        ApplicationUser user, UserLoginInfo? login, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(user);
        PendingExternalLoginLink.Cancel(httpContext);

        var eligibility = await _loginService.ValidateExternalUserSignInAsync(user, cancellationToken);
        if (!eligibility.IsSuccess)
        {
            return ExternalSignInCompletionResult.Blocked(eligibility);
        }

        if (!await _signInManager.CanSignInAsync(user))
        {
            LogSignInNotAllowed(user.Id);
            return ExternalSignInCompletionResult.Blocked(LoginResult.InvalidCredentials());
        }

        if (!await _migrationIssuanceGuard.CanIssueAsync(user.Id, cancellationToken) ||
            !await _lifecycleEligibility.IsEligibleAsync(user.Id, cancellationToken))
        {
            return ExternalSignInCompletionResult.Blocked(LoginResult.InvalidCredentials());
        }

        var preserveSession = login != null && httpContext.User.Identity?.IsAuthenticated == true &&
            httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) == user.Id.ToString() &&
            !string.IsNullOrEmpty(user.SecurityStamp) &&
            httpContext.User.FindFirstValue(_userManager.Options.ClaimsIdentity.SecurityStampClaimType) == user.SecurityStamp;
        if (preserveSession && MfaEnrollmentSession.HasMfa(httpContext.User))
        {
            return await PendingExternalLoginLink.PersistAsync(httpContext, user, login!, user.SecurityStamp, cancellationToken)
                ? ExternalSignInCompletionResult.Succeeded()
                : ExternalSignInCompletionResult.Blocked(LoginResult.InvalidCredentials());
        }

        await httpContext.Session.LoadAsync(cancellationToken);
        AuthenticationMethodSession.Replace(httpContext.Session, AuthConstants.Amr.External);

        if (login != null)
        {
            if (string.IsNullOrEmpty(user.SecurityStamp) || user.RequiresPasswordChange ||
                await _userManager.FindByLoginAsync(login.LoginProvider, login.ProviderKey) != null ||
                !(await _loginService.CanLinkExternalLoginAsync(user, login.LoginProvider, cancellationToken)).Succeeded)
                return ExternalSignInCompletionResult.Blocked(LoginResult.InvalidCredentials());
            PendingExternalLoginLink.Begin(httpContext.Session, user, login, _timeProvider);
        }

        if (user.TwoFactorEnabled)
        {
            await IssuePartialSignInAsync(httpContext, user);
            return ExternalSignInCompletionResult.TotpRequired();
        }

        if (user.EmailMfaEnabled)
        {
            await IssuePartialSignInAsync(httpContext, user);
            return ExternalSignInCompletionResult.EmailOtpRequired();
        }

        if (login != null && (await _passkeyService.GetUserPasskeysAsync(user.Id, cancellationToken)).Count > 0)
        {
            await IssuePartialSignInAsync(httpContext, user);
            return new ExternalSignInCompletionResult(ExternalSignInCompletionStatus.PasskeyRequired);
        }

        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        if (policy.EnforceMandatoryMfaEnrollment)
        {
            var passkeys = await _passkeyService.GetUserPasskeysAsync(user.Id, cancellationToken);
            if (passkeys.Count == 0)
            {
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                if (user.MfaRequirementNotifiedAt == null)
                {
                    user.MfaRequirementNotifiedAt = now;
                    var updateResult = await _userManager.UpdateAsync(user);
                    if (!updateResult.Succeeded)
                    {
                        httpContext.Session.Remove(AuthenticationMethodSession.SessionKey);
                        LogMfaNotificationUpdateFailed(user.Id);
                        return ExternalSignInCompletionResult.Blocked(LoginResult.InvalidCredentials());
                    }
                }

                var enforcementTime = user.MfaRequirementNotifiedAt.Value
                    .AddDays(policy.MfaEnforcementGracePeriodDays);
                if (now >= enforcementTime)
                {
                    await IssuePartialSignInAsync(httpContext, user, initialEnrollment: true);
                    return ExternalSignInCompletionResult.MfaEnrollmentRequired();
                }
            }
        }

        var claims = AuthenticationMethodSession.CreateClaims(
            httpContext.Session,
            AuthConstants.Amr.External);
        if (!await _lifecycleEligibility.IsEligibleAsync(user.Id, cancellationToken))
        {
            return ExternalSignInCompletionResult.Blocked(LoginResult.InvalidCredentials());
        }
        if (login != null)
        {
            PendingExternalLoginLink.Cancel(httpContext);
            if (!await PendingExternalLoginLink.PersistAsync(httpContext, user, login, user.SecurityStamp, cancellationToken))
                return ExternalSignInCompletionResult.Blocked(LoginResult.InvalidCredentials());
        }
        if (!preserveSession)
            await _signInManager.SignInWithClaimsAsync(user, isPersistent: false, claims);

        return ExternalSignInCompletionResult.Succeeded();
    }

    private Task IssuePartialSignInAsync(HttpContext httpContext, ApplicationUser user, bool initialEnrollment = false)
    {
        var identity = new ClaimsIdentity(IdentityConstants.TwoFactorUserIdScheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
        var linkPurpose = PendingExternalLoginLink.GetPurposeClaim(httpContext.Session, user.Id, _timeProvider);
        if (linkPurpose != null) identity.AddClaim(linkPurpose);
        if (initialEnrollment)
        {
            identity.AddClaim(MfaEnrollmentSession.BeginInitial(httpContext.Session, user.Id));
        }

        return httpContext.SignInAsync(
            IdentityConstants.TwoFactorUserIdScheme,
            new ClaimsPrincipal(identity));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "External sign-in is not allowed for user {UserId} by Identity policy.")]
    partial void LogSignInNotAllowed(Guid userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to persist mandatory MFA notification state for external user {UserId}.")]
    partial void LogMfaNotificationUpdateFailed(Guid userId);
}
