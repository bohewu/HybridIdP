using System.Security.Claims;
using Core.Application;
using Core.Application.Interfaces;
using Core.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Web.IdP.Helpers;

namespace Web.IdP.Services.Saml.Idp;

public sealed record SamlIdpAccountResult(Guid UserId, DateTimeOffset AuthenticatedAt, IActionResult? RequiredAction = null);

public interface ISamlIdpAccountPolicy
{
    Task<SamlIdpAccountResult> EvaluateAsync(HttpContext http, string returnUrl, CancellationToken cancellationToken);
}

/// <summary>Reuses the local Identity authority; SAML never imports OIDC grants or upstream MFA.</summary>
public sealed class SamlIdpAccountPolicy(IApplicationDbContext db, UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn, ICurrentUserLifecycleEligibility lifecycle,
    IMigrationIssuanceGuard migration, ISecurityPolicyService policies, IPasskeyService passkeys,
    IAccountRecoveryCodeService recoveryCodes, TimeProvider clock) : ISamlIdpAccountPolicy
{
    public async Task<SamlIdpAccountResult> EvaluateAsync(HttpContext http, string returnUrl, CancellationToken cancellationToken)
    {
        var authentication = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var principal = authentication.Principal;
        if (!authentication.Succeeded || principal?.Identity?.IsAuthenticated != true)
            return Action(new ChallengeResult(IdentityConstants.ApplicationScheme,
                new AuthenticationProperties { RedirectUri = returnUrl }));
        if (!AuthorizationAuthenticationSession.HasCurrentAssuranceVersion(principal) ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Denied();

        var user = await db.Users.AsNoTracking().Include(u => u.Person).SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user == null || !user.IsActive || user.IsDeleted || user.RequiresPasswordChange ||
            user.LockoutEnd > clock.GetUtcNow() ||
            user.PersonId.HasValue && user.Person?.CanAuthenticate() != true ||
            string.IsNullOrEmpty(user.SecurityStamp) ||
            principal.FindFirstValue(users.Options.ClaimsIdentity.SecurityStampClaimType) != user.SecurityStamp ||
            !await signIn.CanSignInAsync(user) || !await lifecycle.IsEligibleAsync(id, cancellationToken) ||
            !await migration.CanIssueAsync(id, cancellationToken) || !await lifecycle.IsEligibleAsync(id, cancellationToken))
            return Denied();
        var authTime = AuthorizationAuthenticationSession.GetAuthenticationTime(principal);
        if (authTime == null || authTime < 0 || authTime > clock.GetUtcNow().ToUnixTimeSeconds()) return Denied();

        var policy = await policies.GetCurrentPolicyAsync();
        if ((policy.EnforceMandatoryMfaEnrollment || user.TwoFactorEnabled || user.EmailMfaEnabled) &&
            !AuthorizationAuthenticationSession.HasCurrentMfa(principal, user))
        {
            http.User = principal;
            var hasPasskeys = (await passkeys.GetUserPasskeysAsync(id, cancellationToken)).Count > 0;
            var hasCodes = (await recoveryCodes.GetStatusAsync(id, cancellationToken)).RemainingCodes > 0;
            if (user.TwoFactorEnabled || user.EmailMfaEnabled || hasPasskeys || hasCodes)
            {
                var path = user.EmailMfaEnabled && !user.TwoFactorEnabled && !hasPasskeys && !hasCodes
                    ? "/Account/LoginEmailOtp" : "/Account/LoginMfa";
                return Action(new RedirectResult(QueryHelpers.AddQueryString(path, "returnUrl", returnUrl)));
            }
            http.Session.SetString("MfaEnforcedByAcr", "true");
            var setupUrl = QueryHelpers.AddQueryString("/Account/MfaSetup", "returnUrl", returnUrl);
            if (await MfaEnrollmentSession.TryContinueInitialAfterPasswordAsync(http, user, cancellationToken))
                return Action(new RedirectResult(setupUrl));
            if (await AccountSecurityOperationSession.BeginAsync(http, user,
                    AccountSecurityOperationSession.MfaEnrollmentPurpose, id.ToString(), cancellationToken) == null)
                return Denied();
            MfaEnrollmentSession.Begin(http.Session, id, securityStamp: user.SecurityStamp);
            return Action(new RedirectResult(await AccountSecurityOperationSession.GetLoginUrlAsync(http, user, setupUrl)));
        }
        return new(id, DateTimeOffset.FromUnixTimeSeconds(authTime.Value));
    }

    private static SamlIdpAccountResult Action(IActionResult action) => new(Guid.Empty, default, action);
    private static SamlIdpAccountResult Denied() => Action(new StatusCodeResult(StatusCodes.Status403Forbidden));
}
