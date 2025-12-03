using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Core.Application;
using System.Security.Claims;

namespace Web.IdP.Pages.Account;

[Authorize]
public class LinkedAccountsModel : PageModel
{
    private readonly ILogger<LinkedAccountsModel> _logger;
    private readonly IAccountManagementService _accountManagementService;

    public LinkedAccountsModel(
        ILogger<LinkedAccountsModel> logger,
        IAccountManagementService accountManagementService)
    {
        _logger = logger;
        _accountManagementService = accountManagementService;
    }

    public List<Core.Application.DTOs.LinkedAccountDto> LinkedAccounts { get; set; } = new();
    public string CurrentUserEmail { get; set; } = "";

    public async Task OnGetAsync()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        CurrentUserEmail = User.FindFirstValue(ClaimTypes.Email) ?? "";

        var accounts = await _accountManagementService.GetMyLinkedAccountsAsync(userId);
        LinkedAccounts = accounts.ToList();
    }
}
