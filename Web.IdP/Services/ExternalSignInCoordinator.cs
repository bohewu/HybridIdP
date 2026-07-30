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
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILoginService _loginService;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly IPasskeyService _passkeyService;
    private readonly ILogger<ExternalSignInCoordinator> _logger;
    private readonly TimeProvider _timeProvider;

    public ExternalSignInCoordinator(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILoginService loginService,
        ISecurityPolicyService securityPolicyService,
        IPasskeyService passkeyService,
        ILogger<ExternalSignInCoordinator> logger,
        TimeProvider? timeProvider = null)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _loginService = loginService;
        _securityPolicyService = securityPolicyService;
        _passkeyService = passkeyService;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ExternalSignInCompletionResult> CompleteAsync(
        HttpContext httpContext,
        ApplicationUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(user);

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

        await httpContext.Session.LoadAsync(cancellationToken);
        AuthenticationMethodSession.Replace(httpContext.Session, AuthConstants.Amr.External);

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
                    await IssuePartialSignInAsync(httpContext, user);
                    return ExternalSignInCompletionResult.MfaEnrollmentRequired();
                }
            }
        }

        var claims = AuthenticationMethodSession.CreateClaims(
            httpContext.Session,
            AuthConstants.Amr.External);
        await _signInManager.SignInWithClaimsAsync(user, isPersistent: false, claims);

        return ExternalSignInCompletionResult.Succeeded();
    }

    private static Task IssuePartialSignInAsync(HttpContext httpContext, ApplicationUser user)
    {
        var identity = new ClaimsIdentity(IdentityConstants.TwoFactorUserIdScheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));

        return httpContext.SignInAsync(
            IdentityConstants.TwoFactorUserIdScheme,
            new ClaimsPrincipal(identity));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "External sign-in is not allowed for user {UserId} by Identity policy.")]
    partial void LogSignInNotAllowed(Guid userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to persist mandatory MFA notification state for external user {UserId}.")]
    partial void LogMfaNotificationUpdateFailed(Guid userId);
}
