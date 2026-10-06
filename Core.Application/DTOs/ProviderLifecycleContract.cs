using System.Buffers;
using System.Text;

namespace Core.Application.DTOs;

public static class ProviderLifecycleContract
{
    public const string Type = "lifecycle-status";
    public const string Version = "1.0";
    public const string Discriminator = "lifecycle-status/1.0";
    public const int MaximumRequestBytes = 4096;
    public const int MaximumResponseBytes = 8192;
    public const int MaximumJsonDepth = 8;

    public static bool IsRequestId(string? value) => value is { Length: 36 } &&
        Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty && value == id.ToString("D");

    public static bool IsOpaque(string? value, int maximumScalars)
    {
        if (string.IsNullOrEmpty(value)) return false;
        var remaining = value.AsSpan();
        var count = 0;
        var hasNonWhitespace = false;
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) != OperationStatus.Done ||
                Rune.IsControl(rune) || ++count > maximumScalars) return false;
            hasNonWhitespace |= !Rune.IsWhiteSpace(rune);
            remaining = remaining[consumed..];
        }
        return hasNonWhitespace;
    }
}

// Transient server-only data. Never destructure these objects in logs or claims.
public sealed record ProviderLifecycleBinding
{
    public string ProviderNamespace { get; init; } = string.Empty;
    public string StableSubject { get; init; } = string.Empty;
    public bool IsValid() => ProviderLifecycleContract.IsOpaque(ProviderNamespace, 200) &&
        ProviderLifecycleContract.IsOpaque(StableSubject, 256);
    public override string ToString() => "ProviderLifecycleBinding [redacted]";
}

public sealed record ProviderLifecycleRequest
{
    public string RequestId { get; init; } = string.Empty;
    public ProviderLifecycleBinding Binding { get; init; } = new();
    public bool IsValid() => ProviderLifecycleContract.IsRequestId(RequestId) && Binding is not null && Binding.IsValid();
    public override string ToString() => "ProviderLifecycleRequest [redacted]";
}

public enum ProviderLifecycleOutcome { Found, NotFound, Ambiguous, Unavailable, Unsupported, Malformed }
public enum ProviderLifecycleAccountState { Enabled, Disabled, Retired, Superseded, Unknown }

public sealed record ProviderLifecycleEvidence
{
    public string SourceAuthority { get; init; } = string.Empty;
    public string MappingVersion { get; init; } = string.Empty;
    public string SnapshotVersion { get; init; } = string.Empty;
    public DateTimeOffset ObservedAt { get; init; }
    public DateTimeOffset EffectiveFrom { get; init; }
    public DateTimeOffset EffectiveUntil { get; init; }
    public override string ToString() => "ProviderLifecycleEvidence [redacted]";
}

public sealed record ProviderLifecycleResponse
{
    public string? RequestId { get; init; }
    public ProviderLifecycleOutcome Outcome { get; init; }
    public ProviderLifecycleBinding? Binding { get; init; }
    public ProviderLifecycleAccountState? AccountState { get; init; }
    public ProviderLifecycleEvidence? Evidence { get; init; }
    public ProviderLifecycleBinding? Successor { get; init; }
    public override string ToString() => "ProviderLifecycleResponse [redacted]";
}
