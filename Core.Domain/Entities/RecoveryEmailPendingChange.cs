namespace Core.Domain.Entities;

/// <summary>A candidate destination, isolated from the effective recovery record.</summary>
public sealed class RecoveryEmailPendingChange
{
    private RecoveryEmailPendingChange() { }

    public RecoveryEmailPendingChange(Guid localAccountId, string address, string normalizedAddress,
        string codeHash, long expectedSelectionEpoch, string securityStamp, string contextHash,
        string csrfHash, Guid stepUpGrantId, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc,
        DateTimeOffset nextSendAllowedAtUtc, int maxAttempts)
    {
        RecoveryStateGuard.Account(localAccountId);
        RecoveryStateGuard.Account(stepUpGrantId);
        RecoveryStateGuard.Text(address, 320);
        RecoveryStateGuard.Text(normalizedAddress, 320);
        RecoveryStateGuard.Text(codeHash, 512);
        RecoveryStateGuard.Text(securityStamp, 256);
        RecoveryStateGuard.Text(contextHash, 256);
        RecoveryStateGuard.Text(csrfHash, 256);
        RecoveryStateGuard.Lifetime(createdAtUtc, expiresAtUtc);
        if (expectedSelectionEpoch < 1 || maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        Id = Guid.NewGuid();
        LocalAccountId = localAccountId;
        Address = address;
        NormalizedAddress = normalizedAddress;
        CodeHash = codeHash;
        ExpectedSelectionEpoch = expectedSelectionEpoch;
        SecurityStamp = securityStamp;
        ContextHash = contextHash;
        CsrfHash = csrfHash;
        StepUpGrantId = stepUpGrantId;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        NextSendAllowedAtUtc = nextSendAllowedAtUtc;
        MaxAttempts = maxAttempts;
    }

    public Guid Id { get; private set; }
    public Guid LocalAccountId { get; private set; }
    public string Address { get; private set; } = string.Empty;
    public string NormalizedAddress { get; private set; } = string.Empty;
    public string CodeHash { get; private set; } = string.Empty;
    public long ExpectedSelectionEpoch { get; private set; }
    public string SecurityStamp { get; private set; } = string.Empty;
    public string ContextHash { get; private set; } = string.Empty;
    public string CsrfHash { get; private set; } = string.Empty;
    public Guid StepUpGrantId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset NextSendAllowedAtUtc { get; private set; }
    public DateTimeOffset? DeliveredAtUtc { get; private set; }
    public int VerificationAttempts { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public long Version { get; private set; } = 1;

    public bool IsPending(DateTimeOffset now) => ConsumedAtUtc is null && RevokedAtUtc is null && ExpiresAtUtc > now;

    public bool TryReserveAttempt(DateTimeOffset now)
    {
        if (!IsPending(now) || DeliveredAtUtc is null || VerificationAttempts >= MaxAttempts) return false;
        VerificationAttempts++;
        Version++;
        return true;
    }

    public bool TryReserveResend(string codeHash, DateTimeOffset now, DateTimeOffset nextSendAllowedAtUtc)
    {
        RecoveryStateGuard.Text(codeHash, 512);
        if (!IsPending(now) || NextSendAllowedAtUtc > now || VerificationAttempts >= MaxAttempts || nextSendAllowedAtUtc <= now)
            return false;
        CodeHash = codeHash;
        DeliveredAtUtc = null;
        NextSendAllowedAtUtc = nextSendAllowedAtUtc;
        Version++;
        return true;
    }

    public bool TryMarkDelivered(DateTimeOffset now)
    {
        if (!IsPending(now) || DeliveredAtUtc is not null) return false;
        DeliveredAtUtc = now;
        Version++;
        return true;
    }

    // The caller must verify the reserved attempt's code and fresh step-up, then commit this
    // together with the preference CAS, active address and notification intents.
    public bool TryConsume(long currentEpoch, DateTimeOffset now)
    {
        if (!IsPending(now) || DeliveredAtUtc is null || currentEpoch != ExpectedSelectionEpoch ||
            VerificationAttempts < 1 || VerificationAttempts > MaxAttempts) return false;
        ConsumedAtUtc = now;
        Version++;
        return true;
    }

    public void Revoke(DateTimeOffset now)
    {
        if (ConsumedAtUtc is not null || RevokedAtUtc is not null) return;
        RevokedAtUtc = now;
        Version++;
    }
}
