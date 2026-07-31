namespace Core.Application;

/// <summary>
/// Result of atomically reserving one verification attempt for a pending email MFA code.
/// </summary>
public enum EmailMfaAttemptReservation
{
    Rejected,
    Reserved,
    FinalAttempt
}

/// <summary>
/// Persists the bounded verification budget for a pending email MFA code.
/// </summary>
public interface IEmailMfaAttemptStore
{
    /// <summary>
    /// Atomically reserves one attempt only while the same unexpired code is pending.
    /// </summary>
    Task<EmailMfaAttemptReservation> TryReserveAttemptAsync(
        Guid userId,
        string codeHash,
        DateTime utcNow,
        int maxAttempts,
        CancellationToken ct = default);

    /// <summary>
    /// Invalidates the matching pending code after its final allowed attempt fails.
    /// </summary>
    Task InvalidatePendingCodeAsync(
        Guid userId,
        string codeHash,
        CancellationToken ct = default);
}
