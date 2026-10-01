using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Constants;
using Infrastructure.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;

namespace Web.IdP.Helpers;

public static class RecoveryReauthenticationSession
{
    private const string PendingKey = "recovery-preference.pending-auth";
    private const string ContextKey = "recovery-preference.context";
    private const string CsrfKey = "recovery-preference.csrf";
    private const string GrantKey = "recovery-preference.grant";
    private static readonly object CompletionKey = new();

    public static void Begin(HttpContext http, RecoveryAuthenticationState state)
    {
        http.Session.Remove(GrantKey);
        http.Session.SetString(ContextKey, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        http.Session.SetString(CsrfKey, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        http.Session.SetString(PendingKey, JsonSerializer.Serialize(new Pending(state.User.Id,
            state.User.SecurityStamp!, state.Authority, state.Binding, DateTimeOffset.UtcNow.AddMinutes(5), null)));
    }

    public static RecoveryPreferenceContext Context(HttpContext http) => new(
        http.Session.GetString(ContextKey) ?? string.Empty, http.Session.GetString(CsrfKey) ?? string.Empty,
        Guid.TryParse(http.Session.GetString(GrantKey), out var id) ? id : Guid.Empty);

    public static bool HasPending(HttpContext http) => Read(http) is not null;
    public static bool HasPendingExternal(HttpContext http) => Read(http)?.Authority == "external";

    public static async Task<LoginResult> AuthenticatePasswordAsync(HttpContext http, string login, string password,
        ILoginService loginService, UserManager<ApplicationUser> users, CancellationToken ct)
    {
        var pending = Read(http);
        if (pending is null) return await loginService.AuthenticateAsync(login, password, ct);
        // Do not submit another account's password or an external account's password to any authority.
        if (pending.Authority is not ("local" or "directory")) return LoginResult.InvalidCredentials();
        var user = await users.FindByEmailAsync(login) ?? await users.FindByNameAsync(login);
        if (user?.Id != pending.AccountId) return LoginResult.InvalidCredentials();
        var service = http.RequestServices.GetRequiredService<RecoveryEmailStepUpService>();
        var state = await service.ResolveAsync(user.Id, ct);
        if (state?.Binding != pending.Binding || state.User.SecurityStamp != pending.Stamp) return LoginResult.InvalidCredentials();
        var result = await loginService.AuthenticateAsync(login, password, ct);
        if (result.Status == LoginStatus.Success && result.User?.Id == pending.AccountId)
            http.Session.SetString(PendingKey, JsonSerializer.Serialize(pending with { PrimaryAtUtc = DateTimeOffset.UtcNow }));
        return result;
    }

    // Called only immediately after a successful existing factor verification.
    public static void MarkFullCompletion(HttpContext http, Guid accountId, bool hardware = false)
    {
        var pending = Read(http);
        if (pending?.AccountId != accountId || !hardware && pending.PrimaryAtUtc is null) return;
        http.Items[CompletionKey] = hardware ? pending with { PrimaryAtUtc = DateTimeOffset.UtcNow } : pending;
    }

    public static async Task CompleteAsync(HttpContext http, ClaimsPrincipal principal)
    {
        if (!http.Items.Remove(CompletionKey, out var value) || value is not Pending pending) return;
        var account = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        if (!Guid.TryParse(account, out var id) || id != pending.AccountId || pending.PrimaryAtUtc is null ||
            !principal.Claims.Any(c => (c.Type == AuthConstants.ClaimTypes.Amr || c.Type == AuthConstants.ClaimTypes.AuthenticationMethod) &&
                c.Value is AuthConstants.Amr.Mfa or AuthConstants.Amr.HardwareKey)) return;
        var context = Context(http);
        var service = http.RequestServices.GetRequiredService<RecoveryEmailStepUpService>();
        var grant = await service.IssueAsync(id, pending.Stamp, pending.Binding, context.ContextHash, context.CsrfHash,
            pending.PrimaryAtUtc.Value, http.RequestAborted);
        http.Session.Remove(PendingKey);
        if (grant is { } grantId) http.Session.SetString(GrantKey, grantId.ToString("D"));
    }

    private static Pending? Read(HttpContext http)
    {
        var session = http.Features.Get<ISessionFeature>()?.Session;
        var raw = session?.GetString(PendingKey);
        if (raw is null) return null;
        try
        {
            var pending = JsonSerializer.Deserialize<Pending>(raw);
            if (pending?.ExpiresAtUtc > DateTimeOffset.UtcNow) return pending;
        }
        catch (JsonException) { }
        session!.Remove(PendingKey);
        return null;
    }

    private sealed record Pending(Guid AccountId, string Stamp, string Authority, string Binding,
        DateTimeOffset ExpiresAtUtc, DateTimeOffset? PrimaryAtUtc);
}
