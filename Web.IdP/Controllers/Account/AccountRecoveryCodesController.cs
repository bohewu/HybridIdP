using System.Security.Claims;
using Core.Application;
using Core.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Web.IdP.Attributes;
using Web.IdP.Helpers;
using Web.IdP.Services;

namespace Web.IdP.Controllers.Account;

[ApiController]
[Route("api/account/recovery-codes")]
[ApiAuthorize]
[ValidateCsrfForCookies]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountRecoveryCodesController(
    IAccountRecoveryCodeService codes,
    UserManager<ApplicationUser> users,
    ICurrentUserLifecycleEligibility lifecycle,
    IMigrationIssuanceGuard migration) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var user = await GetAuthorizedUserAsync(ct);
        return user == null ? Unauthorized(new { error = "sessionExpired" }) :
            Ok(await codes.GetStatusAsync(user.Id, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
    public async Task<IActionResult> Generate(CancellationToken ct)
    {
        var user = await GetAuthorizedUserAsync(ct);
        if (user == null) return Unauthorized(new { error = "sessionExpired" });
        if (!await MfaEnrollmentSession.IsRemovalAuthorizedAsync(HttpContext, user, ct))
            return StatusCode(403, new { error = "freshAuthenticationRequired" });
        MfaEnrollmentSession.Consume(HttpContext.Session);
        var recoveryCodes = await codes.GenerateAsync(user, ct);
        if (recoveryCodes == null) return Conflict(new { error = "freshAuthenticationRequired" });
        Response.Headers.CacheControl = "no-store";
        return Ok(new { recoveryCodes });
    }

    private async Task<ApplicationUser?> GetAuthorizedUserAsync(CancellationToken ct)
    {
        var cookie = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!cookie.Succeeded || cookie.Principal?.Identity?.IsAuthenticated != true) return null;
        var user = await users.GetUserAsync(cookie.Principal);
        if (user == null || string.IsNullOrEmpty(user.SecurityStamp) ||
            cookie.Principal.FindFirstValue(users.Options.ClaimsIdentity.SecurityStampClaimType) != user.SecurityStamp ||
            (User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")) != user.Id.ToString() ||
            !user.IsActive || user.IsDeleted || user.RequiresPasswordChange || await users.IsLockedOutAsync(user) ||
            !await lifecycle.IsEligibleAsync(user.Id, ct) || !await migration.CanIssueAsync(user.Id, ct)) return null;
        return user;
    }
}
