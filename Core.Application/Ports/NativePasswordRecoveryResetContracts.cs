namespace Core.Application.Ports;

public interface INativePasswordRecoveryResetService
{
    Task<NativeRecoveryResetResult> ResetAsync(
        NativeRecoveryResetRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record NativeRecoveryResetRequest(
    Guid RequestId,
    string Proof,
    string NewPassword,
    NativeRecoveryContext Context,
    bool UseAdministrativeApproval = false);

public sealed record NativeRecoveryResetResult(
    NativeRecoveryResetOutcome Outcome,
    IReadOnlyList<string>? ErrorCodes = null);

public enum NativeRecoveryResetOutcome
{
    Denied,
    PasswordRejected,
    Succeeded
}
