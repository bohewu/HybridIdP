using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Constants;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;
using Web.IdP.Services; // Ensure namespace matches interface

namespace Web.IdP.Services // Keep consistent namespace case
{
    public partial class AuthorizationService : Web.IdP.Services.IAuthorizationService
    {
        private readonly IOpenIddictApplicationManager _applicationManager;
        private readonly IOpenIddictAuthorizationManager _authorizationManager;
        private readonly IOpenIddictScopeManager _scopeManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<ApplicationRole> _roleManager;
        private readonly IApplicationDbContext _db;
        private readonly IApiResourceService _apiResourceService;
        private readonly ILocalizationService _localizationService;
        private readonly IScopeService _scopeService;
        private readonly IAuditService _auditService;
        private readonly IClientAllowedScopesService _clientAllowedScopesService;
        private readonly IClientScopeRequestProcessor _clientScopeProcessor;
        private readonly ILogger<AuthorizationService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IClaimsEnrichmentService _claimsEnricher;

        public AuthorizationService(
            IOpenIddictApplicationManager applicationManager,
            IOpenIddictAuthorizationManager authorizationManager,
            IOpenIddictScopeManager scopeManager,
            UserManager<ApplicationUser> userManager,
            RoleManager<ApplicationRole> roleManager,
            IApplicationDbContext db,
            IApiResourceService apiResourceService,
            ILocalizationService localizationService,
            IScopeService scopeService,
            IAuditService auditService,
            IClientAllowedScopesService clientAllowedScopesService,
            IClientScopeRequestProcessor clientScopeProcessor,
            ILogger<AuthorizationService> logger,
            IHttpContextAccessor httpContextAccessor,
            IClaimsEnrichmentService claimsEnricher)
        {
            _applicationManager = applicationManager;
            _authorizationManager = authorizationManager;
            _scopeManager = scopeManager;
            _userManager = userManager;
            _roleManager = roleManager;
            _db = db;
            _apiResourceService = apiResourceService;
            _localizationService = localizationService;
            _scopeService = scopeService;
            _auditService = auditService;
            _clientAllowedScopesService = clientAllowedScopesService;
            _clientScopeProcessor = clientScopeProcessor;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            _claimsEnricher = claimsEnricher;
        }

        private HttpContext HttpContext => _httpContextAccessor.HttpContext ?? throw new InvalidOperationException("HttpContext is not available.");
        private HttpRequest Request => HttpContext.Request;

        // Properties to be accessed by Controller for View rendering
        public string? ApplicationName { get; private set; }
        public string? Scope { get; private set; }
        public List<ScopeInfo> ScopeInfos { get; private set; } = new();


        public async Task<IActionResult> HandleAuthorizeRequestAsync(ClaimsPrincipal? userPrincipal, OpenIddictRequest request, string? prompt)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            // Retrieve the user principal stored in the authentication cookie
            // Using the passed principal if available (from Controller which might have already authenticated)
            // But logic in PageModel was calling AuthenticateAsync again.
            // Let's rely on the passed principal as the primary source if it's treated as authenticated.

            if (userPrincipal?.Identity?.IsAuthenticated != true)
            {
                 return new ChallengeResult(
                    authenticationSchemes: new[] { IdentityConstants.ApplicationScheme },
                    properties: new AuthenticationProperties
                    {
                        RedirectUri = Request.PathBase + Request.Path + Request.QueryString + (Request.QueryString.HasValue ? "&" : "?") + "prompt=login"
                    });
            }

            // Phase: acr_values=mfa enforcement
            var acrValues = request.GetAcrValues();
            if (acrValues.Contains("mfa"))
            {
                var amrClaims = userPrincipal.FindAll("amr").Select(c => c.Value).ToList();
                if (!amrClaims.Contains(AuthConstants.Amr.Mfa))
                {
                    var amrList = string.Join(", ", amrClaims);
                    _logger.LogInformation("MFA required by acr_values but not present in principal. Challenging user.");
                    return new ChallengeResult(
                        authenticationSchemes: new[] { IdentityConstants.ApplicationScheme },
                        properties: new AuthenticationProperties
                        {
                            RedirectUri = Request.PathBase + Request.Path + Request.QueryString + (Request.QueryString.HasValue ? "&" : "?") + "prompt=login",
                            Items = { ["prompt"] = "login" }
                        });
                }
            }

            // Retrieve the application details from the database
            var application = await _applicationManager.FindByClientIdAsync(request.ClientId!) ??
                throw new InvalidOperationException("Details concerning the calling client application cannot be found.");
            var applicationId = await _applicationManager.GetIdAsync(application)
                ?? throw new InvalidOperationException("The application identifier cannot be resolved.");

            ApplicationName = await _applicationManager.GetDisplayNameAsync(application);
            Scope = request.Scope;

            // Validate Response Type Permissions
            // In Passthrough mode, we must manually enforce that the client is allowed to use the requested response types.
            var permissions = await _applicationManager.GetPermissionsAsync(application);
            
            if (request.HasResponseType(ResponseTypes.Code) && !permissions.Contains(OpenIddictConstants.Permissions.ResponseTypes.Code))
            {
                _logger.LogWarning("Client {ClientId} requested response_type=code without permission.", request.ClientId);
                return new ForbidResult(
                    authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.UnauthorizedClient,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The client is not authorized to use 'response_type=code'."
                    }));
            }

            if (request.HasResponseType(ResponseTypes.Token) && !permissions.Contains(OpenIddictConstants.Permissions.ResponseTypes.Token))
            {
                _logger.LogWarning("Client {ClientId} requested response_type=token without permission.", request.ClientId);
                 return new ForbidResult(
                    authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.UnauthorizedClient,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The client is not authorized to use 'response_type=token'."
                    }));
            }

            if (request.HasResponseType(ResponseTypes.IdToken) && !permissions.Contains(OpenIddictConstants.Permissions.ResponseTypes.IdToken))
            {
                _logger.LogWarning("Client {ClientId} requested response_type=id_token without permission.", request.ClientId);
                 return new ForbidResult(
                    authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.UnauthorizedClient,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The client is not authorized to use 'response_type=id_token'."
                    }));
            }

            var requestedScopes = request.GetScopes();
            var clientGuid = Guid.Parse(applicationId);
            var eval = await _clientScopeProcessor.EnforceAsync(clientGuid, requestedScopes, logAuditIfRestricted: true);
            var effectiveRequestedScopes = eval.AllowedScopes.ToImmutableArray();

            // Fetch scope information only for allowed scopes
            await LoadScopeInfosAsync(effectiveRequestedScopes, clientGuid);

            // Retrieve the permanent authorizations associated with the user and the calling client application
            var userId = userPrincipal.GetClaim(Claims.Subject)!;
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
            if (authorizations.Count > 0 && prompt != "consent")
            {
                 // If a permanent authorization was found, return immediately
                // Create a clean identity without ASP.NET Identity cookie claims to avoid duplicates
                var identity = new ClaimsIdentity(
                    authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

                // Add custom claims (email, roles, etc.)
                var user = await _userManager.GetUserAsync(userPrincipal);
                if (user != null)
                {
                    identity.SetClaim(Claims.Subject, await _userManager.GetUserIdAsync(user))
                        .SetClaim(Claims.Email, await _userManager.GetEmailAsync(user))
                        .SetClaim(Claims.Name, await _userManager.GetUserNameAsync(user));

                    var roles = await _userManager.GetRolesAsync(user);
                    identity.SetClaims(Claims.Role, [..roles]);

                    // Enrichment
                    await _claimsEnricher.AddPermissionClaimsAsync(identity, user);
                    await _claimsEnricher.AddScopeMappedClaimsAsync(identity, user, scopes);

                    // Copy AMR claims from userPrincipal
                    foreach (var amr in userPrincipal.FindAll("amr"))
                    {
                        identity.AddClaim(new Claim("amr", amr.Value));
                    }

                    // Set ACR if requested and present
                    if (acrValues.Contains("mfa") && userPrincipal.FindAll("amr").Any(c => c.Value == AuthConstants.Amr.Mfa))
                    {
                        identity.SetClaim(Claims.AuthenticationContextReference, "mfa");
                    }
                }

                // Add audience (aud) claims from API Resources associated with requested scopes
                var audiences = await _apiResourceService.GetAudiencesByScopesAsync(scopes);
                if (audiences.Count > 0)
                {
                    identity.SetAudiences([..audiences]);
                    LogSettingAudiencesForExistingAuth(string.Join(", ", audiences));
                }
                else
                {
                    LogNoAudiencesForExistingAuth(string.Join(", ", scopes));
                }

                identity.SetDestinations(GetDestinations);

                return new Microsoft.AspNetCore.Mvc.SignInResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            }

            // Show consent View
            // The Controller will retrieve properties from this service to pass to View
            return new OkResult(); // Signal to controller to show View
        }

        public async Task<IActionResult> HandleAuthorizeSubmitAsync(ClaimsPrincipal? userPrincipal, OpenIddictRequest request, string? submit, string[]? granted_scopes)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            // Resolve the calling application and its identifier (GUID key)
            var application = await _applicationManager.FindByClientIdAsync(request.ClientId!) ??
                throw new InvalidOperationException("Details concerning the calling client application cannot be found.");
            var applicationId = await _applicationManager.GetIdAsync(application)
                ?? throw new InvalidOperationException("The application identifier cannot be resolved.");

            if (userPrincipal?.Identity?.IsAuthenticated != true)
            {
                 return new ChallengeResult(
                    authenticationSchemes: new[] { IdentityConstants.ApplicationScheme },
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
                await _auditService.LogEventAsync("AuthorizationDenied", userPrincipal.GetClaim(Claims.Subject), denyDetails, ip, ua);

                return new ForbidResult(
                    authenticationSchemes: new []{ OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.AccessDenied,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The authorization was denied by the user"
                    }));
            }

            var user = await _userManager.GetUserAsync(userPrincipal) ??
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
            await _claimsEnricher.AddPermissionClaimsAsync(identity, user);

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
            
            LogTamperingCheck(
                request.ClientId,
                string.Join(",", clientRequiredScopes),
                string.Join(",", grantedSet),
                string.Join(",", missingRequired));
            
            if (missingRequired.Count > 0)
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
                await _auditService.LogEventAsync("ConsentTamperingDetected", userPrincipal.GetClaim(Claims.Subject), tamperDetails, ip, ua);
                
                // Return BadRequest. NOTE: In a real controller this would be BadRequestResult or ContentResult.
                // Here we might need a custom result type or just return a simple IActionResult that Controller can interpret.
                // For now, let's assume we return a Content result or similar, but the interface says IActionResult.
                // To match PageModel behavior: return BadRequest("...");
                return new BadRequestObjectResult("Required scopes cannot be excluded from consent.");
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
            await _claimsEnricher.AddScopeMappedClaimsAsync(identity, user, effectiveScopes);

            // Copy AMR claims from userPrincipal
            foreach (var amr in userPrincipal.FindAll("amr"))
            {
                identity.AddClaim(new Claim("amr", amr.Value));
            }

            // Set ACR if requested and present
            var acrValuesSubmit = request.GetAcrValues();
            if (acrValuesSubmit.Contains("mfa") && userPrincipal.FindAll("amr").Any(c => c.Value == AuthConstants.Amr.Mfa))
            {
                identity.SetClaim(Claims.AuthenticationContextReference, "mfa");
            }

            // Audiences based on effective scopes
            var audiences = await _apiResourceService.GetAudiencesByScopesAsync(effectiveScopes);
            if (audiences.Count > 0)
            {
                identity.SetAudiences(audiences.ToImmutableArray());
                LogSettingAudiencesForAuth(string.Join(", ", audiences));
            }
            else
            {
                LogNoAudiencesForAuth(string.Join(", ", effectiveScopes));
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

            return new Microsoft.AspNetCore.Mvc.SignInResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        }

        // Helper methods copied and adapted from PageModel
         private static IEnumerable<string> GetDestinations(Claim claim)
        {
            // Note: by default, claims are NOT automatically included in the access and identity tokens.
            // To allow OpenIddict to serialize them, you must attach them a destination, that specifies
            // whether they should be included in access tokens, in identity tokens or in both.

            switch (claim.Type)
            {
                case Claims.Name:
                case Claims.Email:
                case Claims.Subject:
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

                case "amr":
                    yield return Destinations.AccessToken;
                    yield return Destinations.IdentityToken;
                    yield break;

                case Claims.AuthenticationContextReference:
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



        [LoggerMessage(Level = LogLevel.Information, Message = "Setting audiences for existing authorization: {Audiences}")]
        partial void LogSettingAudiencesForExistingAuth(string audiences);

        [LoggerMessage(Level = LogLevel.Information, Message = "No audiences found for existing authorization with scopes: {Scopes}")]
        partial void LogNoAudiencesForExistingAuth(string scopes);

        [LoggerMessage(Level = LogLevel.Information, Message = "Tampering check: clientId={ClientId}, requiredScopes={Required}, grantedScopes={Granted}, missing={Missing}")]
        partial void LogTamperingCheck(string? clientId, string required, string granted, string missing);

        [LoggerMessage(Level = LogLevel.Information, Message = "Setting audiences for authorization: {Audiences}")]
        partial void LogSettingAudiencesForAuth(string audiences);

        [LoggerMessage(Level = LogLevel.Information, Message = "No audiences found for effective scopes: {Scopes}")]
        partial void LogNoAudiencesForAuth(string scopes);
    }
}
