using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using static Core.Domain.Constants.AuthConstants;

namespace Web.IdP.Pages.ApplicationManager;

[Authorize(Policy = Permissions.Clients.Read)]
public class IndexModel : PageModel
{
    private readonly IClientService _clientService;
    private readonly IScopeService _scopeService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IClientService clientService,
        IScopeService scopeService,
        UserManager<ApplicationUser> userManager,
        ILogger<IndexModel> logger)
    {
        _clientService = clientService;
        _scopeService = scopeService;
        _userManager = userManager;
        _logger = logger;
    }

    public string UserName { get; set; } = string.Empty;
    public int ClientCount { get; set; }
    public int ScopeCount { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                _logger.LogWarning("User not found for ApplicationManager dashboard");
                UserName = User.Identity?.Name ?? "User";
                return;
            }

            UserName = user.UserName ?? User.Identity?.Name ?? "User";

            // Get PersonId from claims
            var personIdClaim = User.FindFirst("person_id");
            if (personIdClaim != null && Guid.TryParse(personIdClaim.Value, out var personId))
            {
                // Get user's role to determine if they're an admin
                var roles = await _userManager.GetRolesAsync(user);
                var isAdmin = roles.Contains(Roles.Admin);

                // Count clients - admin sees all, others see only their own
                Guid? ownerFilter = isAdmin ? null : personId;
                var clientsResult = await _clientService.GetClientsAsync(0, int.MaxValue, null, null, null, ownerFilter);
                ClientCount = clientsResult.totalCount;

                // Count scopes - admin sees all, others see only their own
                var scopesResult = await _scopeService.GetScopesAsync(0, int.MaxValue, null, null, ownerFilter);
                ScopeCount = scopesResult.totalCount;
            }
            else
            {
                _logger.LogWarning("PersonId claim not found for user {UserName}", user.UserName);
                ClientCount = 0;
                ScopeCount = 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading ApplicationManager dashboard data");
            ClientCount = 0;
            ScopeCount = 0;
        }
    }
}
