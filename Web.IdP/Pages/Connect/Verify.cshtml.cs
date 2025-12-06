using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;
using Core.Domain;

namespace Web.IdP.Pages.Connect;

[Authorize, IgnoreAntiforgeryToken]
public class DeviceModel : PageModel
{
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IStringLocalizer<DeviceModel> _localizer;
    private readonly ILogger<DeviceModel> _logger;

    public DeviceModel(
        IOpenIddictScopeManager scopeManager,
        IOpenIddictApplicationManager applicationManager,
        UserManager<ApplicationUser> userManager,
        IStringLocalizer<DeviceModel> localizer,
        ILogger<DeviceModel> logger)
    {
        _scopeManager = scopeManager;
        _applicationManager = applicationManager;
        _userManager = userManager;
        _localizer = localizer;
        _logger = logger;
    }

    [BindProperty(Name = "user_code")]
    public string? UserCode { get; set; }

    public string? ApplicationName { get; set; }
    public string? Scope { get; set; }
    public string? Error { get; set; }
    public string? ErrorDescription { get; set; }
    
    public async Task<IActionResult> OnGetAsync()
    {
        // Retrieve the claims principal associated with the user code.
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        if (result is { Succeeded: true } && !string.IsNullOrEmpty(result.Principal.GetClaim(Claims.ClientId)))
        {
            // Retrieve the application details from database using client_id stored in principal.
            var application = await _applicationManager.FindByClientIdAsync(result.Principal.GetClaim(Claims.ClientId)!);
            if (application == null)
            {
                Error = Errors.InvalidClient;
                ErrorDescription = _localizer["InvalidClient"];
                return Page();
            }

            // Render a form asking the user to confirm the authorization demand.
            ApplicationName = await _applicationManager.GetDisplayNameAsync(application);
            Scope = string.Join(" ", result.Principal.GetScopes());
            UserCode = result.Properties?.GetTokenValue(OpenIddictServerAspNetCoreConstants.Tokens.UserCode);
            return Page();
        }

        // If a user code was specified (e.g as part of the verification_uri_complete)
        // but is not valid, render a form asking the user to enter the user code manually.
        var userCodeFromResult = result.Properties?.GetTokenValue(OpenIddictServerAspNetCoreConstants.Tokens.UserCode);
        if (!string.IsNullOrEmpty(userCodeFromResult))
        {
            Error = Errors.InvalidToken;
            ErrorDescription = _localizer["InvalidUserCode"];
            return Page();
        }

        // Otherwise, render a form asking the user to enter the user code manually.
        return Page();
    }

    [Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> OnPostAsync()
    {
        // Retrieve the profile of the logged in user.
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            _logger.LogError("Cannot retrieve user details during device verification.");
            Error = Errors.ServerError;
            ErrorDescription = _localizer["UserRetrievalFailed"];
            return Page();
        }

        // Retrieve the claims principal associated with the user code.
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (result is { Succeeded: true } && !string.IsNullOrEmpty(result.Principal.GetClaim(Claims.ClientId)))
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

            // Note: in this sample, the granted scopes match the requested scope
            // but you may want to allow the user to uncheck specific scopes.
            identity.SetScopes(result.Principal.GetScopes());
            identity.SetResources(await _scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());
            identity.SetDestinations(GetDestinations);

            var properties = new AuthenticationProperties
            {
                // This property points to the address OpenIddict will automatically
                // redirect the user to after validating the authorization demand.
                RedirectUri = "/"
            };

            _logger.LogInformation("Device flow authorization approved for user {UserId}", user.Id);
            return SignIn(new ClaimsPrincipal(identity), properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        // Redisplay the form when the user code is not valid.
        Error = Errors.InvalidToken;
        ErrorDescription = _localizer["InvalidUserCode"];
        return Page();
    }

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        // Note: by default, claims are NOT automatically included in the access and identity tokens.
        // To allow OpenIddict to serialize them, you must attach them a destination, that specifies
        // whether they should be included in access tokens, in identity tokens or in both.

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

            // Never include the security stamp in the access and identity tokens, as it's a secret value.
            case "AspNet.Identity.SecurityStamp": yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
