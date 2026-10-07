using System.Security.Claims;
using System.Text.Json;
using Core.Application;
using Core.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Web.IdP.Services;

namespace Web.IdP.Helpers;

// A linking intent is not a login credential. Only its protected partial-cookie
// nonce, carried through an actual successful MFA ceremony, can complete it.
public static class PendingExternalLoginLink
{
    private const string SessionKey = "ExternalLogin:PendingLink";
    public const string PurposeClaim = "external_link";
    private static readonly object CompletionKey = new();

    public static Claim Begin(ISession session, ApplicationUser user, UserLoginInfo login,
        TimeProvider? timeProvider = null)
    {
        var pending = new Pending(user.Id, user.SecurityStamp ?? string.Empty,
            login.LoginProvider, login.ProviderKey, login.ProviderDisplayName,
            Guid.NewGuid().ToString("N"), (timeProvider ?? TimeProvider.System).GetUtcNow().AddMinutes(5));
        session.SetString(SessionKey, JsonSerializer.Serialize(pending));
        return new Claim(PurposeClaim, pending.Nonce);
    }

    public static void Cancel(ISession? session) => session?.Remove(SessionKey);

    public static void Cancel(HttpContext http) => Cancel(http.Features.Get<ISessionFeature>()?.Session);

    public static Claim? GetPurposeClaim(ISession session, Guid userId, TimeProvider? timeProvider = null)
    {
        var pending = Read(session, timeProvider);
        return pending?.UserId == userId ? new Claim(PurposeClaim, pending.Nonce) : null;
    }

    // Identity rotates the stamp when this authorized initial setup creates a
    // TOTP seed. Carry only that in-request transition, never an arbitrary reset.
    public static async Task CarryInitialEnrollmentStampAsync(HttpContext http, ApplicationUser user, string? previousStamp)
    {
        var pending = Read(http.Session);
        if (pending == null || pending.UserId != user.Id || pending.Stamp != previousStamp ||
            string.IsNullOrEmpty(user.SecurityStamp) || !MfaEnrollmentSession.HasInitial(http.Session, user.Id)) return;
        var partial = await http.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        if (partial.Succeeded && partial.Principal?.FindFirstValue(PurposeClaim) == pending.Nonce &&
            partial.Principal.FindFirstValue(ClaimTypes.NameIdentifier) == user.Id.ToString() &&
            await HasPersistedStampAsync(http, user.Id, user.SecurityStamp, http.RequestAborted))
            http.Session.SetString(SessionKey, JsonSerializer.Serialize(pending with { Stamp = user.SecurityStamp }));
    }

    // Called only by successful TOTP, email, recovery or user-verified passkey callers.
    public static async Task MarkMfaCompletionAsync(HttpContext http, ApplicationUser user)
    {
        var pending = Read(http.Features.Get<ISessionFeature>()?.Session);
        if (pending == null) return;
        var partial = await http.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        if (partial.Succeeded && partial.Principal?.Identity?.IsAuthenticated == true &&
            partial.Principal.FindFirstValue(ClaimTypes.NameIdentifier) == pending.UserId.ToString() &&
            partial.Principal.FindFirstValue(PurposeClaim) == pending.Nonce &&
            user.Id == pending.UserId && !string.IsNullOrEmpty(pending.Stamp) && user.SecurityStamp == pending.Stamp)
        {
            http.Items[CompletionKey] = pending.Nonce;
        }
    }

    public static async Task<bool> CompleteAsync(HttpContext http, ClaimsPrincipal principal,
        TimeProvider? timeProvider = null)
    {
        // A generic sign-in, refresh or factor-management reauth never consumes a link.
        if (!http.Items.Remove(CompletionKey, out var marker)) return true;
        var pending = Read(http.Session, timeProvider);
        Cancel(http);
        if (pending == null || marker is not string nonce || pending.Nonce != nonce ||
            principal.Identity?.IsAuthenticated != true || !MfaEnrollmentSession.HasMfa(principal) ||
            principal.FindFirstValue(ClaimTypes.NameIdentifier) != pending.UserId.ToString()) return false;

        var users = http.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        if (principal.FindFirstValue(users.Options.ClaimsIdentity.SecurityStampClaimType) != pending.Stamp) return false;
        var user = await users.FindByIdAsync(pending.UserId.ToString());
        if (user == null) return false;
        var result = await PersistAsync(http, user,
            new UserLoginInfo(pending.Provider, pending.ProviderKey, pending.DisplayName), pending.Stamp,
            http.RequestAborted, pending.ExpiresUtc, timeProvider);
        if (result) await http.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
        return result;
    }

    public static async Task<bool> PersistAsync(HttpContext http, ApplicationUser user,
        UserLoginInfo login, string? expectedStamp, CancellationToken cancellationToken = default,
        DateTimeOffset? expiresUtc = null, TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrEmpty(expectedStamp) || user.SecurityStamp != expectedStamp ||
            !user.IsActive || user.IsDeleted || user.RequiresPasswordChange) return false;
        var services = http.RequestServices;
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var loginService = services.GetRequiredService<ILoginService>();
        if (!(await loginService.ValidateExternalUserSignInAsync(user, cancellationToken)).IsSuccess ||
            !await services.GetRequiredService<SignInManager<ApplicationUser>>().CanSignInAsync(user) ||
            !await services.GetRequiredService<IMigrationIssuanceGuard>().CanIssueAsync(user.Id, cancellationToken) ||
            !await services.GetRequiredService<ICurrentUserLifecycleEligibility>().IsEligibleAsync(user.Id, cancellationToken) ||
            await users.FindByLoginAsync(login.LoginProvider, login.ProviderKey) != null ||
            !(await loginService.CanLinkExternalLoginAsync(user, login.LoginProvider, cancellationToken)).Succeeded)
            return false;

        if (!await HasPersistedStampAsync(http, user.Id, expectedStamp, cancellationToken) ||
            expiresUtc <= (timeProvider ?? TimeProvider.System).GetUtcNow()) return false;
        return (await users.AddLoginAsync(user, login)).Succeeded;
    }

    private static Task<bool> HasPersistedStampAsync(HttpContext http, Guid userId, string stamp, CancellationToken ct) =>
        http.RequestServices.GetRequiredService<IApplicationDbContext>().Users.AsNoTracking()
            .AnyAsync(user => user.Id == userId && user.SecurityStamp == stamp, ct);

    private static Pending? Read(ISession? session, TimeProvider? timeProvider = null)
    {
        var raw = session?.GetString(SessionKey);
        if (raw == null) return null;
        try
        {
            var pending = JsonSerializer.Deserialize<Pending>(raw);
            if (pending?.ExpiresUtc > (timeProvider ?? TimeProvider.System).GetUtcNow()) return pending;
        }
        catch (JsonException) { }
        Cancel(session);
        return null;
    }

    private sealed record Pending(Guid UserId, string Stamp, string Provider, string ProviderKey,
        string? DisplayName, string Nonce, DateTimeOffset ExpiresUtc);
}
