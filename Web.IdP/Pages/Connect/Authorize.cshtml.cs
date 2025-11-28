using Core.Domain;
using Core.Domain.Constants;
using Core.Application;
using Core.Application.DTOs;
using Infrastructure.Services;
using Infrastructure;
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
using System.Globalization;
using System.Text.Json;

namespace Web.IdP.Pages.Connect;

[Authorize]
[IgnoreAntiforgeryToken] // OAuth flows use state parameter for CSRF protection
public class AuthorizeModel : PageModel
{
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictAuthorizationManager _authorizationManager;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ApplicationDbContext _db;
    private readonly IApiResourceService _apiResourceService;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger<AuthorizeModel> _logger;
    private readonly IScopeService _scopeService;
    private readonly IAuditService _auditService;
    private readonly IClientAllowedScopesService _clientAllowedScopesService;
    private readonly ClientScopeRequestProcessor _clientScopeProcessor;

    public AuthorizeModel(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictAuthorizationManager authorizationManager,
        IOpenIddictScopeManager scopeManager,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ApplicationDbContext db,
        IApiResourceService apiResourceService,
        ILocalizationService localizationService,
        IScopeService scopeService,
        IAuditService auditService,
        IClientAllowedScopesService clientAllowedScopesService,
        ClientScopeRequestProcessor clientScopeProcessor,
        ILogger<AuthorizeModel> logger)
    {
        _applicationManager = applicationManager;
        _authorizationManager = authorizationManager;
        _scopeManager = scopeManager;
        _userManager = userManager;
        _roleManager = roleManager;
        _db = db;
        _apiResourceService = apiResourceService;
        _localizationService = localizationService;
        _logger = logger;
        _scopeService = scopeService;
        _auditService = auditService;
        _clientAllowedScopesService = clientAllowedScopesService;
        _clientScopeProcessor = clientScopeProcessor;
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

        var requestedScopes = request.GetScopes();
        var clientGuid = Guid.Parse(applicationId);
        var eval = await _clientScopeProcessor.EnforceAsync(clientGuid, requestedScopes, logAuditIfRestricted: true);
        var effectiveRequestedScopes = eval.AllowedScopes.ToImmutableArray();

        // Fetch scope information only for allowed scopes
        await LoadScopeInfosAsync(effectiveRequestedScopes, clientGuid);

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

                // Add permission claims from user's roles
                await AddPermissionClaimsAsync(identity, user);

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

    public async Task<IActionResult> OnPostAsync(string? submit, string[]? granted_scopes)
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

        // User denied consent -> audit and forbid
        if (submit == "deny")
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var ua = Request.Headers["User-Agent"].ToString();
            var requestScopes = request.GetScopes().ToImmutableArray();
            var denyDetails = JsonSerializer.Serialize(new
            {
                clientId = request.ClientId,
                requested = requestScopes,
                reason = "user_denied"
            });
            await _auditService.LogEventAsync("AuthorizationDenied", result.Principal.GetClaim(Claims.Subject), denyDetails, ip, ua);

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

        // Add permission claims from user's roles
        await AddPermissionClaimsAsync(identity, user);

        // Requested scopes
        // Enforce client scope policy again to guard against tampering
        var clientGuid = Guid.Parse(applicationId);
        var requestedScopesOriginal = request.GetScopes().ToImmutableArray();
        var eval = await _clientScopeProcessor.EnforceAsync(clientGuid, requestedScopesOriginal, logAuditIfRestricted: false);
        var requestedScopes = eval.AllowedScopes.ToImmutableArray();

        // Reload scope information for POST request (ScopeInfos is only populated in GET)
        await LoadScopeInfosAsync(requestedScopes, clientGuid);

        // Server-side validation: Ensure all required scopes are present in granted_scopes (prevent tampering)
        // This must be done BEFORE ClassifyScopes, because ClassifyScopes auto-adds required scopes
        var clientRequiredScopes = await _clientAllowedScopesService.GetRequiredScopesAsync(clientGuid);
        var grantedSet = (granted_scopes ?? Array.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingRequired = clientRequiredScopes.Except(grantedSet, StringComparer.OrdinalIgnoreCase).ToList();
        
        _logger.LogInformation("Tampering check: clientId={ClientId}, requiredScopes={Required}, grantedScopes={Granted}, missing={Missing}",
            request.ClientId,
            string.Join(",", clientRequiredScopes),
            string.Join(",", grantedSet),
            string.Join(",", missingRequired));
        
        if (missingRequired.Any())
        {
            // Log tampering attempt
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var ua = Request.Headers["User-Agent"].ToString();
            var tamperDetails = JsonSerializer.Serialize(new
            {
                clientId = request.ClientId,
                missingRequiredScopes = missingRequired,
                grantedScopes = granted_scopes ?? Array.Empty<string>()
            });
            await _auditService.LogEventAsync("ConsentTamperingDetected", result.Principal.GetClaim(Claims.Subject), tamperDetails, ip, ua);
            
            return BadRequest("Required scopes cannot be excluded from consent.");
        }

        // Build minimal available scope summaries from loaded consent info
        var availableSummaries = ScopeInfos.Select(s => new ScopeSummary
        {
            Name = s.Name,
            IsRequired = s.IsRequired
        }).ToList();

        // Classify scopes based on user consent input (this will auto-add required scopes)
        var classification = _scopeService.ClassifyScopes(requestedScopes, availableSummaries, granted_scopes);
        var effectiveScopes = classification.Allowed.ToImmutableArray();

        // Add claims mapped by effective (allowed) scopes
        await AddScopeMappedClaimsAsync(identity, user, effectiveScopes);

        // Audiences based on effective scopes
        var audiences = await _apiResourceService.GetAudiencesByScopesAsync(effectiveScopes);
        if (audiences.Any())
        {
            identity.SetAudiences(audiences.ToImmutableArray());
            _logger.LogInformation("Setting audiences for authorization: {Audiences}", string.Join(", ", audiences));
        }
        else
        {
            _logger.LogInformation("No audiences found for effective scopes: {Scopes}", string.Join(", ", effectiveScopes));
        }

        identity.SetScopes(effectiveScopes);
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
            scopes: effectiveScopes);

        identity.SetAuthorizationId(await _authorizationManager.GetIdAsync(authorization));

        // Structured audit log for full/partial grant
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers["User-Agent"].ToString();
        var auditDetails = JsonSerializer.Serialize(new
        {
            clientId = request.ClientId,
            requested = requestedScopes,
            allowed = classification.Allowed,
            required = classification.Required,
            rejected = classification.Rejected,
            isPartial = classification.IsPartialGrant,
            consentType,
            authorizationType
        });
        await _auditService.LogEventAsync(
            classification.IsPartialGrant ? "AuthorizationGrantedPartial" : "AuthorizationGrantedFull",
            await _userManager.GetUserIdAsync(user),
            auditDetails,
            ipAddress,
            userAgent);

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

    private async Task LoadScopeInfosAsync(ImmutableArray<string> scopeNames, Guid clientId)
    {
        ScopeInfos.Clear();
        
        if (scopeNames.IsDefaultOrEmpty)
            return;

        // Get user's culture for localization
        var culture = CultureInfo.CurrentCulture.Name;

        // Load all scope extensions for efficient lookup
        var scopeExtensions = await _db.ScopeExtensions.ToDictionaryAsync(se => se.ScopeId);

        // Load client-specific required scopes
        var clientRequiredScopes = await _clientAllowedScopesService.GetRequiredScopesAsync(clientId);
        var clientRequiredSet = clientRequiredScopes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var scopeName in scopeNames)
        {
            var scope = await _scopeManager.FindByNameAsync(scopeName);
            if (scope == null) continue;

            var scopeId = await _scopeManager.GetIdAsync(scope);
            var displayName = await _scopeManager.GetDisplayNameAsync(scope);

            // Get scope extension if exists
            scopeExtensions.TryGetValue(scopeId!, out var extension);

            // Get localized consent text
            string? consentDisplayName = null;
            string? consentDescription = null;
            if (extension != null)
            {
                if (!string.IsNullOrEmpty(extension.ConsentDisplayNameKey))
                {
                    consentDisplayName = await _localizationService.GetLocalizedStringAsync(extension.ConsentDisplayNameKey, culture);
                }
                if (!string.IsNullOrEmpty(extension.ConsentDescriptionKey))
                {
                    consentDescription = await _localizationService.GetLocalizedStringAsync(extension.ConsentDescriptionKey, culture);
                }
            }

            // Merge global and client-specific required flags
            var isGlobalRequired = extension?.IsRequired ?? false;
            var isClientRequired = clientRequiredSet.Contains(scopeName);

            ScopeInfos.Add(new ScopeInfo
            {
                Name = scopeName,
                DisplayName = displayName ?? scopeName,
                ConsentDisplayName = consentDisplayName,
                ConsentDescription = consentDescription,
                IconUrl = extension?.IconUrl,
                IsRequired = isGlobalRequired || isClientRequired,
                DisplayOrder = extension?.DisplayOrder ?? 0,
                Category = extension?.Category
            });
        }

        // Sort by DisplayOrder, then by Name
        ScopeInfos = ScopeInfos.OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name).ToList();
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

}
