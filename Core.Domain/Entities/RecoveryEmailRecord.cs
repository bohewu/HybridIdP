namespace Core.Domain.Entities;

/// <summary>
/// An independently enrolled recovery destination. It is not profile email or email MFA state.
/// </summary>
public sealed class RecoveryEmailRecord
{
    private RecoveryEmailRecord()
    {
    }

    public RecoveryEmailRecord(
        Guid localAccountId,
        string address,
        string normalizedAddress,
        DateTimeOffset createdAtUtc)
    {
        if (localAccountId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(localAccountId));
        }

        SetAddress(address, normalizedAddress, createdAtUtc);
        Id = Guid.NewGuid();
        LocalAccountId = localAccountId;
        CreatedAtUtc = createdAtUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid LocalAccountId { get; private set; }
    public string Address { get; private set; } = string.Empty;
    public string NormalizedAddress { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? VerifiedAtUtc { get; private set; }
    public DateTimeOffset? NextSendAllowedAtUtc { get; private set; }
    public Guid? LastAdministrativeActorId { get; private set; }
    public string? LastAdministrativeReason { get; private set; }
    public string? LastIdentityCheckEvidence { get; private set; }
    public long Version { get; private set; }

    public void ReplaceAddress(
        string address,
        string normalizedAddress,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset nextSendAllowedAtUtc,
        Guid? administrativeActorId = null,
        string? administrativeReason = null,
        string? identityCheckEvidence = null)
    {
        var hasAdministrativeMetadata = administrativeActorId is not null ||
            administrativeReason is not null || identityCheckEvidence is not null;
        if (hasAdministrativeMetadata &&
            (administrativeActorId is null || administrativeActorId == Guid.Empty ||
             string.IsNullOrWhiteSpace(administrativeReason) ||
             string.IsNullOrWhiteSpace(identityCheckEvidence)))
        {
            throw new ArgumentException("Administrative replacement requires an actor, reason, and identity-check evidence.");
        }

        SetAddress(address, normalizedAddress, updatedAtUtc);
        VerifiedAtUtc = null;
        NextSendAllowedAtUtc = nextSendAllowedAtUtc;
        LastAdministrativeActorId = administrativeActorId;
        LastAdministrativeReason = administrativeReason;
        LastIdentityCheckEvidence = identityCheckEvidence;
        Version++;
    }

    public void ReserveInitialSend(DateTimeOffset nextSendAllowedAtUtc)
    {
        NextSendAllowedAtUtc = nextSendAllowedAtUtc;
        Version++;
    }

    public bool TryReserveSend(DateTimeOffset now, DateTimeOffset nextSendAllowedAtUtc)
    {
        if (VerifiedAtUtc is null || NextSendAllowedAtUtc > now)
        {
            return false;
        }

        NextSendAllowedAtUtc = nextSendAllowedAtUtc;
        Version++;
        return true;
    }

    public void MarkVerified(DateTimeOffset verifiedAtUtc)
    {
        VerifiedAtUtc = verifiedAtUtc;
        UpdatedAtUtc = verifiedAtUtc;
        Version++;
    }

    private void SetAddress(string address, string normalizedAddress, DateTimeOffset updatedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("A recovery address is required.", nameof(address));
        }

        if (string.IsNullOrWhiteSpace(normalizedAddress))
        {
            throw new ArgumentException("A normalized recovery address is required.", nameof(normalizedAddress));
        }

        Address = address;
        NormalizedAddress = normalizedAddress;
        UpdatedAtUtc = updatedAtUtc;
    }
}
