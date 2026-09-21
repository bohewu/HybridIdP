using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Core.Application;
using Core.Application.Ports;
using Core.Domain;
using Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Web.IdP.Helpers;

namespace Web.IdP.Pages.Account;

[EnableRateLimiting("login")]
public sealed class RequiredPasswordChangeModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICredentialMigrationStateStore _migrationStateStore;
    private readonly IAuditService _auditService;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly ApplicationDbContext _dbContext;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly TimeProvider _timeProvider;
    private readonly IDirectoryRequiredCredentialChangeService? _directoryChangeService;

    public RequiredPasswordChangeModel(
        UserManager<ApplicationUser> userManager,
        ICredentialMigrationStateStore migrationStateStore,
        IAuditService auditService,
        ISecurityPolicyService securityPolicyService,
        ApplicationDbContext dbContext,
        IStringLocalizer<SharedResource> localizer,
        TimeProvider? timeProvider = null,
        IDirectoryRequiredCredentialChangeService? directoryChangeService = null)
    {
        _userManager = userManager;
        _migrationStateStore = migrationStateStore;
        _auditService = auditService;
        _securityPolicyService = securityPolicyService;
        _dbContext = dbContext;
        _localizer = localizer;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _directoryChangeService = directoryChangeService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required]
        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword))]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public IActionResult OnGet()
    {
        return RequiredPasswordChangeSession.TryRead(
            HttpContext.Session,
            _timeProvider.GetUtcNow(),
            out _)
            ? Page()
            : RedirectToPage("./Login");
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return OnGet();
        }

        if (!RequiredPasswordChangeSession.TryConsume(
                HttpContext.Session,
                _timeProvider.GetUtcNow(),
                out var pending))
        {
            return RedirectToPage("./Login");
        }

        var user = await _userManager.FindByIdAsync(pending.UserId.ToString());
        if (user is null || !user.IsActive || user.IsDeleted ||
            !string.Equals(user.SecurityStamp, pending.SecurityStamp, StringComparison.Ordinal) ||
            await _userManager.IsLockedOutAsync(user))
        {
            return RedirectToPage("./Login");
        }

        var migration = await _migrationStateStore.FindAsync(user.Id, cancellationToken);
        if (pending.Authority == RequiredPasswordChangeAuthority.Directory)
        {
            if (_directoryChangeService is null || !user.RequiresPasswordChange ||
                pending.DirectoryObjectId is not Guid directoryObjectId ||
                migration?.State != Core.Domain.Entities.CredentialMigrationState.LocalFinalized ||
                migration.Binding.DirectoryObjectId != directoryObjectId)
            {
                return RedirectToPage("./Login");
            }

            var directoryResult = await _directoryChangeService.ChangeAsync(
                new DirectoryRequiredCredentialChangeRequest(
                    user.Id, directoryObjectId, Input.CurrentPassword, Input.NewPassword),
                cancellationToken);
            if (directoryResult == RecoveryProofOutcome.Success)
            {
                RequiredPasswordChangeSession.Clear(HttpContext.Session);
                return RedirectToPage("./Login", new { returnUrl = pending.ReturnUrl });
            }
            if (directoryResult == RecoveryProofOutcome.Invalid)
            {
                ModelState.AddModelError(string.Empty, _localizer["RequiredPasswordChange.Invalid"]);
                return Page();
            }
            return RedirectToPage("./Login");
        }

        if (!await _userManager.HasPasswordAsync(user) || migration is not null)
        {
            return RedirectToPage("./Login");
        }

        if (!await _userManager.CheckPasswordAsync(user, Input.CurrentPassword))
        {
            var policy = await _securityPolicyService.GetCurrentPolicyAsync();
            if (policy.MaxFailedAccessAttempts > 0)
            {
                var failure = await _userManager.AccessFailedAsync(user);
                if (failure.Succeeded &&
                    await _userManager.GetAccessFailedCountAsync(user) >= policy.MaxFailedAccessAttempts)
                {
                    await _userManager.SetLockoutEndDateAsync(
                        user,
                        _timeProvider.GetUtcNow().AddMinutes(policy.LockoutDurationMinutes));
                }
            }

            ModelState.AddModelError(string.Empty, _localizer["RequiredPasswordChange.Invalid"]);
            return Page();
        }

        RequiredPasswordChangeSession.Begin(
            HttpContext.Session,
            user,
            pending.ReturnUrl,
            _timeProvider.GetUtcNow());

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var previousHash = user.PasswordHash;
            var result = await _userManager.ChangePasswordAsync(
                user,
                Input.CurrentPassword,
                Input.NewPassword);
            if (!result.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return Page();
            }

            var policy = await _securityPolicyService.GetCurrentPolicyAsync();
            user.PasswordHistory = AddPasswordHistory(
                user.PasswordHistory,
                previousHash,
                policy.PasswordHistoryCount);
            user.RequiresPasswordChange = false;
            user.LastPasswordChangeDate = _timeProvider.GetUtcNow().UtcDateTime;
            var update = await _userManager.UpdateAsync(user);
            if (!update.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return RedirectToPage("./Login");
            }

            await _userManager.ResetAccessFailedCountAsync(user);
            await _auditService.LogEventAsync(
                "Account.RequiredPasswordChange",
                user.Id.ToString(),
                "User completed a required local password change",
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            RequiredPasswordChangeSession.Clear(HttpContext.Session);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        return RedirectToPage("./Login", new { returnUrl = pending.ReturnUrl });
    }

    private static string AddPasswordHistory(
        string serializedHistory,
        string? previousHash,
        int maximumCount)
    {
        if (maximumCount <= 0 || string.IsNullOrWhiteSpace(previousHash))
        {
            return serializedHistory;
        }

        List<string> history;
        try
        {
            history = JsonSerializer.Deserialize<List<string>>(serializedHistory) ?? [];
        }
        catch (JsonException)
        {
            history = [];
        }

        history.Insert(0, previousHash);
        return JsonSerializer.Serialize(
            history.Distinct(StringComparer.Ordinal).Take(maximumCount).ToList());
    }
}
