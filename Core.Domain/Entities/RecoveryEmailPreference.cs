namespace Core.Domain.Entities;

/// <summary>Account-scoped selection; absence is legacy state, never implicit user intent.</summary>
public sealed class RecoveryEmailPreference
{
    private RecoveryEmailPreference() { }

    public RecoveryEmailPreference(Guid localAccountId, DateTimeOffset now)
    {
        RecoveryStateGuard.Account(localAccountId);
        LocalAccountId = localAccountId;
        UpdatedAtUtc = now;
    }

    public Guid LocalAccountId { get; private set; }
    public RecoveryEmailSelectionMode Mode { get; private set; } = RecoveryEmailSelectionMode.Legacy;
    public long SelectionEpoch { get; private set; } = 1;
    public long Version { get; private set; } = 1;
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? SourceDefaultOptOutAtUtc { get; private set; }
    public DateTimeOffset? AdministrativeBlockedAtUtc { get; private set; }

    public bool TrySelect(RecoveryEmailSelectionMode mode, long expectedEpoch, DateTimeOffset now)
    {
        if (!Enum.IsDefined(mode) || mode == RecoveryEmailSelectionMode.Legacy ||
            SelectionEpoch != expectedEpoch || AdministrativeBlockedAtUtc is not null)
            return false;
        Mode = mode;
        Advance(now);
        return true;
    }

    // Separate operations deliberately do not clear each other's denial evidence.
    public void SetSourceDefaultOptOut(bool optedOut, DateTimeOffset now)
    {
        SourceDefaultOptOutAtUtc = optedOut ? now : null;
        Advance(now);
    }

    public void SetAdministrativeBlock(bool blocked, DateTimeOffset now)
    {
        AdministrativeBlockedAtUtc = blocked ? now : null;
        Advance(now);
    }

    public void Advance(DateTimeOffset now)
    {
        SelectionEpoch = checked(SelectionEpoch + 1);
        Version = checked(Version + 1);
        UpdatedAtUtc = now;
    }
}

public enum RecoveryEmailSelectionMode { Legacy, UseDefault, UseCustom, Disabled }
public enum RecoveryEmailProvenance { LegacyUnknown, UserVerified, AdminAssistedVerified, SourceDefault }
public enum RecoveryDestinationKind { Legacy, Custom, TrustedDefault }

internal static class RecoveryStateGuard
{
    public static void Account(Guid value)
    {
        if (value == Guid.Empty) throw new ArgumentException("An account or state identifier is required.");
    }

    public static void Text(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            throw new ArgumentException("Recovery state binding is missing or exceeds its limit.");
    }

    public static void Lifetime(DateTimeOffset created, DateTimeOffset expires)
    {
        if (expires <= created) throw new ArgumentException("A positive recovery lifetime is required.");
    }

    public static void Selection(long epoch, RecoveryDestinationKind kind, string fingerprint, long version)
    {
        if (epoch < 1 || version < 1 || !Enum.IsDefined(kind))
            throw new ArgumentException("A complete recovery selection is required.");
        Text(fingerprint, 64);
    }
}
