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

public class DeviceFlowService : IDeviceFlowService
{
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IStringLocalizer<DeviceFlowService> _localizer;
    private readonly ILogger<DeviceFlowService> _logger;

    public DeviceFlowService(
        IOpenIddictScopeManager scopeManager,
        IOpenIddictApplicationManager applicationManager,
        UserManager<ApplicationUser> userManager,
        IStringLocalizer<DeviceFlowService> localizer,
        ILogger<DeviceFlowService> logger)
    {
        _scopeManager = scopeManager;
        _applicationManager = applicationManager;
        _userManager = userManager;
        _localizer = localizer;
        _logger = logger;
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
            _logger.LogError("Cannot retrieve user details during device verification.");
             var vm = new DeviceVerificationViewModel 
             {
                 Error = Errors.ServerError,
                 ErrorDescription = _localizer["UserRetrievalFailed"]
             };
             // Logic in Controller should return View(vm) if result is not SignInResult
             // But here we return IActionResult. 
             // We can return BadRequestObjectResult or similar, or ViewResult? 
             // Services typically shouldn't return ViewResult.
             // Let's refactor interface to return a composite result or logic?
             // Or, stick to IActionResult (SignInResult vs ViewResult). 
             // For now, let's return a special ObjectResult that Controller can interpret, or throw exception?
             // Replicating existing logic: it returns Page() with error. 
             // So I should return the ViewModel with Error?
             // Issue: I cannot return ViewModel as IActionResult seamlessly without Controller support.
             // Strategy: Return Challenge? Or make interface return (IActionResult? result, DeviceVerificationViewModel? vm).
             
             // Simplest: Return a Forbidden/BadRequest, and Controller handles UI?
             // But UI needs to show Error description.
             // Let's change return type to `Task<DeviceVerificationResult>`
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
            identity.SetClaim(Claims.Subject, await _userManager.GetUserIdAsync(user))
                    .SetClaim(Claims.Email, await _userManager.GetEmailAsync(user))
                    .SetClaim(Claims.Name, await _userManager.GetUserNameAsync(user))
                    .SetClaim(Claims.PreferredUsername, await _userManager.GetUserNameAsync(user))
                    .SetClaims(Claims.Role, [.. (await _userManager.GetRolesAsync(user))]);

            identity.SetScopes(authenticateResult.Principal.GetScopes());
            identity.SetResources(await _scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());
            identity.SetDestinations(GetDestinations);

            var properties = new AuthenticationProperties
            {
                RedirectUri = "/connect/verify/success"
            };

            _logger.LogInformation("Device flow authorization approved for user {UserId}", user.Id);
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
}
