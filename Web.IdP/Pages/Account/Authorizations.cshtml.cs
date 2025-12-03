using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenIddict.Abstractions;
using System.Security.Claims;

namespace Web.IdP.Pages.Account;

[Authorize]
[IgnoreAntiforgeryToken]
public class AuthorizationsModel : PageModel
{
    private readonly ILogger<AuthorizationsModel> _logger;
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictAuthorizationManager _authorizationManager;

    public AuthorizationsModel(
        ILogger<AuthorizationsModel> logger,
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictAuthorizationManager authorizationManager)
    {
        _logger = logger;
        _applicationManager = applicationManager;
        _authorizationManager = authorizationManager;
    }

    public List<ApplicationInfo> Applications { get; set; } = new();

    public async Task OnGetAsync()
    {
        // Get all applications user has authorized
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        
        // First, collect all candidate applications (avoid concurrent DB operations)
        var candidateApps = new List<(object App, string? ApplicationId, string? ClientId, string DisplayName, string LoginUrl)>();
        
        await foreach (var app in _applicationManager.ListAsync())
        {
            var clientType = await _applicationManager.GetClientTypeAsync(app);
            var displayName = await _applicationManager.GetDisplayNameAsync(app);
            var redirectUris = await _applicationManager.GetRedirectUrisAsync(app);
            
            // Filter: Only public clients with display name and redirect URIs
            bool isPublicClient = clientType == OpenIddictConstants.ClientTypes.Public;
            bool hasDisplayName = !string.IsNullOrEmpty(displayName);
            bool hasRedirectUri = redirectUris.Any();
            
            if (!isPublicClient || !hasDisplayName || !hasRedirectUri)
            {
                continue;
            }
            
            var clientId = await _applicationManager.GetClientIdAsync(app);
            var applicationId = await _applicationManager.GetIdAsync(app);
            var loginUrl = redirectUris.FirstOrDefault()?.ToString() ?? "#";
            
            candidateApps.Add((app, applicationId, clientId, displayName ?? clientId ?? "Unknown", loginUrl));
        }
        
        // Filter by user consent
        foreach (var (app, applicationId, clientId, displayName, loginUrl) in candidateApps)
        {
            if (applicationId == null)
            {
                continue;
            }
            
            // Check if user has any valid authorization (consent) for this app
            var authorizations = _authorizationManager.FindAsync(
                subject: userId,
                client: applicationId,
                status: OpenIddictConstants.Statuses.Valid,
                type: OpenIddictConstants.AuthorizationTypes.Permanent,
                scopes: default);
            
            bool hasConsent = false;
            await foreach (var auth in authorizations)
            {
                hasConsent = true;
                break;
            }
            
            if (hasConsent)
            {
                Applications.Add(new ApplicationInfo
                {
                    ClientId = clientId ?? "",
                    DisplayName = displayName,
                    LoginUrl = loginUrl,
                    ApplicationId = applicationId
                });
            }
        }
    }
    
    public async Task<IActionResult> OnPostRevokeAsync(string applicationId)
    {
        if (string.IsNullOrEmpty(applicationId))
        {
            return BadRequest();
        }
        
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        
        // Find all authorizations for this user and application
        var authorizationsQuery = _authorizationManager.FindAsync(
            subject: userId,
            client: applicationId,
            status: OpenIddictConstants.Statuses.Valid,
            type: OpenIddictConstants.AuthorizationTypes.Permanent,
            scopes: default);
        
        // Materialize the list first to avoid "command already in progress" error
        var authorizationsList = new List<object>();
        await foreach (var authorization in authorizationsQuery)
        {
            authorizationsList.Add(authorization);
        }
        
        // Now delete them
        foreach (var authorization in authorizationsList)
        {
            await _authorizationManager.DeleteAsync(authorization);
        }
        
        _logger.LogInformation("User {UserId} revoked authorization for application {ApplicationId}", userId, applicationId);
        
        // Return success - page will reload via JavaScript
        return new JsonResult(new { success = true });
    }
}

public class ApplicationInfo
{
    public string ClientId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string LoginUrl { get; set; } = "";
    public string? ApplicationId { get; set; }
}
