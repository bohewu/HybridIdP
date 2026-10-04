using Core.Application.DTOs;

namespace Core.Application.Ports;

public interface IRecoveryIdentityVerificationClient
{
    /// <summary>
    /// Performs one verification against a server-selected existing binding. Returns only a
    /// strictly validated, request-bound response, or null on invalid/operational failure.
    /// Caller cancellation propagates. No retry, grant, OTP, session or reset is performed.
    /// The request and evidence must never be persisted or logged.
    /// </summary>
    Task<RecoveryIdentityVerificationResponse?> VerifyAsync(
        RecoveryIdentityVerificationRequest request,
        CancellationToken cancellationToken = default);
}
