namespace Core.Application.Ports;

public interface IRecoveryPrecheckService
{
    bool Enabled { get; }
    bool IdentityInputEnabled { get; }
    string LabelResourceKey { get; }
    string HelpResourceKey { get; }
    Task<RecoveryPrepareResult> PrepareAsync(RecoveryPrepareRequest request, CancellationToken cancellationToken = default);
    Task<NativeRecoveryStartResult> SendOtpAsync(RecoverySendOtpRequest request, CancellationToken cancellationToken = default);
}

// Only Identifier and IdentityIdentifier are browser inputs. Context and source IP come from the host.
public sealed record RecoveryPrepareRequest(string Identifier, string IdentityIdentifier,
    NativeRecoveryContext Context, string SourceIp)
{
    public override string ToString() => "RecoveryPrepareRequest [redacted]";
}

// Server-only authority reference. MaskedDestination is omitted when public precheck hints are disabled.
public sealed record RecoveryPrepareResult(Guid? GrantId = null, string? MaskedDestination = null);
public sealed record RecoverySendOtpRequest(Guid GrantId, NativeRecoveryContext Context, string SourceIp);
