using System.Security.Claims;
using Core.Domain;
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
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IStringLocalizer<DeviceFlowService> _localizer;
    private readonly ILogger<DeviceFlowService> _logger;
    private readonly IClaimsEnrichmentService _claimsEnricher;

    public DeviceFlowService(
        IOpenIddictScopeManager scopeManager,
        IOpenIddictApplicationManager applicationManager,
        UserManager<ApplicationUser> userManager,
        IStringLocalizer<DeviceFlowService> localizer,
        ILogger<DeviceFlowService> logger,
        IClaimsEnrichmentService claimsEnricher)
    {
        _scopeManager = scopeManager;
        _applicationManager = applicationManager;
        _userManager = userManager;
        _localizer = localizer;
        _logger = logger;
        _claimsEnricher = claimsEnricher;
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
            vm.ApplicationName = await _applicationManager.GetDisplayNameAsync(application);
            vm.Scope = string.Join(" ", authenticateResult.Principal.GetScopes());
            vm.UserCode = authenticateResult.Properties?.GetTokenValue(OpenIddictServerAspNetCoreConstants.Tokens.UserCode);
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

    public async Task<IActionResult> ProcessVerificationAsync(ClaimsPrincipal userPrincipal, AuthenticateResult authenticateResult)
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
            var identity = new ClaimsIdentity(
                authenticationType: TokenValidationParameters.DefaultAuthenticationType,
                nameType: Claims.Name,
                roleType: Claims.Role);

            // Add the claims that will be persisted in the tokens.
            identity.SetClaim(Claims.Subject, await _userManager.GetUserIdAsync(user));
            
            // Enrich with scope-mapped claims and permissions using shared service
            var scopes = authenticateResult.Principal.GetScopes();
            await _claimsEnricher.AddScopeMappedClaimsAsync(identity, user, scopes);
            await _claimsEnricher.AddPermissionClaimsAsync(identity, user);

            identity.SetScopes(scopes);
            identity.SetResources(await _scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());
            identity.SetDestinations(GetDestinations);

            var properties = new AuthenticationProperties
            {
                RedirectUri = "/connect/verify/success"
            };

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

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        switch (claim.Type)
        {
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
