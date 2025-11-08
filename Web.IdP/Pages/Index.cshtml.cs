using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using System.Security.Claims;

namespace Web.IdP.Pages;

[Authorize]
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
        // [Authorize] ensures user is authenticated
        // Only show applications that user has consented to:
        // 1. Are public clients
        // 2. Have display name and redirect URIs
        // 3. User has given valid permanent authorization (consent)
        
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
    
    public async Task<IActionResult> OnPostDisconnectAsync(string applicationId)
    {
        if (string.IsNullOrEmpty(applicationId))
        {
            return BadRequest();
        }
        
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        
        // Find and delete all authorizations for this user and application
        var authorizations = _authorizationManager.FindAsync(
            subject: userId,
            client: applicationId,
            status: OpenIddictConstants.Statuses.Valid,
            type: OpenIddictConstants.AuthorizationTypes.Permanent,
            scopes: default);
        
        await foreach (var authorization in authorizations)
        {
            await _authorizationManager.DeleteAsync(authorization);
        }
        
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
