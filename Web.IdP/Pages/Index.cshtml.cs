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
        // TODO: In the future, filter applications based on user permissions
        // For now, show all public clients that have a valid redirect URI
        
        await foreach (var app in _applicationManager.ListAsync())
        {
            var clientId = await _applicationManager.GetClientIdAsync(app);
            var displayName = await _applicationManager.GetDisplayNameAsync(app);
            var redirectUris = await _applicationManager.GetRedirectUrisAsync(app);
            
            if (!string.IsNullOrEmpty(displayName) && redirectUris.Any())
            {
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
