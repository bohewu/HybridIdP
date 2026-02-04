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
    public partial class TokenService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<ApplicationRole> roleManager,
        IApiResourceService apiResourceService,
        IAuditService auditService,
        IApplicationDbContext db,
        IOpenIddictApplicationManager applicationManager,
        ILogger<TokenService> logger,
        IClaimsEnrichmentService claimsEnricher) : ITokenService
    {
        private readonly UserManager<ApplicationUser> _userManager = userManager;
        private readonly SignInManager<ApplicationUser> _signInManager = signInManager;
        private readonly RoleManager<ApplicationRole> _roleManager = roleManager;
        private readonly IApiResourceService _apiResourceService = apiResourceService;
        private readonly IAuditService _auditService = auditService;
        private readonly IApplicationDbContext _db = db;
        private readonly IOpenIddictApplicationManager _applicationManager = applicationManager;
        private readonly ILogger<TokenService> _logger = logger;
        private readonly IClaimsEnrichmentService _claimsEnricher = claimsEnricher;

        public async Task<IActionResult> HandleTokenRequestAsync(OpenIddictRequest request, ClaimsPrincipal? schemePrincipal, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            // Validate client grant type permissions (required for passthrough mode)
            var permissionError = await ValidateClientGrantPermissionAsync(request);
            if (permissionError != null)
            {
                return permissionError;
            }

            if (request.IsPasswordGrantType())
            {
                return await HandlePasswordGrantAsync(request, cancellationToken);
            }

            if (request.IsAuthorizationCodeGrantType())
            {
                return await HandleAuthorizationCodeGrantAsync(request, schemePrincipal);
            }

            if (request.IsRefreshTokenGrantType())
            {
                return await HandleRefreshTokenGrantAsync(request, schemePrincipal, cancellationToken);
            }
            
            if (request.IsDeviceCodeGrantType())
            {
                return await HandleDeviceCodeGrantAsync(request, schemePrincipal, cancellationToken);
            }

            if (request.IsClientCredentialsGrantType())
            {
                return await HandleClientCredentialsGrantAsync(request);
            }

            return new ForbidResult(
                authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.UnsupportedGrantType,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The specified grant type is not supported."
                }));
        }

        private async Task<IActionResult?> ValidateClientGrantPermissionAsync(OpenIddictRequest request)
        {
             // Resolve the application to check permissions
            var application = await _applicationManager.FindByClientIdAsync(request.ClientId ?? string.Empty);
            if (application == null)
            {
                return new ForbidResult(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidClient,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The calling client application cannot be found."
                    }));
            }

            var permissions = await _applicationManager.GetPermissionsAsync(application);
            
            // Map grant types to permissions
            var requiredPermission = request.GrantType switch
            {
                GrantTypes.AuthorizationCode => OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                GrantTypes.ClientCredentials => OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                GrantTypes.DeviceCode => OpenIddictConstants.Permissions.GrantTypes.DeviceCode,
                GrantTypes.Password => OpenIddictConstants.Permissions.GrantTypes.Password,
                GrantTypes.RefreshToken => OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                _ => null
            };

            if (requiredPermission != null && !permissions.Contains(requiredPermission))
            {
                LogClientAttemptedGrantWithoutPermission(request.ClientId, request.GrantType ?? "unknown");
                
                return new ForbidResult(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.UnauthorizedClient,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = $"The client is not authorized to use the '{request.GrantType}' grant type."
                    }));
            }

            return null;
        }

        private async Task<IActionResult> HandlePasswordGrantAsync(OpenIddictRequest request, CancellationToken cancellationToken)
        {
            var user = await _userManager.FindByNameAsync(request.Username!);
            if (user == null)
            {
                 return new ForbidResult(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The username/password couple is invalid."
                    }));
            }

            // Ensure the user is allowed to sign in
            if (!await _signInManager.CanSignInAsync(user))
            {
                return new ForbidResult(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user is no longer allowed to sign in."
                    }));
            }

            if (!await _userManager.CheckPasswordAsync(user, request.Password!))
            {
                 // Audit failed login attempt
                var ip = "unknown"; 
                var ua = "unknown";
                await _auditService.LogEventAsync("UserLogin", user.Id.ToString(), System.Text.Json.JsonSerializer.Serialize(new { Success = false, FailureReason = "Invalid password" }), ip, ua);

                return new ForbidResult(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The username/password couple is invalid."
                    }));
            }

            // Valid credentials
            var identity = new ClaimsIdentity(
                authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                nameType: Claims.Name,
                roleType: Claims.Role);

            identity.SetClaim(Claims.Subject, await _userManager.GetUserIdAsync(user))
                .SetClaim(Claims.Email, await _userManager.GetEmailAsync(user))
                .SetClaim(Claims.Name, await _userManager.GetUserNameAsync(user))
                .SetClaim(Claims.PreferredUsername, await _userManager.GetUserNameAsync(user))
                .SetClaims(Claims.Role, [.. (await _userManager.GetRolesAsync(user))]);

            // Add Permissions
            await _claimsEnricher.AddPermissionClaimsAsync(identity, user, request.ClientId, cancellationToken);
            await _claimsEnricher.AddAppSpecificRolesAsync(identity, user, request.ClientId ?? string.Empty, cancellationToken);
            
            // Add Scope Mapped Claims
            await _claimsEnricher.AddScopeMappedClaimsAsync(identity, user, request.GetScopes(), cancellationToken);

            // Add AMR manually for Password grant
            identity.AddClaim(AuthConstants.ClaimTypes.Amr, AuthConstants.Amr.Password);

            identity.SetScopes(request.GetScopes());
            identity.SetDestinations(GetDestinations);

            var principal = new ClaimsPrincipal(identity);
            
            // Audit successful login
            var ip2 = "unknown";
            var ua2 = "unknown";
            await _auditService.LogEventAsync("UserLogin", user.Id.ToString(), System.Text.Json.JsonSerializer.Serialize(new { Success = true }), ip2, ua2);

            return new Microsoft.AspNetCore.Mvc.SignInResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, principal);
        }

        private async Task<IActionResult> HandleAuthorizationCodeGrantAsync(OpenIddictRequest request, ClaimsPrincipal? principal)
        {
            if (principal == null)
             {
                 return new ForbidResult(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The authorization code is invalid."
                    }));
             }

            // Ensure that the user happens to represent a valid user in our DB
            var userId = principal.GetClaim(Claims.Subject);
            var user = await _userManager.FindByIdAsync(userId!);
            if (user == null)
            {
                 return new ForbidResult(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user is no longer allowed to sign in."
                    }));
            }

            if (!await _signInManager.CanSignInAsync(user))
            {
                 return new ForbidResult(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user is no longer allowed to sign in."
                    }));
            }
            
            foreach (var claim in principal.Claims)
            {
                claim.SetDestinations(GetDestinations(claim));
            }

            return new Microsoft.AspNetCore.Mvc.SignInResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, principal);
        }

        private async Task<IActionResult> HandleRefreshTokenGrantAsync(OpenIddictRequest request, ClaimsPrincipal? principal, CancellationToken cancellationToken)
        {
            if (principal == null)
            {
                 return new ForbidResult(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The refresh token is invalid."
                    }));
            }
             
            var userId = principal.GetClaim(Claims.Subject);
            var user = await _userManager.FindByIdAsync(userId!);
            if (user == null || !await _signInManager.CanSignInAsync(user))
            {
                 return new ForbidResult(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user is no longer allowed to sign in."
                    }));
            }

            var identity = new ClaimsIdentity(
                authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                nameType: Claims.Name,
                roleType: Claims.Role);

            identity.SetClaim(Claims.Subject, await _userManager.GetUserIdAsync(user))
                .SetClaim(Claims.Email, await _userManager.GetEmailAsync(user))
                .SetClaim(Claims.Name, await _userManager.GetUserNameAsync(user))
                .SetClaim(Claims.PreferredUsername, await _userManager.GetUserNameAsync(user))
                .SetClaims(Claims.Role, [.. (await _userManager.GetRolesAsync(user))]);

             var amr = principal.GetClaim(AuthConstants.ClaimTypes.Amr);
             if (!string.IsNullOrEmpty(amr))
             {
                 identity.AddClaim(AuthConstants.ClaimTypes.Amr, amr);
             }

            await _claimsEnricher.AddPermissionClaimsAsync(identity, user, request.ClientId, cancellationToken);
            await _claimsEnricher.AddAppSpecificRolesAsync(identity, user, request.ClientId ?? string.Empty, cancellationToken);
            await _claimsEnricher.AddScopeMappedClaimsAsync(identity, user, request.GetScopes(), cancellationToken);

            identity.SetScopes(request.GetScopes());
            identity.SetDestinations(GetDestinations);

            return new Microsoft.AspNetCore.Mvc.SignInResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        }

        private async Task<IActionResult> HandleDeviceCodeGrantAsync(OpenIddictRequest request, ClaimsPrincipal? schemePrincipal, CancellationToken cancellationToken)
        {
            LogProcessingDeviceCodeGrant();
            try
            {
                if (schemePrincipal == null)
                {
                    return new ForbidResult(
                        authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
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
                        authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                        properties: new AuthenticationProperties(new Dictionary<string, string?>
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The device code is missing the subject claim."
                        }));
                }

                // Retrieve the user profile corresponding to the device code.
                var user = await _db.Users
                    .Include(u => u.Person)
                    .FirstOrDefaultAsync(u => u.Id == Guid.Parse(subject), cancellationToken);

                if (user == null || !await _signInManager.CanSignInAsync(user))
                {
                        return new ForbidResult(
                        authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                        properties: new AuthenticationProperties(new Dictionary<string, string?>
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The token is no longer valid or user cannot sign in."
                        }));
                }

                var identity = new ClaimsIdentity(
                    authenticationType: Microsoft.IdentityModel.Tokens.TokenValidationParameters.DefaultAuthenticationType,
                    nameType: Claims.Name,
                    roleType: Claims.Role);

                identity.SetClaim(Claims.Subject, await _userManager.GetUserIdAsync(user));

                var requestedScopes = schemePrincipal.GetScopes().ToList();
                await _claimsEnricher.AddScopeMappedClaimsAsync(identity, user, requestedScopes, cancellationToken);

                identity.SetScopes(requestedScopes);

                await _claimsEnricher.AddPermissionClaimsAsync(identity, user, request.ClientId, cancellationToken);
                await _claimsEnricher.AddAppSpecificRolesAsync(identity, user, request.ClientId ?? string.Empty, cancellationToken);

                var audiences = await _apiResourceService.GetAudiencesByScopesAsync(requestedScopes);
                if (audiences.Count > 0)
                {
                    identity.SetAudiences(audiences.ToImmutableArray());
                }

                identity.SetDestinations(GetDestinations);

                var claimsPrincipal = new ClaimsPrincipal(identity);
                LogDeviceCodeGrantSuccess(claimsPrincipal.GetClaim(Claims.Subject));
                return new Microsoft.AspNetCore.Mvc.SignInResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, claimsPrincipal);
            }
            catch (Exception ex)
            {
                LogDeviceCodeGrantError(ex);
                return new ForbidResult(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.ServerError,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "An error occurred processing the device code."
                    }));
            }
        }

        private async Task<IActionResult> HandleClientCredentialsGrantAsync(OpenIddictRequest request)
        {
            var application = await _applicationManager.FindByClientIdAsync(request.ClientId!);
            if (application == null)
            {
                 return new ForbidResult(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidClient,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The client application cannot be found."
                    }));
            }

            var identity = new ClaimsIdentity(
                authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                nameType: Claims.Name,
                roleType: Claims.Role);
            
            identity.SetClaim(Claims.Subject, await _applicationManager.GetClientIdAsync(application));
            identity.SetClaim(Claims.Name, await _applicationManager.GetDisplayNameAsync(application));
            
            identity.SetScopes(request.GetScopes());
            identity.SetDestinations(GetDestinations);

            return new Microsoft.AspNetCore.Mvc.SignInResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        }

        private static IEnumerable<string> GetDestinations(Claim claim)
        {
            switch (claim.Type)
            {
                case Claims.Name:
                case Claims.Email:
                case Claims.Subject:
                case Claims.Role:
                case "app_role":
                case "permission":
                case AuthConstants.Claims.PreferredUsername:
                case AuthConstants.Claims.Department:
                case AuthConstants.Claims.PersonId:
                case Claims.AuthenticationMethodReference:
                case Claims.AuthenticationContextReference:
                    yield return Destinations.AccessToken;
                    yield return Destinations.IdentityToken;
                    yield break;

                case "AspNet.Identity.SecurityStamp":
                    yield break;

                default:
                    // Include custom/dynamic claims in both tokens by default
                    yield return Destinations.AccessToken;
                    yield return Destinations.IdentityToken;
                    yield break;
            }
        }

        [LoggerMessage(Level = LogLevel.Information, Message = "Processing device code grant")]
        partial void LogProcessingDeviceCodeGrant();

        [LoggerMessage(Level = LogLevel.Information, Message = "Device code grant: ClaimsPrincipal created successfully with subject {Subject}")]
        partial void LogDeviceCodeGrantSuccess(string? subject);

        [LoggerMessage(Level = LogLevel.Error, Message = "Error processing device code grant")]
        partial void LogDeviceCodeGrantError(Exception ex);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Client {ClientId} attempted {GrantType} grant without permission")]
        partial void LogClientAttemptedGrantWithoutPermission(string? clientId, string grantType);
    }
}
