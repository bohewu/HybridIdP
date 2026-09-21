using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Core.Application.DTOs;
using Core.Application.Ports;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Web.IdP.Pages.Account;

/// <summary>
/// Browser boundary for the two-step migration ceremony. It stores the opaque continuation only
/// in the server session and always returns the same public failure message.
/// </summary>
[EnableRateLimiting("login")]
public sealed class CredentialMigrationModel : PageModel
{
    private const string ContextKey = "credential-migration.context";
    private const string CsrfKey = "credential-migration.csrf";
    private const string ContinuationKey = "credential-migration.continuation";
    private const string RecoveryContinuationKey = "credential-migration.recovery-continuation";
    private const string OtpRequiredKey = "credential-migration.otp-required";
    private const string OtpProofKey = "credential-migration.otp-proof";
    private const string ResetApprovalObservedKey = "credential-migration.reset-approval-observed";

    private readonly IStage2CredentialMigrationService _ceremony;
    private readonly ICredentialMigrationRecoveryService? _recovery;
    private readonly IMigrationOtpProofService? _migrationOtp;
    private readonly IRecoveryEmailService? _recoveryEmail;
    private readonly IRecoveryAssistanceService? _recoveryAssistance;

    public CredentialMigrationModel(
        IStage2CredentialMigrationService ceremony,
        ICredentialMigrationRecoveryService? recovery = null,
        IMigrationOtpProofService? migrationOtp = null,
        IRecoveryEmailService? recoveryEmail = null,
        IRecoveryAssistanceService? recoveryAssistance = null)
    {
        _ceremony = ceremony;
        _recovery = recovery;
        _migrationOtp = migrationOtp;
        _recoveryEmail = recoveryEmail;
        _recoveryAssistance = recoveryAssistance;
    }

    [BindProperty]
    public BeginInput Input { get; set; } = new();

    [BindProperty]
    public CommitInput Commit { get; set; } = new();

    [BindProperty]
    public RecoveryInput Recovery { get; set; } = new();

    [BindProperty]
    public ProofInput Proof { get; set; } = new();

    public bool AwaitingNewPassword { get; private set; }

    public bool AwaitingProof { get; private set; }

    public bool RequiresEmailOtp { get; private set; }

    public string ProofOutcomeCode { get; private set; } = CredentialMigrationOutcomeCodes.None;

    public int RetryAfterSeconds { get; private set; }

    public bool RequestDenied { get; private set; }

    public sealed class BeginInput
    {
        [Required(ErrorMessage = "CredentialMigration.AccountRequired")]
        public string AccountName { get; set; } = string.Empty;

        [Required(ErrorMessage = "CredentialMigration.CurrentPasswordRequired")]
        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = string.Empty;
    }

    public sealed class CommitInput
    {
        [Required(ErrorMessage = "CredentialMigration.NewPasswordRequired")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "CredentialMigration.ConfirmPasswordRequired")]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "CredentialMigration.PasswordMismatch")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public sealed class RecoveryInput
    {
        [Required]
        public Guid LocalAccountId { get; set; }

        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;
    }

    public sealed class ProofInput
    {
        [Required(ErrorMessage = "CredentialMigration.ProofCodeRequired")]
        public string Code { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await HttpContext.Session.LoadAsync();
        RestoreCeremonyState();
        return Page();
    }

    public async Task<IActionResult> OnPostBeginAsync(CancellationToken cancellationToken)
    {
        await HttpContext.Session.LoadAsync(cancellationToken);
        ClearCeremonyState();
        RetainModelStateFor(nameof(Input));
        if (!ModelState.IsValid)
        {
            return DeniedPage();
        }

        var context = CreateContext();
        var result = await _ceremony.BeginAsync(
            new MigrationProofCeremonyRequest(
                Input.AccountName,
                Input.CurrentPassword,
                context,
                EmailOtpPolicy.ProviderRequested),
            cancellationToken);
        if (result.Outcome != MigrationCeremonyOutcome.ContinuationIssued || string.IsNullOrEmpty(result.Continuation))
        {
            return DeniedPage();
        }

        HttpContext.Session.SetString(ContinuationKey, result.Continuation);
        HttpContext.Session.SetString(OtpRequiredKey, result.RequiresEmailOtp ? "1" : "0");
        RequiresEmailOtp = result.RequiresEmailOtp;
        if (!result.RequiresEmailOtp)
        {
            AwaitingNewPassword = true;
            ProofOutcomeCode = CredentialMigrationOutcomeCodes.NotRequired;
            return Page();
        }

        await SendOtpAsync(result.Continuation, context, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostSendOtpAsync(CancellationToken cancellationToken) =>
        await SendOtpFromSessionAsync(cancellationToken);

    public async Task<IActionResult> OnPostResendOtpAsync(CancellationToken cancellationToken) =>
        await SendOtpFromSessionAsync(cancellationToken);

    public async Task<IActionResult> OnPostVerifyOtpAsync(CancellationToken cancellationToken)
    {
        await HttpContext.Session.LoadAsync(cancellationToken);
        RetainModelStateFor(nameof(Proof));
        if (!ModelState.IsValid || _migrationOtp is null || string.IsNullOrWhiteSpace(Proof.Code) ||
            !TryGetRequiredCeremony(out var continuation, out var context))
        {
            return ProofFailurePage(CredentialMigrationOutcomeCodes.Invalid);
        }

        var result = await _migrationOtp.VerifyAsync(
            new MigrationOtpVerificationRequest(continuation, context, Proof.Code),
            cancellationToken);
        if (result.Outcome == RecoveryProofOutcome.Success && !string.IsNullOrWhiteSpace(result.Proof))
        {
            HttpContext.Session.SetString(OtpProofKey, result.Proof);
            HttpContext.Session.Remove(ResetApprovalObservedKey);
            RequiresEmailOtp = true;
            AwaitingNewPassword = true;
            ProofOutcomeCode = CredentialMigrationOutcomeCodes.OtpVerified;
            return Page();
        }

        return ProofFailurePage(ToOutcomeCode(result.Outcome));
    }

    public async Task<IActionResult> OnPostVerifyRecoveryEmailAsync(CancellationToken cancellationToken)
    {
        await HttpContext.Session.LoadAsync(cancellationToken);
        RetainModelStateFor(nameof(Proof));
        if (!ModelState.IsValid || _recoveryEmail is null || string.IsNullOrWhiteSpace(Proof.Code) ||
            !TryGetRequiredCeremony(out var continuation, out var context))
        {
            return ProofFailurePage(CredentialMigrationOutcomeCodes.Invalid);
        }

        var outcome = await _recoveryEmail.VerifyForMigrationAsync(
            new MigrationRecoveryEmailVerificationRequest(continuation, context, Proof.Code),
            cancellationToken);
        if (outcome != RecoveryProofOutcome.Success)
        {
            return ProofFailurePage(ToOutcomeCode(outcome));
        }

        await SendOtpAsync(continuation, context, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCheckApprovalAsync(CancellationToken cancellationToken)
    {
        await HttpContext.Session.LoadAsync(cancellationToken);
        RetainModelStateFor();
        if (_recoveryAssistance is null ||
            !TryGetRequiredCeremony(out var continuation, out var context))
        {
            return ProofFailurePage(CredentialMigrationOutcomeCodes.Invalid);
        }

        var outcome = await _recoveryAssistance.GetResetApprovalStatusAsync(
            new ResetApprovalConsumptionRequest(continuation, context),
            cancellationToken);
        if (outcome == RecoveryProofOutcome.Success)
        {
            HttpContext.Session.Remove(OtpProofKey);
            HttpContext.Session.SetString(ResetApprovalObservedKey, "1");
            RequiresEmailOtp = true;
            AwaitingNewPassword = true;
            ProofOutcomeCode = CredentialMigrationOutcomeCodes.AdminApprovalAccepted;
            return Page();
        }

        return ProofFailurePage(
            outcome == RecoveryProofOutcome.Missing
                ? CredentialMigrationOutcomeCodes.AdminApprovalPending
                : ToOutcomeCode(outcome));
    }

    public async Task<IActionResult> OnPostCommitAsync(CancellationToken cancellationToken)
    {
        await HttpContext.Session.LoadAsync(cancellationToken);
        RetainModelStateFor(nameof(Commit));
        if (!ModelState.IsValid)
        {
            RestoreCeremonyState();
            return AwaitingNewPassword ? Page() : DeniedPage();
        }

        var continuation = HttpContext.Session.GetString(ContinuationKey);
        var requiresEmailOtp = HttpContext.Session.GetString(OtpRequiredKey) == "1";
        var otpProof = HttpContext.Session.GetString(OtpProofKey);
        var resetApprovalObserved = HttpContext.Session.GetString(ResetApprovalObservedKey) == "1";
        if (string.IsNullOrEmpty(continuation) || !TryGetContext(out var context))
        {
            return DeniedPage();
        }

        if (requiresEmailOtp && string.IsNullOrWhiteSpace(otpProof) && !resetApprovalObserved)
        {
            return ProofFailurePage(CredentialMigrationOutcomeCodes.ProofRequired);
        }

        // Remove browser access before consuming. The durable store provides the atomic replay defense.
        ClearCeremonyState();
        var result = await _ceremony.CommitAsync(
            new MigrationCommitCeremonyRequest(
                continuation,
                Commit.NewPassword,
                context,
                otpProof),
            cancellationToken);
        if (result.Outcome != MigrationCeremonyOutcome.Completed)
        {
            return DeniedPage();
        }

        return RedirectToPage("./Login");
    }

    public async Task<IActionResult> OnPostRecoveryBeginAsync(CancellationToken cancellationToken)
    {
        await HttpContext.Session.LoadAsync(cancellationToken);
        RetainModelStateFor(nameof(Recovery));
        HttpContext.Session.Remove(RecoveryContinuationKey);
        if (_recovery is null || Recovery.LocalAccountId == Guid.Empty)
        {
            return DeniedPage();
        }

        var context = CreateContext();
        var result = await _recovery.BeginDirectoryRecoveryAsync(
            new MigrationRecoveryBeginRequest(Recovery.LocalAccountId, context),
            cancellationToken);
        if (result.Outcome != MigrationRecoveryOutcome.Reconciled || string.IsNullOrEmpty(result.Continuation))
        {
            return DeniedPage();
        }

        HttpContext.Session.SetString(RecoveryContinuationKey, result.Continuation);
        return Page();
    }

    public async Task<IActionResult> OnPostRecoveryCommitAsync(CancellationToken cancellationToken)
    {
        await HttpContext.Session.LoadAsync(cancellationToken);
        RetainModelStateFor(nameof(Recovery));
        var continuation = HttpContext.Session.GetString(RecoveryContinuationKey);
        if (_recovery is null || string.IsNullOrEmpty(continuation) ||
            string.IsNullOrEmpty(Recovery.NewPassword) || !TryGetContext(out var context))
        {
            return DeniedPage();
        }

        HttpContext.Session.Remove(RecoveryContinuationKey);
        HttpContext.Session.Remove(ContextKey);
        HttpContext.Session.Remove(CsrfKey);
        var result = await _recovery.RecoverAsync(
            new MigrationRecoveryRequest(Guid.Empty, continuation, Recovery.NewPassword, context),
            cancellationToken);
        return result.Outcome == MigrationRecoveryOutcome.Reconciled
            ? RedirectToPage("./Login")
            : DeniedPage();
    }

    public async Task<IActionResult> OnPostRecoveryLocalAsync(CancellationToken cancellationToken)
    {
        RetainModelStateFor(nameof(Recovery));
        if (_recovery is null || Recovery.LocalAccountId == Guid.Empty)
        {
            return DeniedPage();
        }

        var result = await _recovery.RecoverAsync(
            new MigrationRecoveryRequest(Recovery.LocalAccountId),
            cancellationToken);
        return result.Outcome == MigrationRecoveryOutcome.Reconciled
            ? RedirectToPage("./Login")
            : DeniedPage();
    }

    private MigrationContinuationContext CreateContext()
    {
        var context = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var csrf = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        HttpContext.Session.SetString(ContextKey, context);
        HttpContext.Session.SetString(CsrfKey, csrf);
        return new MigrationContinuationContext(Hash(context), Hash(csrf));
    }

    private bool TryGetContext(out MigrationContinuationContext context)
    {
        var browserContext = HttpContext.Session.GetString(ContextKey);
        var csrf = HttpContext.Session.GetString(CsrfKey);
        if (string.IsNullOrEmpty(browserContext) || string.IsNullOrEmpty(csrf))
        {
            context = default!;
            return false;
        }

        context = new MigrationContinuationContext(Hash(browserContext), Hash(csrf));
        return true;
    }

    private IActionResult DeniedPage()
    {
        ClearCeremonyState();
        RequestDenied = true;
        AwaitingNewPassword = false;
        return Page();
    }

    private async Task<IActionResult> SendOtpFromSessionAsync(CancellationToken cancellationToken)
    {
        await HttpContext.Session.LoadAsync(cancellationToken);
        RetainModelStateFor();
        if (!TryGetRequiredCeremony(out var continuation, out var context))
        {
            return ProofFailurePage(CredentialMigrationOutcomeCodes.Invalid);
        }

        await SendOtpAsync(continuation, context, cancellationToken);
        return Page();
    }

    private async Task SendOtpAsync(
        string continuation,
        MigrationContinuationContext context,
        CancellationToken cancellationToken)
    {
        RequiresEmailOtp = true;
        AwaitingProof = true;
        AwaitingNewPassword = false;
        if (_migrationOtp is null)
        {
            ProofOutcomeCode = CredentialMigrationOutcomeCodes.Unavailable;
            return;
        }

        var result = await _migrationOtp.SendAsync(
            new MigrationOtpSendRequest(continuation, context),
            cancellationToken);
        RetryAfterSeconds = result.RetryAfterSeconds;
        ProofOutcomeCode = result.Outcome switch
        {
            RecoveryProofOutcome.Success => CredentialMigrationOutcomeCodes.OtpSent,
            RecoveryProofOutcome.Missing => CredentialMigrationOutcomeCodes.RecoveryEmailOrAdminRequired,
            _ => ToOutcomeCode(result.Outcome)
        };
    }

    private bool TryGetRequiredCeremony(
        out string continuation,
        out MigrationContinuationContext context)
    {
        context = default!;
        continuation = HttpContext.Session.GetString(ContinuationKey) ?? string.Empty;
        return HttpContext.Session.GetString(OtpRequiredKey) == "1" &&
               !string.IsNullOrEmpty(continuation) &&
               TryGetContext(out context);
    }

    private IActionResult ProofFailurePage(string outcomeCode)
    {
        RestoreCeremonyState();
        AwaitingNewPassword = false;
        AwaitingProof = true;
        RequiresEmailOtp = true;
        ProofOutcomeCode = outcomeCode;
        return Page();
    }

    private void RestoreCeremonyState()
    {
        var hasContinuation = !string.IsNullOrEmpty(HttpContext.Session.GetString(ContinuationKey));
        RequiresEmailOtp = hasContinuation && HttpContext.Session.GetString(OtpRequiredKey) == "1";
        var hasProof = !string.IsNullOrEmpty(HttpContext.Session.GetString(OtpProofKey)) ||
                       HttpContext.Session.GetString(ResetApprovalObservedKey) == "1";
        AwaitingProof = hasContinuation && RequiresEmailOtp && !hasProof;
        AwaitingNewPassword = hasContinuation && (!RequiresEmailOtp || hasProof);
        ProofOutcomeCode = RequiresEmailOtp
            ? hasProof ? CredentialMigrationOutcomeCodes.ProofReady : CredentialMigrationOutcomeCodes.ProofRequired
            : hasContinuation ? CredentialMigrationOutcomeCodes.NotRequired : CredentialMigrationOutcomeCodes.None;
    }

    private static string ToOutcomeCode(RecoveryProofOutcome outcome) => outcome switch
    {
        RecoveryProofOutcome.Invalid => CredentialMigrationOutcomeCodes.Invalid,
        RecoveryProofOutcome.Expired => CredentialMigrationOutcomeCodes.Expired,
        RecoveryProofOutcome.Exhausted => CredentialMigrationOutcomeCodes.Exhausted,
        RecoveryProofOutcome.Replayed => CredentialMigrationOutcomeCodes.Replayed,
        RecoveryProofOutcome.Cooldown => CredentialMigrationOutcomeCodes.Cooldown,
        RecoveryProofOutcome.Missing => CredentialMigrationOutcomeCodes.Missing,
        RecoveryProofOutcome.Unauthorized => CredentialMigrationOutcomeCodes.HighAssuranceRequired,
        _ => CredentialMigrationOutcomeCodes.Unavailable
    };

    private void ClearCeremonyState()
    {
        HttpContext.Session.Remove(ContextKey);
        HttpContext.Session.Remove(CsrfKey);
        HttpContext.Session.Remove(ContinuationKey);
        HttpContext.Session.Remove(RecoveryContinuationKey);
        HttpContext.Session.Remove(OtpRequiredKey);
        HttpContext.Session.Remove(OtpProofKey);
        HttpContext.Session.Remove(ResetApprovalObservedKey);
    }

    private void RetainModelStateFor(string? propertyName = null)
    {
        var prefix = propertyName is null ? null : $"{propertyName}.";
        foreach (var key in ModelState.Keys.ToArray())
        {
            if (key.Length == 0 ||
                (propertyName is not null &&
                 (key.Equals(propertyName, StringComparison.OrdinalIgnoreCase) ||
                  key.StartsWith(prefix!, StringComparison.OrdinalIgnoreCase))))
            {
                continue;
            }

            ModelState.Remove(key);
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public static class CredentialMigrationOutcomeCodes
{
    public const string None = "none";
    public const string NotRequired = "notRequired";
    public const string ProofRequired = "proofRequired";
    public const string ProofReady = "proofReady";
    public const string OtpSent = "otpSent";
    public const string OtpVerified = "otpVerified";
    public const string RecoveryEmailOrAdminRequired = "recoveryEmailOrAdminRequired";
    public const string AdminApprovalPending = "adminApprovalPending";
    public const string AdminApprovalAccepted = "adminApprovalAccepted";
    public const string Invalid = "invalid";
    public const string Missing = "missing";
    public const string Expired = "expired";
    public const string Exhausted = "exhausted";
    public const string Replayed = "replayed";
    public const string Cooldown = "cooldown";
    public const string HighAssuranceRequired = "highAssuranceRequired";
    public const string Unavailable = "unavailable";
}
