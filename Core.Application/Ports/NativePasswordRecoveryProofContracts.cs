namespace Core.Application.Ports;

public interface INativePasswordRecoveryProofService
{
    Task<NativeRecoveryStartResult> StartAsync(
        NativeRecoveryStartRequest request,
        CancellationToken cancellationToken = default);

    Task<NativeRecoveryVerificationResult> VerifyAsync(
        NativeRecoveryVerificationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record NativeRecoveryContext(string ContextHash, string CsrfHash);

public sealed record NativeRecoveryStartRequest(
    string Identifier,
    NativeRecoveryContext Context);

public sealed record NativeRecoveryStartResult(Guid RequestId);

public sealed record NativeRecoveryVerificationRequest(
    Guid RequestId,
    string Code,
    NativeRecoveryContext Context);

public sealed record NativeRecoveryVerificationResult(
    NativeRecoveryVerificationOutcome Outcome,
    string? Proof = null);

public enum NativeRecoveryVerificationOutcome
{
    Denied,
    Verified
}
