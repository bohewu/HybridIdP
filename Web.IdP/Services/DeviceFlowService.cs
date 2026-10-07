using System.Security.Claims;
using System.Collections.Immutable;
using Core.Application;
using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Web.IdP.Services;

public partial class DeviceFlowService : IDeviceFlowService
{
    private readonly Web.IdP.Services.ICurrentUserLifecycleEligibility _lifecycleEligibility;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictAuthorizationManager _authorizationManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IStringLocalizer<DeviceFlowService> _localizer;
    private readonly ILogger<DeviceFlowService> _logger;
    private readonly IClaimsEnrichmentService _claimsEnricher;
    private readonly ISecurityPolicyService _securityPolicyService;

    public DeviceFlowService(
        Web.IdP.Services.ICurrentUserLifecycleEligibility lifecycleEligibility,
        IOpenIddictScopeManager scopeManager,
        IOpenIddictApplicationManager applicationManager,
        UserManager<ApplicationUser> userManager,
        IStringLocalizer<DeviceFlowService> localizer,
        ILogger<DeviceFlowService> logger,
        IClaimsEnrichmentService claimsEnricher,
        ISecurityPolicyService securityPolicyService,
        IOpenIddictAuthorizationManager authorizationManager)
    {
        _lifecycleEligibility = lifecycleEligibility;
        _scopeManager = scopeManager;
        _applicationManager = applicationManager;
        _userManager = userManager;
        _localizer = localizer;
        _logger = logger;
        _claimsEnricher = claimsEnricher;
        _securityPolicyService = securityPolicyService;
        _authorizationManager = authorizationManager;
    }

    public async Task<DeviceVerificationViewModel> PrepareVerificationViewModelAsync(AuthenticateResult authenticateResult)
    {
        var vm = new DeviceVerificationViewModel();

        if (authenticateResult is { Succeeded: true } && !string.IsNullOrEmpty(authenticateResult.Principal.GetClaim(Claims.ClientId)))
        {
            // Retrieve the application details from database using client_id stored in principal.
            var application = await _applicationManager.FindByClientIdAsync(authenticateResult.Principal.GetClaim(Claims.ClientId)!);
            if (application == null)
            {
                vm.Error = Errors.InvalidClient;
                vm.ErrorDescription = _localizer["InvalidClient"];
                return vm;
            }

            // Render a form asking the user to confirm the authorization demand.
            vm.ApplicationName = await _applicationManager.GetDisplayNameAsync(application)
                ?? authenticateResult.Principal.GetClaim(Claims.ClientId);
            vm.Scope = string.Join(" ", authenticateResult.Principal.GetScopes());
            vm.UserCode = authenticateResult.Properties?.GetTokenValue(OpenIddictServerAspNetCoreConstants.Tokens.UserCode);
            vm.IsResolved = !string.IsNullOrWhiteSpace(vm.UserCode);
            return vm;
        }

        // If a user code was specified but is not valid, render a form asking the user to enter manually.
        var userCodeFromResult = authenticateResult.Properties?.GetTokenValue(OpenIddictServerAspNetCoreConstants.Tokens.UserCode);
        if (!string.IsNullOrEmpty(userCodeFromResult))
        {
            vm.Error = Errors.InvalidToken;
            vm.ErrorDescription = _localizer["InvalidUserCode"];
            return vm;
        }

        // Otherwise, render a form asking the user to enter the user code manually.
        return vm;
    }

    public async Task<IActionResult> ProcessVerificationAsync(ClaimsPrincipal userPrincipal, AuthenticateResult authenticateResult, CancellationToken cancellationToken = default, bool consentGranted = false)
    {
        var user = await _userManager.GetUserAsync(userPrincipal);
        if (user == null)
        {
            LogUserRetrievalFailed();
            return new BadRequestObjectResult(new DeviceVerificationViewModel
            {
                 Error = Errors.ServerError,
                 ErrorDescription = _localizer["UserRetrievalFailed"]
            });
        }

        if (authenticateResult is { Succeeded: true } && !string.IsNullOrEmpty(authenticateResult.Principal.GetClaim(Claims.ClientId)))
        {
            // Create the claims-based identity that will be used by OpenIddict to generate tokens.
            var client = await _applicationManager.FindByClientIdAsync(
                authenticateResult.Principal.GetClaim(Claims.ClientId)!, cancellationToken);
            var policy = await _securityPolicyService.GetCurrentPolicyAsync();
            if (client == null || ((policy.EnforceMandatoryMfaEnrollment ||
                ClientMfaPolicy.RequiresMfa(await _applicationManager.GetPropertiesAsync(client, cancellationToken))) &&
                !Web.IdP.Helpers.MfaEnrollmentSession.HasMfa(userPrincipal)))
            {
                return new ForbidResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Multi-factor authentication is required for this device authorization."
                    }));
            }
            var scopes = authenticateResult.Principal.GetScopes();
            var consentType = await _applicationManager.GetConsentTypeAsync(client, cancellationToken)
                ?? ConsentTypes.Explicit;
            string? authorizationId = null;
            if (consentType == ConsentTypes.External)
            {
                var applicationId = await _applicationManager.GetIdAsync(client, cancellationToken);
                if (!string.IsNullOrEmpty(applicationId))
                {
                    await foreach (var authorization in _authorizationManager.FindAsync(
                        subject: user.Id.ToString(), client: applicationId, status: Statuses.Valid,
                        type: AuthorizationTypes.Permanent, scopes: scopes, cancellationToken: cancellationToken))
                    {
                        var approvedScopes = await _authorizationManager.GetScopesAsync(authorization, cancellationToken);
                        if (scopes.All(scope => approvedScopes.Contains(scope, StringComparer.Ordinal)))
                        {
                            authorizationId = await _authorizationManager.GetIdAsync(authorization, cancellationToken);
                            if (!string.IsNullOrEmpty(authorizationId)) break;
                        }
                    }
                }
                if (string.IsNullOrEmpty(authorizationId)) return ConsentDenied();
            }
            else if (consentType != ConsentTypes.Implicit &&
                (consentType is not (ConsentTypes.Explicit or ConsentTypes.Systematic) || !consentGranted))
            {
                return ConsentDenied();
            }
            var identity = new ClaimsIdentity(
                authenticationType: TokenValidationParameters.DefaultAuthenticationType,
                nameType: Claims.Name,
                roleType: Claims.Role);

            // Add the claims that will be persisted in the tokens.
            identity.SetClaim(Claims.Subject, await _userManager.GetUserIdAsync(user));
            identity.SetClaim(Claims.AuthenticationTime, Web.IdP.Helpers.AuthorizationAuthenticationSession.GetAuthenticationTime(userPrincipal));
            identity.SetClaims(AuthConstants.ClaimTypes.Amr, userPrincipal.Claims
                .Where(c => c.Type == AuthConstants.ClaimTypes.Amr || c.Type == ClaimTypes.AuthenticationMethod)
                .Select(c => c.Value).Distinct(StringComparer.Ordinal).ToImmutableArray());
            
            // Enrich with scope-mapped claims and permissions using shared service
            await _claimsEnricher.AddScopeMappedClaimsAsync(identity, user, scopes);
            await _claimsEnricher.AddPermissionClaimsAsync(identity, user, authenticateResult.Principal.GetClaim(Claims.ClientId));

            identity.SetScopes(scopes);
            if (authorizationId != null) identity.SetAuthorizationId(authorizationId);
            identity.SetResources(await _scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());
            Web.IdP.Helpers.UserTokenClaimScopes.Apply(identity);
            identity.SetDestinations(GetDestinations);

            var properties = new AuthenticationProperties
            {
                RedirectUri = "/connect/verify/success"
            };

            if (!await _lifecycleEligibility.IsEligibleAsync(user.Id, cancellationToken))
            {
                return new ForbidResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user is no longer allowed to sign in."
                    }));
            }
            LogDeviceFlowApproved(user.Id);
            return new Microsoft.AspNetCore.Mvc.SignInResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), properties);
        }

        // Redisplay the form when the user code is not valid.
        return new BadRequestObjectResult(new DeviceVerificationViewModel
        {
            Error = Errors.InvalidToken,
            ErrorDescription = _localizer["InvalidUserCode"]
        });
    }

    private static ForbidResult ConsentDenied() => new(
        OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
        new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.AccessDenied,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Consent is required for this device authorization."
        }));

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        if (Web.IdP.Helpers.UserTokenClaimScopes.RequiredScope(claim.Type) is string required &&
            claim.Subject?.HasScope(required) != true) yield break;
        switch (claim.Type)
        {
            case Claims.AuthenticationTime:
                yield return Destinations.IdentityToken;
                yield break;
            case Claims.Name or Claims.PreferredUsername:
                yield return Destinations.AccessToken;
                if (claim.Subject!.HasScope(Scopes.Profile))
                    yield return Destinations.IdentityToken;
                yield break;

            case Claims.Email:
                yield return Destinations.AccessToken;
                if (claim.Subject!.HasScope(Scopes.Email))
                    yield return Destinations.IdentityToken;
                yield break;

            case Claims.Role:
                yield return Destinations.AccessToken;
                if (claim.Subject!.HasScope(Scopes.Roles))
                    yield return Destinations.IdentityToken;
                yield break;
            
            // Fix: Add Subject claim to IdentityToken
            case Claims.Subject:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;

            case AuthConstants.Claims.PersonId:
                if (claim.Subject!.HasScope(Scopes.OpenId))
                {
                    yield return Destinations.AccessToken;
                    yield return Destinations.IdentityToken;
                }
                yield break;

            case "AspNet.Identity.SecurityStamp": yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Cannot retrieve user details during device verification.")]
    partial void LogUserRetrievalFailed();

    [LoggerMessage(Level = LogLevel.Information, Message = "Device flow authorization approved for user {UserId}")]
    partial void LogDeviceFlowApproved(Guid userId);
}
