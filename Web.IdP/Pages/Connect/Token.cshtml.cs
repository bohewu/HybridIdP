using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using System.Collections.Immutable;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;
using Core.Application;

namespace Web.IdP.Pages.Connect;

public class TokenModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IApiResourceService _apiResourceService;
    private readonly IAuditService _auditService;

    public TokenModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<ApplicationRole> roleManager,
        IApiResourceService apiResourceService,
        IAuditService auditService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _apiResourceService = apiResourceService;
        _auditService = auditService;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        ClaimsPrincipal claimsPrincipal;

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            // Retrieve the claims principal stored in the authorization code/refresh token
            claimsPrincipal = (await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal!;
        }
        else if (request.IsClientCredentialsGrantType())
        {
            // Client credentials grant for M2M authentication
            // Subject is the client_id (service account principal)
            var m2mClientId = request.ClientId;

            // Create the claims-based identity for the service account
            var identity = new ClaimsIdentity(
                authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                nameType: Claims.Name,
                roleType: Claims.Role);

            // Set subject to client_id for M2M clients
            identity.SetClaim(Claims.Subject, m2mClientId)
                    .SetClaim(Claims.Name, m2mClientId);

            // Add audience (aud) claims from API Resources associated with requested scopes
            var requestedScopes = request.GetScopes();
            var audiences = await _apiResourceService.GetAudiencesByScopesAsync(requestedScopes);
            if (audiences.Any())
            {
                identity.SetAudiences(audiences.ToImmutableArray());
            }

            identity.SetDestinations(GetDestinations);

            identity.SetDestinations(GetDestinations);

            claimsPrincipal = new ClaimsPrincipal(identity);
        }
        else if (request.GrantType == GrantTypes.DeviceCode)
        {
            // Retrieve the claims principal associated with the device code
            claimsPrincipal = (await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal!;
        }
        else if (request.IsPasswordGrantType())
        {
            // Password grant type (not used in authorization code flow, but included for completeness)
            var user = await _userManager.FindByNameAsync(request.Username!);
            if (user == null)
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The username/password couple is invalid."
                    }));
            }

            // Validate the username/password parameters and ensure the account is not locked out
            var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password!, lockoutOnFailure: true);
            if (!result.Succeeded)
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The username/password couple is invalid."
                    }));
            }

            // Create the claims-based identity
            var identity = new ClaimsIdentity(
                authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                nameType: Claims.Name,
                roleType: Claims.Role);

            // Add the claims
            identity.SetClaim(Claims.Subject, await _userManager.GetUserIdAsync(user))
                    .SetClaim(Claims.Email, await _userManager.GetEmailAsync(user))
                    .SetClaim(Claims.Name, await _userManager.GetUserNameAsync(user));

            var roles = await _userManager.GetRolesAsync(user);
            identity.SetClaims(Claims.Role, roles.ToImmutableArray());

            // Add permission claims from user's roles
            await AddPermissionClaimsAsync(identity, user);

            // Add audience (aud) claims from API Resources associated with requested scopes
            var requestedScopes = request.GetScopes();
            var audiences = await _apiResourceService.GetAudiencesByScopesAsync(requestedScopes);
            if (audiences.Any())
            {
                identity.SetAudiences(audiences.ToImmutableArray());
            }

            identity.SetDestinations(GetDestinations);

            claimsPrincipal = new ClaimsPrincipal(identity);
        }
        else
        {
            throw new InvalidOperationException("The specified grant type is not supported.");
        }

        // Log token issuance for audit
        var grantType = request.GrantType ?? "unknown";
        var userId = claimsPrincipal.GetClaim(Claims.Subject);
        var clientId = request.ClientId;
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = HttpContext.Request.Headers["User-Agent"].ToString();

        await _auditService.LogEventAsync(
            eventType: "token_issued",
            userId: userId,
            details: System.Text.Json.JsonSerializer.Serialize(new
            {
                grant_type = grantType,
                client_id = clientId,
                scopes = request.GetScopes().ToList()
            }),
            ipAddress: ipAddress,
            userAgent: userAgent
        );

        // Returning a SignInResult will ask OpenIddict to issue the appropriate access/identity tokens
        return SignIn(claimsPrincipal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task AddPermissionClaimsAsync(ClaimsIdentity identity, ApplicationUser user)
    {
        var userRoles = await _userManager.GetRolesAsync(user);
        var permissions = new HashSet<string>();

        foreach (var roleName in userRoles)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role != null && !string.IsNullOrWhiteSpace(role.Permissions))
            {
                // Parse permissions from the role's Permissions property (comma-separated string)
                var rolePermissions = role.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrEmpty(p));

                foreach (var permission in rolePermissions)
                {
                    permissions.Add(permission);
                }
            }
        }

        // Add permission claims to identity
        foreach (var permission in permissions)
        {
            identity.AddClaim(new Claim("permission", permission));
        }
    }

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        switch (claim.Type)
        {
            case Claims.Name:
            case Claims.Email:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;

            case Claims.Role:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;

            // Permission claims for authorization
            case "permission":
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;

            // Custom claims we want in both tokens for client-side display
            case AuthConstants.Claims.PreferredUsername:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;

            case AuthConstants.Claims.Department:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;

            case "AspNet.Identity.SecurityStamp":
                yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
