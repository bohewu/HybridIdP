using System.Security.Cryptography;
using System.Text;
using Core.Domain.Entities;

namespace Core.Application.Ports;

public interface IRecoveryDestinationResolver
{
    Task<RecoveryDestinationResult> ResolveAsync(Guid localAccountId, CancellationToken cancellationToken = default);
}

// Server-only evidence seam shared with the verification-period evaluator. No HTTP call.
public interface IRecoveryDefaultDestinationEvaluator
{
    Task<RecoveryDefaultDestination?> EvaluateDefaultAsync(Guid localAccountId, CancellationToken cancellationToken = default);
}

public enum RecoveryDestinationFailure
{
    None, PolicyDisabled, IneligibleAccount, Blocked, InvalidCustom, SourceOptOut, NoEligibleDefault, LookupFailed
}

public sealed record RecoveryDestinationResult(RecoveryDestinationFailure Failure, RecoveryDestination? Destination = null);

/// <summary>Server-only recipient; never serialize into an anonymous response or structured log.</summary>
public sealed record RecoveryDestination(
    Guid LocalAccountId,
    string Address,
    string MaskedAddress,
    RecoveryDestinationKind Kind,
    string Fingerprint,
    long Version,
    long SelectionEpoch,
    string EffectivePolicyDigest,
    Guid? RecoveryEmailId)
{
    public bool Matches(RecoveryDestination other) =>
        LocalAccountId == other.LocalAccountId && Kind == other.Kind &&
        Fingerprint == other.Fingerprint && Version == other.Version &&
        SelectionEpoch == other.SelectionEpoch && EffectivePolicyDigest == other.EffectivePolicyDigest;

    public override string ToString() => nameof(RecoveryDestination);
}

public sealed record RecoveryDefaultDestination(
    string Address, string Fingerprint, long Version, bool BootstrapActive)
{
    public override string ToString() => nameof(RecoveryDefaultDestination);
}

/// <summary>Canonical ordered UTF-8 length-prefixed digest for server-owned recovery bindings.</summary>
public static class RecoveryDestinationBinding
{
    public static string ComputeDigest(params string?[] fields)
    {
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var field in fields)
            {
                if (field is null) { writer.Write(-1); continue; }
                var encoded = Encoding.UTF8.GetBytes(field);
                writer.Write(encoded.Length);
                writer.Write(encoded);
            }
        }
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }
}
