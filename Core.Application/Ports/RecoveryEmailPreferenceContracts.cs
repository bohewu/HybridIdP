namespace Core.Application.Ports;

public interface IRecoveryEmailPreferenceService
{
    bool Enabled { get; }
    Task<RecoveryPreferenceStatus> GetStatusAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<RecoveryProofOutcome> BeginAsync(Guid accountId, string candidate, RecoveryPreferenceContext context, CancellationToken cancellationToken = default);
    Task<RecoveryProofOutcome> VerifyAsync(Guid accountId, string code, RecoveryPreferenceContext context, CancellationToken cancellationToken = default);
    Task<RecoveryProofOutcome> ResendAsync(Guid accountId, RecoveryPreferenceContext context, CancellationToken cancellationToken = default);
    Task<RecoveryProofOutcome> CancelAsync(Guid accountId, RecoveryPreferenceContext context, CancellationToken cancellationToken = default);
    Task<RecoveryDefaultConfirmation?> PrepareDefaultAsync(Guid accountId, RecoveryPreferenceContext context, CancellationToken cancellationToken = default);
    Task<RecoveryProofOutcome> UseDefaultAsync(Guid accountId, string confirmation, RecoveryPreferenceContext context, CancellationToken cancellationToken = default);
}

// Constructed only by the authenticated controller from server session state.
public sealed record RecoveryPreferenceContext(string ContextHash, string CsrfHash, Guid StepUpGrantId)
{
    public override string ToString() => nameof(RecoveryPreferenceContext);
}

// A valid Mode selects this status shape even when Enabled=false (read-only).
// "unavailable" means no selection policy/state: the controller retains its legacy GET contract.
public sealed record RecoveryPreferenceStatus(bool Enabled, string Mode, string? MaskedAddress,
    string? MaskedDefaultAddress, string? MaskedPendingAddress, DateTimeOffset? PendingExpiresAtUtc,
    DateTimeOffset? NextSendAllowedAtUtc);
public sealed record RecoveryDefaultConfirmation(string MaskedAddress, string Confirmation);
