using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using System.Collections.Immutable;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;
using Infrastructure;
using Core.Domain.Entities;
using Core.Application;

namespace Web.IdP.Pages.Connect;

[Authorize]
public class AuthorizeModel : PageModel
{
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictAuthorizationManager _authorizationManager;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly IApiResourceService _apiResourceService;
    private readonly ILogger<AuthorizeModel> _logger;

    public AuthorizeModel(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictAuthorizationManager authorizationManager,
        IOpenIddictScopeManager scopeManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db,
        IApiResourceService apiResourceService,
        ILogger<AuthorizeModel> logger)
    {
        _applicationManager = applicationManager;
        _authorizationManager = authorizationManager;
        _scopeManager = scopeManager;
        _userManager = userManager;
        _db = db;
        _apiResourceService = apiResourceService;
        _logger = logger;
    }

    public string? ApplicationName { get; set; }
    public string? Scope { get; set; }
    public List<ScopeInfo> ScopeInfos { get; set; } = new();

    public class ScopeInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? ConsentDisplayName { get; set; }
        public string? ConsentDescription { get; set; }
        public string? IconUrl { get; set; }
        public bool IsRequired { get; set; }
        public int DisplayOrder { get; set; }
        public string? Category { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // Retrieve the user principal stored in the authentication cookie
        var result = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (result?.Principal == null)
        {
            return Challenge(
                authenticationSchemes: IdentityConstants.ApplicationScheme,
                properties: new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + QueryString.Create(
                        Request.HasFormContentType ? Request.Form.ToList() : Request.Query.ToList())
                });
        }

        // Retrieve the application details from the database
        var application = await _applicationManager.FindByClientIdAsync(request.ClientId!) ??
            throw new InvalidOperationException("Details concerning the calling client application cannot be found.");
        var applicationId = await _applicationManager.GetIdAsync(application)
            ?? throw new InvalidOperationException("The application identifier cannot be resolved.");

        ApplicationName = await _applicationManager.GetDisplayNameAsync(application);
        Scope = request.Scope;

        // Fetch scope information with extensions for consent screen
        await LoadScopeInfosAsync(request.GetScopes());

        // Retrieve the permanent authorizations associated with the user and the calling client application
        var userId = result.Principal.GetClaim(Claims.Subject)!;
        var scopes = request.GetScopes();
        
        var authorizationsEnumerable = _authorizationManager.FindAsync(
            subject: userId,
            client: applicationId,
            status: Statuses.Valid,
            type: AuthorizationTypes.Permanent,
            scopes: scopes);

        var authorizations = new List<object>();
        await foreach (var authorization in authorizationsEnumerable)
        {
            authorizations.Add(authorization);
        }

        // Always show consent page for first time or if prompt=consent is requested
        // In production, you may skip consent if authorization already exists
        if (authorizations.Any())
        {
            // If a permanent authorization was found, return immediately
            // Create a clean identity without ASP.NET Identity cookie claims to avoid duplicates
            var identity = new ClaimsIdentity(
                authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            // Add custom claims (email, roles, etc.)
            var user = await _userManager.GetUserAsync(result.Principal);
            if (user != null)
            {
                identity.SetClaim(Claims.Subject, await _userManager.GetUserIdAsync(user))
                    .SetClaim(Claims.Email, await _userManager.GetEmailAsync(user))
                    .SetClaim(Claims.Name, await _userManager.GetUserNameAsync(user));

                var roles = await _userManager.GetRolesAsync(user);
                identity.SetClaims(Claims.Role, roles.ToImmutableArray());

                // Enrich with scope-mapped claims from DB based on requested scopes
                await AddScopeMappedClaimsAsync(identity, user, scopes.ToImmutableArray());
            }

            // Add audience (aud) claims from API Resources associated with requested scopes
            var audiences = await _apiResourceService.GetAudiencesByScopesAsync(scopes);
            if (audiences.Any())
            {
                identity.SetAudiences(audiences.ToImmutableArray());
                _logger.LogInformation("Setting audiences for existing authorization: {Audiences}", string.Join(", ", audiences));
            }
            else
            {
                _logger.LogInformation("No audiences found for existing authorization with scopes: {Scopes}", string.Join(", ", scopes));
            }

            identity.SetDestinations(GetDestinations);

            return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        // Show consent page
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? submit)
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // Resolve the calling application and its identifier (GUID key)
        var application = await _applicationManager.FindByClientIdAsync(request.ClientId!) ??
            throw new InvalidOperationException("Details concerning the calling client application cannot be found.");
        var applicationId = await _applicationManager.GetIdAsync(application)
            ?? throw new InvalidOperationException("The application identifier cannot be resolved.");

        // Retrieve the user principal stored in the authentication cookie
        var result = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (result?.Principal == null)
        {
            return Challenge(
                authenticationSchemes: IdentityConstants.ApplicationScheme,
                properties: new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + QueryString.Create(
                        Request.HasFormContentType ? Request.Form.ToList() : Request.Query.ToList())
                });
        }

        // User denied consent
        if (submit == "deny")
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.AccessDenied,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The authorization was denied by the user"
                }));
        }

        var user = await _userManager.GetUserAsync(result.Principal) ??
            throw new InvalidOperationException("The user details cannot be retrieved.");

        // Create a clean ClaimsIdentity without copying ASP.NET Identity cookie claims to avoid duplicates
        var identity = new ClaimsIdentity(
            authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        // Add custom claims
        identity.SetClaim(Claims.Subject, await _userManager.GetUserIdAsync(user))
            .SetClaim(Claims.Email, await _userManager.GetEmailAsync(user))
            .SetClaim(Claims.Name, await _userManager.GetUserNameAsync(user));

        var roles = await _userManager.GetRolesAsync(user);
        identity.SetClaims(Claims.Role, roles.ToImmutableArray());

        // Enrich with scope-mapped claims from DB based on requested scopes
        var requestedScopes = request.GetScopes().ToImmutableArray();
        await AddScopeMappedClaimsAsync(identity, user, requestedScopes);

        // Add audience (aud) claims from API Resources associated with requested scopes
        var audiences = await _apiResourceService.GetAudiencesByScopesAsync(requestedScopes);
        if (audiences.Any())
        {
            identity.SetAudiences(audiences.ToImmutableArray());
            _logger.LogInformation("Setting audiences for authorization: {Audiences}", string.Join(", ", audiences));
        }
        else
        {
            _logger.LogInformation("No audiences found for requested scopes: {Scopes}", string.Join(", ", requestedScopes));
        }

        identity.SetDestinations(GetDestinations);

        // Determine authorization type based on client's consent type
        // If ConsentType is Explicit, create AdHoc (temporary) authorization that requires consent each time
        // Otherwise, create Permanent authorization that persists across sessions
        var consentType = await _applicationManager.GetConsentTypeAsync(application);
        var authorizationType = consentType == ConsentTypes.Explicit 
            ? AuthorizationTypes.AdHoc 
            : AuthorizationTypes.Permanent;

        var authorization = await _authorizationManager.CreateAsync(
            identity: identity,
            subject: await _userManager.GetUserIdAsync(user),
            client: applicationId,
            type: authorizationType,
            scopes: identity.GetScopes());

        identity.SetAuthorizationId(await _authorizationManager.GetIdAsync(authorization));

        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        // Note: by default, claims are NOT automatically included in the access and identity tokens.
        // To allow OpenIddict to serialize them, you must attach them a destination, that specifies
        // whether they should be included in access tokens, in identity tokens or in both.

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

            // Custom claims we want in both tokens for client-side display
            case AuthConstants.Claims.PreferredUsername:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;

            case AuthConstants.Claims.Department:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;

            // Never include the security stamp in the access and identity tokens, as it's a secret value.
            case "AspNet.Identity.SecurityStamp":
                yield break;

            default:
                // Include custom/dynamic claims in both tokens by default
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;
        }
    }

    private async Task AddScopeMappedClaimsAsync(ClaimsIdentity identity, ApplicationUser user, ImmutableArray<string> requestedScopes)
    {
        if (requestedScopes.IsDefaultOrEmpty)
        {
            return;
        }

        var scopeNames = requestedScopes.ToArray();
        var mappings = await _db.ScopeClaims
            .Include(sc => sc.UserClaim)
            .Where(sc => scopeNames.Contains(sc.ScopeName))
            .ToListAsync();

        foreach (var map in mappings)
        {
            var def = map.UserClaim;
            if (def == null) continue;

            var value = ResolveUserProperty(user, def.UserPropertyPath);

            if (string.IsNullOrEmpty(value) && !map.AlwaysInclude)
            {
                continue;
            }

            if (identity.HasClaim(c => c.Type == def.ClaimType))
            {
                continue;
            }

            identity.SetClaim(def.ClaimType, value ?? string.Empty);
        }
    }

    private static string? ResolveUserProperty(ApplicationUser user, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        object? current = user;
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var seg in segments)
        {
            if (current == null) return null;
            var type = current.GetType();
            var prop = type.GetProperty(seg);
            if (prop == null) return null;
            current = prop.GetValue(current);
        }

        return current?.ToString();
    }

    private async Task LoadScopeInfosAsync(ImmutableArray<string> scopeNames)
    {
        ScopeInfos.Clear();
        
        if (scopeNames.IsDefaultOrEmpty)
            return;

        // Load all scope extensions for efficient lookup
        var scopeExtensions = await _db.ScopeExtensions.ToDictionaryAsync(se => se.ScopeId);

        foreach (var scopeName in scopeNames)
        {
            var scope = await _scopeManager.FindByNameAsync(scopeName);
            if (scope == null) continue;

            var scopeId = await _scopeManager.GetIdAsync(scope);
            var displayName = await _scopeManager.GetDisplayNameAsync(scope);

            // Get scope extension if exists
            scopeExtensions.TryGetValue(scopeId!, out var extension);

            ScopeInfos.Add(new ScopeInfo
            {
                Name = scopeName,
                DisplayName = displayName ?? scopeName,
                ConsentDisplayName = extension?.ConsentDisplayName,
                ConsentDescription = extension?.ConsentDescription,
                IconUrl = extension?.IconUrl,
                IsRequired = extension?.IsRequired ?? false,
                DisplayOrder = extension?.DisplayOrder ?? 0,
                Category = extension?.Category
            });
        }

        // Sort by DisplayOrder, then by Name
        ScopeInfos = ScopeInfos.OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name).ToList();
    }
}
