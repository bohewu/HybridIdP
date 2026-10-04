using System.Buffers;
using System.Text;

namespace Core.Application.DTOs;

public static class RecoveryIdentityVerificationContract
{
    public const string CurrentVersion = "1.0";
    public const string Baseline = "RIV-1.0-PLAN-20260930-A";
    public const string Scheme = "identity-identifier";
    public const int MaximumRequestBytes = 8 * 1024;
    public const int MaximumResponseBytes = 4 * 1024;
    public const int MaximumJsonDepth = 8;

    public static bool IsRequestId(string? value) =>
        value is { Length: 36 } && Guid.TryParseExact(value, "D", out var id) &&
        id != Guid.Empty && string.Equals(value, id.ToString("D"), StringComparison.Ordinal);

    // Count Unicode scalar values, rejecting malformed UTF-16 instead of replacing it.
    // Opaque binding values are never trimmed or case-folded.
    public static bool IsText(string? value, int maximumCodePoints, bool rejectWhitespace = false)
    {
        if (string.IsNullOrEmpty(value) || (rejectWhitespace && string.IsNullOrWhiteSpace(value)))
            return false;

        var remaining = value.AsSpan();
        var count = 0;
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) != OperationStatus.Done ||
                Rune.IsControl(rune) || ++count > maximumCodePoints)
                return false;
            remaining = remaining[consumed..];
        }
        return true;
    }
}

// These objects contain transient identity evidence. Never destructure them in logging.
// Explicit redaction also prevents record-generated ToString from disclosing their values.
public sealed record RecoveryIdentityVerificationRequest
{
    public string ContractVersion { get; init; } = RecoveryIdentityVerificationContract.CurrentVersion;
    public string RequestId { get; init; } = string.Empty;
    public string ProviderNamespace { get; init; } = string.Empty;
    public string StableSubject { get; init; } = string.Empty;
    public string Scheme { get; init; } = RecoveryIdentityVerificationContract.Scheme;
    public RecoveryIdentityVerificationEvidence Evidence { get; init; } = new();

    public bool IsValid() =>
        ContractVersion == RecoveryIdentityVerificationContract.CurrentVersion &&
        RecoveryIdentityVerificationContract.IsRequestId(RequestId) &&
        RecoveryIdentityVerificationContract.IsText(ProviderNamespace, 200) &&
        RecoveryIdentityVerificationContract.IsText(StableSubject, 256) &&
        Scheme == RecoveryIdentityVerificationContract.Scheme &&
        Evidence is not null && Evidence.IsValid();

    public override string ToString() => "RecoveryIdentityVerificationRequest [redacted]";
}

public sealed record RecoveryIdentityVerificationEvidence
{
    public string IdentityIdentifier { get; init; } = string.Empty;
    public bool IsValid() => RecoveryIdentityVerificationContract.IsText(IdentityIdentifier, 128, true);
    public override string ToString() => "RecoveryIdentityVerificationEvidence [redacted]";
}

public sealed record RecoveryIdentityVerificationBinding
{
    public string ProviderNamespace { get; init; } = string.Empty;
    public string StableSubject { get; init; } = string.Empty;
    public string Scheme { get; init; } = RecoveryIdentityVerificationContract.Scheme;

    public bool IsValid() =>
        RecoveryIdentityVerificationContract.IsText(ProviderNamespace, 200) &&
        RecoveryIdentityVerificationContract.IsText(StableSubject, 256) &&
        Scheme == RecoveryIdentityVerificationContract.Scheme;

    public override string ToString() => "RecoveryIdentityVerificationBinding [redacted]";
}

public enum RecoveryIdentityVerificationOutcome
{
    Verified,
    Denied,
    Unavailable,
    Unsupported,
    Malformed
}

public sealed record RecoveryIdentityVerificationResponse
{
    public string ContractVersion { get; init; } = RecoveryIdentityVerificationContract.CurrentVersion;
    public string? RequestId { get; init; }
    public RecoveryIdentityVerificationOutcome Outcome { get; init; }
    public RecoveryIdentityVerificationBinding? Binding { get; init; }

    public bool IsValid() =>
        ContractVersion == RecoveryIdentityVerificationContract.CurrentVersion &&
        Enum.IsDefined(Outcome) &&
        (RecoveryIdentityVerificationContract.IsRequestId(RequestId) ||
         (RequestId is null && Outcome == RecoveryIdentityVerificationOutcome.Malformed)) &&
        (Outcome == RecoveryIdentityVerificationOutcome.Verified
            ? Binding is not null && Binding.IsValid()
            : Binding is null);

    public override string ToString() => "RecoveryIdentityVerificationResponse [redacted]";
}
