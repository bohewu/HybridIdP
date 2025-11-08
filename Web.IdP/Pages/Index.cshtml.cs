using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Web.IdP.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly IOpenIddictApplicationManager _applicationManager;

    public IndexModel(
        ILogger<IndexModel> logger,
        IOpenIddictApplicationManager applicationManager)
    {
        _logger = logger;
        _applicationManager = applicationManager;
    }

    public List<ApplicationInfo> Applications { get; set; } = new();

    public async Task OnGetAsync()
    {
        // Only show public clients with redirect URIs
        // Confidential clients (server-side apps) shouldn't appear in user app launcher
        
        await foreach (var app in _applicationManager.ListAsync())
        {
            var clientType = await _applicationManager.GetClientTypeAsync(app);
            var displayName = await _applicationManager.GetDisplayNameAsync(app);
            var redirectUris = await _applicationManager.GetRedirectUrisAsync(app);
            
            // Filter: Only public clients with display name and redirect URIs
            bool isPublicClient = clientType == OpenIddictConstants.ClientTypes.Public;
            bool hasDisplayName = !string.IsNullOrEmpty(displayName);
            bool hasRedirectUri = redirectUris.Any();
            
            if (isPublicClient && hasDisplayName && hasRedirectUri)
            {
                var clientId = await _applicationManager.GetClientIdAsync(app);
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
}

public class ApplicationInfo
{
    public string ClientId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string LoginUrl { get; set; } = "";
}
