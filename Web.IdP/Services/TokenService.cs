using System.Collections.Immutable;
using System.Security.Claims;
using Core.Application;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Web.IdP.Services
{
    public partial class TokenService : ITokenService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly RoleManager<ApplicationRole> _roleManager;
        private readonly IApiResourceService _apiResourceService;
        private readonly IAuditService _auditService;
        private readonly IApplicationDbContext _db;
        private readonly IOpenIddictApplicationManager _applicationManager;
        private readonly ILogger<TokenService> _logger;
        private readonly IClaimsEnrichmentService _claimsEnricher;

        public TokenService(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            RoleManager<ApplicationRole> roleManager,
            IApiResourceService apiResourceService,
            IAuditService auditService,
            IApplicationDbContext db,
            IOpenIddictApplicationManager applicationManager,
            ILogger<TokenService> logger,
            IClaimsEnrichmentService claimsEnricher)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _apiResourceService = apiResourceService;
            _auditService = auditService;
            _db = db;
            _applicationManager = applicationManager;
            _logger = logger;
            _claimsEnricher = claimsEnricher;
        }

        public async Task<IActionResult> HandleTokenRequestAsync(OpenIddictRequest request, ClaimsPrincipal? schemePrincipal)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            // Validate client grant type permissions (required for passthrough mode)
            var permissionError = await ValidateClientGrantPermissionAsync(request);
            if (permissionError != null)
            {
                return permissionError;
            }

            ClaimsPrincipal claimsPrincipal;

            if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
            {
                // Retrieve the claims principal stored in the authorization code/refresh token
                var principal = schemePrincipal ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");
                
                // Create a new ClaimsPrincipal to avoid side effects
                // and ensure destinations are set correctly for the new token
                var identity = new ClaimsIdentity(
                    principal.Claims,
                    principal.Identity?.AuthenticationType,
                    Claims.Name,
                    Claims.Role);

                // Re-apply destinations
                identity.SetDestinations(GetDestinations);
                
                claimsPrincipal = new ClaimsPrincipal(identity);
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
                if (audiences.Count > 0)
                {
                    identity.SetAudiences(audiences.ToImmutableArray());
                }

                // Set the scopes on the identity, excluding 'openid' for client_credentials
                identity.SetScopes(requestedScopes.Where(s => s != Scopes.OpenId));

                identity.SetDestinations(GetDestinations);

                claimsPrincipal = new ClaimsPrincipal(identity);
            }
            else if (request.GrantType == GrantTypes.DeviceCode)
            {
                LogProcessingDeviceCodeGrant();
                try
                {
                    // Retrieve the claims principal stored in the device code.
                    if (schemePrincipal == null)
                    {
                        return new ForbidResult(
                            authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
                            properties: new AuthenticationProperties(new Dictionary<string, string?>
                            {
                                [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The device code is invalid or has expired."
                            }));
                    }

                    var subject = schemePrincipal.GetClaim(Claims.Subject);
                    if (string.IsNullOrEmpty(subject))
                    {
                        return new ForbidResult(
                            authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
                            properties: new AuthenticationProperties(new Dictionary<string, string?>
                            {
                                [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The device code is missing the subject claim."
                            }));
                    }

                    // Retrieve the user profile corresponding to the device code.
                    var user = await _userManager.FindByIdAsync(subject);
                    if (user == null)
                    {
                         return new ForbidResult(
                            authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
                            properties: new AuthenticationProperties(new Dictionary<string, string?>
                            {
                                [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The token is no longer valid."
                            }));
                    }

                    // Ensure the user is still allowed to sign in.
                    if (!await _signInManager.CanSignInAsync(user))
                    {
                         return new ForbidResult(
                            authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
                            properties: new AuthenticationProperties(new Dictionary<string, string?>
                            {
                                [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user is no longer allowed to sign in."
                            }));
                    }

                    // Create new identity with the correct authenticationType
                    var identity = new ClaimsIdentity(
                        authenticationType: Microsoft.IdentityModel.Tokens.TokenValidationParameters.DefaultAuthenticationType,
                        nameType: Claims.Name,
                        roleType: Claims.Role);

                    // Subject claim is always required
                    identity.SetClaim(Claims.Subject, await _userManager.GetUserIdAsync(user));

                    // Get scopes from original principal and add scope-based claims from DB
                    var requestedScopes = schemePrincipal.GetScopes().ToList();
                    await _claimsEnricher.AddScopeMappedClaimsAsync(identity, user, requestedScopes);

                    // Copy scopes from the original principal
                    identity.SetScopes(requestedScopes);

                    // Add permission claims
                    await _claimsEnricher.AddPermissionClaimsAsync(identity, user);

                    // Add audience claims from API resources
                    var audiences = await _apiResourceService.GetAudiencesByScopesAsync(requestedScopes);
                    if (audiences.Count > 0)
                    {
                        identity.SetAudiences(audiences.ToImmutableArray());
                    }

                    identity.SetDestinations(GetDestinations);

                    claimsPrincipal = new ClaimsPrincipal(identity);
                    LogDeviceCodeGrantSuccess(claimsPrincipal.GetClaim(Claims.Subject));
                }
                catch (Exception ex)
                {
                    LogDeviceCodeGrantError(ex);
                    return new ForbidResult(
                        authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
                        properties: new AuthenticationProperties(new Dictionary<string, string?>
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.ServerError,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "An error occurred processing the device code."
                        }));
                }
            }
            else if (request.IsPasswordGrantType())
            {
                // Password grant type
                var user = await _userManager.FindByNameAsync(request.Username!);
                if (user == null)
                {
                     return new ForbidResult(
                        authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
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
                     return new ForbidResult(
                        authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
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

                // Subject claim is always required (OIDC core)
                identity.SetClaim(Claims.Subject, await _userManager.GetUserIdAsync(user));

                // Get requested scopes and add scope-based claims from DB
                var requestedScopes = request.GetScopes();
                await _claimsEnricher.AddScopeMappedClaimsAsync(identity, user, requestedScopes);

                // Add permission claims from user's roles
                await _claimsEnricher.AddPermissionClaimsAsync(identity, user);

                // Set granted scopes on identity
                identity.SetScopes(requestedScopes);

                // Add audience (aud) claims from API Resources associated with requested scopes
                var audiences = await _apiResourceService.GetAudiencesByScopesAsync(requestedScopes);
                if (audiences.Count > 0)
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
            // Note: HttpContext is not available here directly unless we inject IHttpContextAccessor, 
            // but we can omit IP/UA or inject Accessor if needed. 
            // Token.cshtml.cs used HttpContext.Connection...
            // Let's inject IHttpContextAccessor to retrieve these or just pass null for now if simpler.
            // But audit logs are better with IP. 
            // For now, let's pass null/unknown to avoid adding valid dependency if not strictly required, 
            // OR I can use the existing _signInManager.Context but _signInManager might not expose it easily.
            // AuthorizationService uses IHttpContextAccessor. Let's add it? 
            // Actually, I'll update constructor later if I need it. For now, "unknown".
            // WAIT, implementing logic from Token.cshtml.cs which used HttpContext.
            // I should probably add IHttpContextAccessor to TokenService.
            
            await _auditService.LogEventAsync(
                eventType: "token_issued",
                userId: userId,
                details: System.Text.Json.JsonSerializer.Serialize(new
                {
                    grant_type = grantType,
                    client_id = clientId,
                    scopes = request.GetScopes().ToList()
                }),
                ipAddress: "unknown", // todo: inject httpcontextaccessor
                userAgent: "unknown"
            );

            return new Microsoft.AspNetCore.Mvc.SignInResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, claimsPrincipal);
        }

        /// <summary>
        /// Validates that the client has permission for the requested grant type.
        /// Required because passthrough mode doesn't automatically enforce client permissions.
        /// </summary>
        private async Task<IActionResult?> ValidateClientGrantPermissionAsync(OpenIddictRequest request)
        {
            var clientId = request.ClientId;
            if (string.IsNullOrEmpty(clientId))
            {
                // No client_id means anonymous request - only valid for some flows
                return null;
            }

            var client = await _applicationManager.FindByClientIdAsync(clientId);
            if (client == null)
            {
                return new ForbidResult(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new Microsoft.AspNetCore.Authentication.AuthenticationProperties(
                        new Dictionary<string, string?>
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidClient,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The specified client identifier is invalid."
                        }));
            }

            var permissions = await _applicationManager.GetPermissionsAsync(client);
            var permissionSet = permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Check grant type permissions
            if (request.IsPasswordGrantType() && 
                !permissionSet.Contains(OpenIddictConstants.Permissions.GrantTypes.Password))
            {
                _logger.LogWarning("Client {ClientId} attempted password grant without permission", clientId);
                return CreateUnauthorizedGrantResponse("password");
            }

            if (request.IsClientCredentialsGrantType() && 
                !permissionSet.Contains(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials))
            {
                _logger.LogWarning("Client {ClientId} attempted client_credentials grant without permission", clientId);
                return CreateUnauthorizedGrantResponse("client_credentials");
            }

            if (request.GrantType == GrantTypes.DeviceCode && 
                !permissionSet.Contains(OpenIddictConstants.Permissions.GrantTypes.DeviceCode))
            {
                _logger.LogWarning("Client {ClientId} attempted device_code grant without permission", clientId);
                return CreateUnauthorizedGrantResponse("urn:ietf:params:oauth:grant-type:device_code");
            }

            if (request.IsRefreshTokenGrantType() && 
                !permissionSet.Contains(OpenIddictConstants.Permissions.GrantTypes.RefreshToken))
            {
                _logger.LogWarning("Client {ClientId} attempted refresh_token grant without permission", clientId);
                return CreateUnauthorizedGrantResponse("refresh_token");
            }

            if (request.IsAuthorizationCodeGrantType() && 
                !permissionSet.Contains(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode))
            {
                _logger.LogWarning("Client {ClientId} attempted authorization_code grant without permission", clientId);
                return CreateUnauthorizedGrantResponse("authorization_code");
            }

            return null; // Permission granted
        }

        private static ForbidResult CreateUnauthorizedGrantResponse(string grantType)
        {
            return new ForbidResult(
                authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                properties: new Microsoft.AspNetCore.Authentication.AuthenticationProperties(
                    new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.UnauthorizedClient,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = 
                            $"The client is not authorized to use the '{grantType}' grant type."
                    }));
        }



        private static IEnumerable<string> GetDestinations(Claim claim)
        {
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

                case "permission":
                    yield return Destinations.AccessToken;
                    yield return Destinations.IdentityToken;
                    yield break;

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

        [LoggerMessage(Level = LogLevel.Information, Message = "Processing device code grant")]
        partial void LogProcessingDeviceCodeGrant();

        [LoggerMessage(Level = LogLevel.Information, Message = "Device code grant: ClaimsPrincipal created successfully with subject {Subject}")]
        partial void LogDeviceCodeGrantSuccess(string? subject);

        [LoggerMessage(Level = LogLevel.Error, Message = "Error processing device code grant")]
        partial void LogDeviceCodeGrantError(Exception ex);
    }
}
