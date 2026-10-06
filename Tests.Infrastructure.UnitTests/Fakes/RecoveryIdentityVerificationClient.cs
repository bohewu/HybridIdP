using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Application.Utilities;

namespace Tests.Infrastructure.UnitTests.Fakes;

/// <summary>Offline contract fake. Never stores evidence, caches a result or creates a grant.</summary>
internal sealed class RecoveryIdentityVerificationClient(
    Func<RecoveryIdentityVerificationRequest, CancellationToken,
        Task<(int StatusCode, string? ContentType, byte[] Body)>> respond) : IRecoveryIdentityVerificationClient
{
    public int Calls { get; private set; }

    public async Task<RecoveryIdentityVerificationResponse?> VerifyAsync(
        RecoveryIdentityVerificationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!RecoveryIdentityVerificationJson.TrySerializeRequest(request, out _)) return null;
        Calls++;
        var response = await respond(request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return RecoveryIdentityVerificationJson.TryReadResponse(response.StatusCode, response.ContentType,
            response.Body, request, out var result) ? result : null;
    }
}
