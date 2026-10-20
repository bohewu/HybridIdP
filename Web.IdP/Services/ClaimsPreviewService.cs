using System.Security.Claims;
using System.Text.Json;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Application.Utilities;
using Infrastructure.Authorization;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using Web.IdP.Helpers;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Web.IdP.Services;

/// <summary>Projects hypothetical user claims. It never enters an authentication or issuance flow.</summary>
public sealed class ClaimsPreviewService(
    IApplicationDbContext db, IOpenIddictApplicationManager applications,
    IApiScopeUsagePolicy scopeUsage, IProviderProfilePreviewReader profiles,
    ClaimsEnrichmentService enrichment, IUserInfoService userInfo,
    ICurrentUserLifecycleEligibility lifecycle, IMigrationIssuanceGuard migration,
    IApiResourceService resources) : IClaimsPreviewService
{
    public async Task<ClaimsPreviewResponse> PreviewAsync(ClaimsPreviewRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId) || request.ClientId.Length > 200 || request.UserId == Guid.Empty ||
            request.Scopes is null || request.Scopes.Length > 64 ||
            request.Scopes.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 200 || s.Any(char.IsWhiteSpace)))
            throw new ClaimsPreviewException("invalid_request");
        var application = await FindClientAsync(request.ClientId, cancellationToken);
        var permissions = await applications.GetPermissionsAsync(application, cancellationToken);
        if (!SupportsUserFlow(permissions)) throw new ClaimsPreviewException("unsupported_client");
        var selected = request.Scopes.Distinct(StringComparer.Ordinal).ToArray();
        if (!await scopeUsage.CanUseScopesAsync(application, selected, cancellationToken))
            throw new ClaimsPreviewException("scope_not_allowed");
        var user = await db.Users.AsNoTracking().Include(u => u.Person)
            .SingleOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new ClaimsPreviewException("account_not_found");
        if (!user.IsActive || user.IsDeleted || user.LockoutEnd > DateTimeOffset.UtcNow ||
            (user.PersonId.HasValue && user.Person?.CanAuthenticate() != true) ||
            !await migration.CanIssueAsync(user.Id, cancellationToken) ||
            !await lifecycle.IsEligibleAsync(user.Id, cancellationToken))
            throw new ClaimsPreviewException("account_ineligible");

        var allowed = ScopeNames(permissions);
        var mappings = await db.ScopeClaims.AsNoTracking().Include(m => m.ClaimDefinition)
            .Where(m => allowed.Contains(m.ScopeName)).ToListAsync(cancellationToken);
        var snapshots = new Dictionary<string, ProviderProfileSnapshotResult>(StringComparer.Ordinal);
        foreach (var source in mappings.Where(m => selected.Contains(m.ScopeName))
                     .Select(m => m.ClaimDefinition?.ProviderProfileSource).OfType<string>().Distinct(StringComparer.Ordinal))
            snapshots[source] = await profiles.ReadForPreviewAsync(user, source, cancellationToken);

        var identity = new ClaimsIdentity();
        identity.SetClaim(Claims.Subject, user.Id.ToString())
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.Name, NameFormatter.BuildDisplayName(user.FirstName, user.MiddleName, user.LastName) ?? user.UserName)
            .SetClaim(Claims.PreferredUsername, user.UserName);
        var omissions = new Dictionary<string, string>(StringComparer.Ordinal);
        await enrichment.AddAppSpecificRolesAsync(identity, user, request.ClientId, cancellationToken);
        await enrichment.AddPreviewMappedClaimsAsync(identity, user, selected, snapshots,
            (claim, code) => omissions[claim] = code, cancellationToken);
        identity.SetScopes(selected);
        UserTokenClaimScopes.Apply(identity);
        identity.SetDestinations(TokenService.GetDestinations);

        var idToken = selected.Contains(Scopes.OpenId) ? Payload(identity, Destinations.IdentityToken) : [];
        var accessToken = Payload(identity, Destinations.AccessToken);
        // These are known request/resource metadata, not a generated token or issuance timestamp.
        if (selected.Length > 0) accessToken[Claims.Scope] = JsonSerializer.SerializeToElement(string.Join(" ", selected));
        var audiences = (await resources.GetAudiencesByScopesAsync(selected)).Distinct(StringComparer.Ordinal).ToArray();
        if (audiences.Length > 0) accessToken[Claims.Audience] = JsonSerializer.SerializeToElement(audiences);
        var userInfoPayload = (await userInfo.GetUserInfoAsync(new ClaimsPrincipal(identity), cancellationToken))
            .ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value), StringComparer.Ordinal);

        var candidates = mappings.Select(m => m.ClaimDefinition?.ClaimType).OfType<string>()
            .Concat(new[] { Claims.Subject, Claims.Name, Claims.PreferredUsername, Claims.Email, Claims.Role, "app_role",
                "amr", "auth_time", "acr", "jti", "iat", "exp", "nbf", "nonce", "sid", "iss", "aud", "scope" })
            .Concat(idToken.Keys).Concat(accessToken.Keys).Concat(userInfoPayload.Keys).Distinct(StringComparer.Ordinal);
        var reasons = new List<ClaimsPreviewReasonDto>();
        foreach (var claim in candidates)
        {
            var maps = mappings.Where(m => m.ClaimDefinition?.ClaimType == claim).ToArray();
            var mappedSelected = maps.Where(m => selected.Contains(m.ScopeName)).ToArray();
            var unavailable = mappedSelected.Select(m => m.ClaimDefinition?.ProviderProfileSource).OfType<string>()
                .Select(s => snapshots[s]).FirstOrDefault(s => s.Status != "confirmed");
            foreach (var (destination, payload) in new[] { ("idToken", idToken), ("accessToken", accessToken), ("userInfo", userInfoPayload) })
            {
                var included = payload.ContainsKey(claim);
                var code = included ? "included" :
                    claim is "amr" or "auth_time" or "acr" ? "authentication_not_performed" :
                    claim is "jti" or "iat" or "exp" or "nbf" or "nonce" or "sid" or "iss" ? "issuance_not_performed" :
                    destination == "idToken" && !selected.Contains(Scopes.OpenId) ? "openid_not_selected" :
                    UserTokenClaimScopes.RequiredScope(claim) is { } required && !selected.Contains(required) ? "scope_not_selected" :
                    maps.Length > 0 && mappedSelected.Length == 0 ? "scope_not_selected" :
                    unavailable is not null ? "source_" + unavailable.Status :
                    omissions.TryGetValue(claim, out var omitted) ? omitted : "not_projected_for_destination";
                var unknown = code is "authentication_not_performed" or "issuance_not_performed" or "condition_input_unknown" or "source_property_unknown" ||
                    (code.StartsWith("source_", StringComparison.Ordinal) && code != "source_property_rejected");
                reasons.Add(new(claim, destination, included ? "included" : unknown ? "unknown" : "omitted", code));
            }
        }
        return new(request.ClientId, user.Id, selected, true, idToken, accessToken, userInfoPayload, reasons,
            snapshots.Select(s => new ClaimsPreviewSourceDto(s.Key, s.Value.Status, s.Value.ConfirmedAt, s.Value.ExpiresAt)).ToArray());
    }

    public async Task<IReadOnlyList<ClaimsPreviewClientDto>> GetClientsAsync(string? search, int skip, int take, CancellationToken cancellationToken = default)
    {
        if (search?.Length > 200) throw new ClaimsPreviewException("invalid_request");
        var term = search?.Trim();
        var result = new List<ClaimsPreviewClientDto>();
        await foreach (var application in applications.ListAsync(query =>
        {
            var clients = query.Cast<OpenIddictEntityFrameworkCoreApplication<Guid>>().Where(a => a.ClientId != null);
            if (!string.IsNullOrEmpty(term))
                clients = clients.Where(a => a.ClientId!.Contains(term) || (a.DisplayName != null && a.DisplayName.Contains(term)));
            return clients.OrderBy(a => a.ClientId).ThenBy(a => a.Id).Skip(Math.Max(skip, 0)).Take(Math.Clamp(take, 1, 100))
                .Select(a => new { a.ClientId, a.DisplayName, a.Permissions });
        }, cancellationToken))
        {
            result.Add(new(application.ClientId!, application.DisplayName ?? application.ClientId!,
                SupportsUserFlow(JsonSerializer.Deserialize<string[]>(application.Permissions ?? "[]") ?? [])));
        }
        return result;
    }

    public async Task<IReadOnlyList<ClaimsPreviewAccountDto>> GetAccountsAsync(string? search, int skip, int take, CancellationToken cancellationToken = default)
    {
        if (search?.Length > 200) throw new ClaimsPreviewException("invalid_request");
        var query = db.Users.AsNoTracking().Where(u => !u.IsDeleted);
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term)) query = query.Where(u => u.UserName != null && u.UserName.Contains(term));
        return await query.OrderBy(u => u.UserName).ThenBy(u => u.Id).Skip(Math.Max(skip, 0)).Take(Math.Clamp(take, 1, 100))
            .Select(u => new ClaimsPreviewAccountDto(u.Id, u.UserName ?? u.Id.ToString())).ToListAsync(cancellationToken);
    }

    public async Task<ClaimsPreviewClientScopesDto> GetScopesAsync(string clientId, CancellationToken cancellationToken = default)
    {
        var application = await FindClientAsync(clientId, cancellationToken);
        var permissions = await applications.GetPermissionsAsync(application, cancellationToken);
        var result = new List<ClaimsPreviewScopeDto>();
        foreach (var scope in ScopeNames(permissions).Order(StringComparer.Ordinal))
            result.Add(new(scope, await scopeUsage.CanUseScopesAsync(application, new[] { scope }, cancellationToken)));
        return new(clientId, SupportsUserFlow(permissions), result);
    }

    private async Task<object> FindClientAsync(string clientId, CancellationToken cancellationToken) =>
        await applications.FindByClientIdAsync(clientId, cancellationToken) ?? throw new ClaimsPreviewException("client_not_found");

    private static bool SupportsUserFlow(IEnumerable<string> permissions) =>
        !permissions.Contains(Permissions.GrantTypes.ClientCredentials) &&
        permissions.Any(p => p is Permissions.GrantTypes.AuthorizationCode or Permissions.GrantTypes.Password or Permissions.GrantTypes.DeviceCode);

    private static string[] ScopeNames(IEnumerable<string> permissions) => permissions
        .Where(p => p.StartsWith(Permissions.Prefixes.Scope, StringComparison.Ordinal))
        .Select(p => p[Permissions.Prefixes.Scope.Length..]).Distinct(StringComparer.Ordinal).ToArray();

    private static Dictionary<string, JsonElement> Payload(ClaimsIdentity identity, string destination) => identity.Claims
        .Where(c => !c.Type.StartsWith("oi_", StringComparison.Ordinal) && c.GetDestinations().Contains(destination))
        .GroupBy(c => c.Type, StringComparer.Ordinal).ToDictionary(group => group.Key, group =>
        {
            var values = group.Select(c => c.ValueType == ClaimValueTypes.Boolean && bool.TryParse(c.Value, out var flag)
                ? JsonSerializer.SerializeToElement(flag)
                : c.ValueType == Microsoft.IdentityModel.JsonWebTokens.JsonClaimValueTypes.JsonArray
                    ? JsonSerializer.Deserialize<JsonElement>(c.Value) : JsonSerializer.SerializeToElement(c.Value)).ToArray();
            return values.Length == 1 ? values[0] : JsonSerializer.SerializeToElement(values);
        }, StringComparer.Ordinal);
}
