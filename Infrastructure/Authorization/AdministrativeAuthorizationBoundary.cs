using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;

namespace Infrastructure.Authorization;

public sealed record AdministrativeAuthority(ClaimsPrincipal Principal, bool IsBearer, IReadOnlySet<string> Permissions);

public interface IAdministrativeAuthorizationBoundary
{
    Task<AdministrativeAuthority?> ResolveAsync();
}

public sealed class AdministrativeAuthorizationBoundary(
    IHttpContextAccessor accessor, IOpenIddictApplicationManager applications) : IAdministrativeAuthorizationBoundary
{
    public const string AuthorityKey = "Idp:AdministrativeAuthority";
    public const string BearerScheme = "OpenIddict.Validation.AspNetCore";

    public async Task<AdministrativeAuthority?> ResolveAsync()
    {
        var context = accessor.HttpContext;
        if (context == null) return null;
        var bearer = await context.AuthenticateAsync(BearerScheme);
        if (bearer.Succeeded && bearer.Principal != null)
        {
            var id = bearer.Principal.FindFirst(AdministrativeClientGrant.ApplicationClaim)?.Value;
            if (string.IsNullOrEmpty(id)) return null;
            var application = await applications.FindByIdAsync(id, context.RequestAborted);
            if (application == null) return null;
            var clientId = await applications.GetClientIdAsync(application, context.RequestAborted);
            if (string.IsNullOrEmpty(clientId) || !bearer.Principal.GetPresenters().Contains(clientId, StringComparer.Ordinal) ||
                bearer.Principal.GetClaim(OpenIddictConstants.Claims.Subject) != clientId)
                return null;
            var ceiling = AdministrativeClientGrant.ReadPermissions(await applications.GetPropertiesAsync(application, context.RequestAborted));
            var permissions = bearer.Principal.GetScopes().Where(ceiling.Contains).ToHashSet(StringComparer.Ordinal);
            if (permissions.Count == 0) return null;
            var authority = new AdministrativeAuthority(bearer.Principal, true, permissions);
            context.Items[AuthorityKey] = authority;
            return authority;
        }
        if (context.Request.Headers.Authorization.Any(value => value?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true))
            return null;
        var cookie = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        return cookie.Succeeded && cookie.Principal != null
            ? new AdministrativeAuthority(cookie.Principal, false, new HashSet<string>()) : null;
    }
}
