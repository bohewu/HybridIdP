using System.Collections.Immutable;
using System.Security.Claims;
using Core.Application;
using Core.Application.Utilities;
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
        ISecurityPolicyService securityPolicyService,
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
        private readonly ISecurityPolicyService _securityPolicyService = securityPolicyService;
        private readonly IApplicationDbContext _db = db;
        private readonly IOpenIddictApplicationManager _applicationManager = applicationManager;
        private readonly ILogger<TokenService> _logger = logger;
        private readonly IClaimsEnrichmentService _claimsEnricher = claimsEnricher;

        public async Task<IActionResult> HandleTokenRequestAsync(OpenIddictRequest request, ClaimsPrincipal? schemePrincipal, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            // Validate client grant type permissions (required for passthrough mode)
            var permissionError = await ValidateClientGrantPermissionAsync(request, cancellationToken);
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
                return await HandleAuthorizationCodeGrantAsync(request, schemePrincipal, cancellationToken);
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
                return await HandleClientCredentialsGrantAsync(request, cancellationToken);
            }

            return new ForbidResult(
                authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.UnsupportedGrantType,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The specified grant type is not supported."
                }));
        }

        private async Task<IActionResult?> ValidateClientGrantPermissionAsync(OpenIddictRequest request, CancellationToken cancellationToken)
        {
             // Resolve the application to check permissions
            var application = await _applicationManager.FindByClientIdAsync(request.ClientId ?? string.Empty, cancellationToken);
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

            var permissions = await _applicationManager.GetPermissionsAsync(application, cancellationToken);
            
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

            if (!await CanIssueTokenForCurrentUserStateAsync(user, cancellationToken))
            {
                return new ForbidResult(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "The username/password couple is invalid."
                    }));
            }

            if (!await _userManager.CheckPasswordAsync(user, request.Password!))
            {
                await RecordPasswordGrantFailureAsync(user);

                 // Audit failed login attempt
                var ip = "unknown"; 
                var ua = "unknown";
                await _auditService.LogEventAsync("UserLogin", user.Id.ToString(), System.Text.Json.JsonSerializer.Serialize(new { Success = false, FailureReason = "Invalid password" }), ip, ua, cancellationToken);

                return new ForbidResult(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The username/password couple is invalid."
                    }));
            }

            await _userManager.ResetAccessFailedCountAsync(user);

            if (!await CanCompletePasswordGrantAsync(user, cancellationToken))
            {
                await _auditService.LogEventAsync(
                    "UserLogin",
                    user.Id.ToString(),
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        Success = false,
                        FailureReason = "Additional authentication required"
                    }),
                    "unknown",
                    "unknown",
                    cancellationToken);

                return new ForbidResult(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "The username/password couple is invalid."
                    }));
            }

            // Valid credentials
            var identity = new ClaimsIdentity(
                authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                nameType: Claims.Name,
                roleType: Claims.Role);

            var displayName = NameFormatter.BuildDisplayName(user.FirstName, user.MiddleName, user.LastName)
                ?? await _userManager.GetUserNameAsync(user);

            identity.SetClaim(Claims.Subject, await _userManager.GetUserIdAsync(user))
                .SetClaim(Claims.Email, await _userManager.GetEmailAsync(user))
                .SetClaim(Claims.Name, displayName)
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
            await _auditService.LogEventAsync("UserLogin", user.Id.ToString(), System.Text.Json.JsonSerializer.Serialize(new { Success = true }), ip2, ua2, cancellationToken);

            return new Microsoft.AspNetCore.Mvc.SignInResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, principal);
        }

        private async Task<IActionResult> HandleAuthorizationCodeGrantAsync(OpenIddictRequest request, ClaimsPrincipal? principal, CancellationToken cancellationToken)
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

            if (!await CanIssueTokenForCurrentUserStateAsync(user, cancellationToken))
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
            var user = string.IsNullOrEmpty(userId)
                ? null
                : await _userManager.FindByIdAsync(userId);
            if (user == null || !await CanIssueTokenForCurrentUserStateAsync(user, cancellationToken))
            {
                 return new ForbidResult(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user is no longer allowed to sign in."
                    }));
            }

            var requestScopes = request.GetScopes().ToImmutableArray();
            var effectiveScopes = requestScopes.IsDefaultOrEmpty
                ? principal.GetScopes().ToImmutableArray()
                : requestScopes;

            var identity = new ClaimsIdentity(
                authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                nameType: Claims.Name,
                roleType: Claims.Role);

            var displayName = NameFormatter.BuildDisplayName(user.FirstName, user.MiddleName, user.LastName)
                ?? await _userManager.GetUserNameAsync(user);

            identity.SetClaim(Claims.Subject, await _userManager.GetUserIdAsync(user))
                .SetClaim(Claims.Email, await _userManager.GetEmailAsync(user))
                .SetClaim(Claims.Name, displayName)
                .SetClaim(Claims.PreferredUsername, await _userManager.GetUserNameAsync(user))
                .SetClaims(Claims.Role, [.. (await _userManager.GetRolesAsync(user))]);

            var amrValues = principal.GetClaims(AuthConstants.ClaimTypes.Amr)
                .Distinct(StringComparer.Ordinal);

            foreach (var amrValue in amrValues)
            {
                identity.AddClaim(AuthConstants.ClaimTypes.Amr, amrValue);
            }

            await _claimsEnricher.AddPermissionClaimsAsync(identity, user, request.ClientId, cancellationToken);
            await _claimsEnricher.AddAppSpecificRolesAsync(identity, user, request.ClientId ?? string.Empty, cancellationToken);
            await _claimsEnricher.AddScopeMappedClaimsAsync(identity, user, effectiveScopes, cancellationToken);

            identity.SetScopes(effectiveScopes);
            identity.SetDestinations(GetDestinations);

            return new Microsoft.AspNetCore.Mvc.SignInResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        }

        private async Task<bool> CanIssueTokenForCurrentUserStateAsync(
            ApplicationUser user,
            CancellationToken cancellationToken)
        {
            if (!user.IsActive || user.IsDeleted)
            {
                return false;
            }

            if (!await _signInManager.CanSignInAsync(user) ||
                await _userManager.IsLockedOutAsync(user))
            {
                return false;
            }

            if (user.PersonId is not Guid personId)
            {
                return true;
            }

            var person = await _db.Persons
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == personId, cancellationToken);

            return person?.CanAuthenticate() == true;
        }

        private async Task<bool> CanCompletePasswordGrantAsync(
            ApplicationUser user,
            CancellationToken cancellationToken)
        {
            // The password grant cannot perform an MFA challenge. Issuing a token here
            // would bypass the second factor required by the interactive login flow.
            if (user.TwoFactorEnabled || user.EmailMfaEnabled)
            {
                return false;
            }

            var policy = await _securityPolicyService.GetCurrentPolicyAsync();
            if (!policy.EnforceMandatoryMfaEnrollment)
            {
                return true;
            }

            // Match the interactive login contract: an enrolled passkey satisfies the
            // mandatory-enrollment policy even when TOTP and Email MFA are not enabled.
            var hasPasskey = await _db.UserCredentials
                .AsNoTracking()
                .AnyAsync(credential => credential.UserId == user.Id, cancellationToken);
            if (hasPasskey)
            {
                return true;
            }

            var now = DateTime.UtcNow;
            if (user.MfaRequirementNotifiedAt == null)
            {
                user.MfaRequirementNotifiedAt = now;
                var updateResult = await _userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    return false;
                }
            }

            var gracePeriodEndsAt = user.MfaRequirementNotifiedAt.Value
                .AddDays(policy.MfaEnforcementGracePeriodDays);
            return now < gracePeriodEndsAt;
        }

        private async Task RecordPasswordGrantFailureAsync(ApplicationUser user)
        {
            var policy = await _securityPolicyService.GetCurrentPolicyAsync();
            if (policy.MaxFailedAccessAttempts <= 0)
            {
                return;
            }

            var failureResult = await _userManager.AccessFailedAsync(user);
            if (!failureResult.Succeeded)
            {
                return;
            }

            var failedAttempts = await _userManager.GetAccessFailedCountAsync(user);
            if (failedAttempts >= policy.MaxFailedAccessAttempts)
            {
                await _userManager.SetLockoutEndDateAsync(
                    user,
                    DateTimeOffset.UtcNow.AddMinutes(policy.LockoutDurationMinutes));
            }
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

                if (user == null ||
                    !await CanIssueTokenForCurrentUserStateAsync(user, cancellationToken))
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

        private async Task<IActionResult> HandleClientCredentialsGrantAsync(OpenIddictRequest request, CancellationToken cancellationToken)
        {
            var application = await _applicationManager.FindByClientIdAsync(request.ClientId!, cancellationToken);
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
            
            identity.SetClaim(Claims.Subject, await _applicationManager.GetClientIdAsync(application, cancellationToken));
            identity.SetClaim(Claims.Name, await _applicationManager.GetDisplayNameAsync(application, cancellationToken));
            
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
                case Claims.AuthenticationMethodReference:
                case Claims.AuthenticationContextReference:
                    yield return Destinations.AccessToken;
                    yield return Destinations.IdentityToken;
                    yield break;

                case AuthConstants.Claims.PersonId:
                    if (claim.Subject is ClaimsIdentity identity && identity.HasScope(Scopes.OpenId))
                    {
                        yield return Destinations.AccessToken;
                        yield return Destinations.IdentityToken;
                    }
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
