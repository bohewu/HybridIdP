using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using System.Security.Claims;

namespace Web.IdP.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictAuthorizationManager _authorizationManager;

    public IndexModel(
        ILogger<IndexModel> logger,
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
        // Only show applications that:
        // 1. Are public clients
        // 2. Have display name and redirect URIs
        // 3. User is not authenticated OR user has given consent
        
        var isAuthenticated = User.Identity?.IsAuthenticated == true;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        
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
            
            // If user is authenticated, only show apps they've consented to
            if (isAuthenticated && !string.IsNullOrEmpty(userId))
            {
                // Check if user has any valid authorization (consent) for this app
                var authorizations = _authorizationManager.FindAsync(
                    subject: userId,
                    client: clientId,
                    status: OpenIddictConstants.Statuses.Valid,
                    type: OpenIddictConstants.AuthorizationTypes.Permanent,
                    scopes: default);
                
                bool hasConsent = false;
                await foreach (var auth in authorizations)
                {
                    hasConsent = true;
                    break;
                }
                
                if (!hasConsent)
                {
                    continue; // Skip apps without user consent
                }
            }
            
            // Use first redirect URI as login URL
            var loginUrl = redirectUris.FirstOrDefault()?.ToString() ?? "#";
            
            Applications.Add(new ApplicationInfo
            {
                ClientId = clientId ?? "",
                DisplayName = displayName ?? clientId ?? "Unknown",
                LoginUrl = loginUrl
            });
        }
    }
}

public class ApplicationInfo
{
    public string ClientId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string LoginUrl { get; set; } = "";
}
