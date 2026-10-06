namespace Core.Domain.Entities;

/// <summary>Recovery-only durable notification intent. No free-form body or proof material.</summary>
public sealed class RecoveryNotification
{
    private RecoveryNotification() { }

    public RecoveryNotification(Guid localAccountId, long selectionEpoch, RecoveryNotificationKind kind,
        string recipient, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc, int maxAttempts)
    {
        RecoveryStateGuard.Account(localAccountId);
        RecoveryStateGuard.Text(recipient, 320);
        RecoveryStateGuard.Lifetime(createdAtUtc, expiresAtUtc);
        if (selectionEpoch < 1 || maxAttempts < 1 || !Enum.IsDefined(kind)) throw new ArgumentException("Invalid notification bounds.");
        Id = Guid.NewGuid();
        LocalAccountId = localAccountId;
        SelectionEpoch = selectionEpoch;
        Kind = kind;
        Recipient = recipient;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        NextAttemptAtUtc = createdAtUtc;
        MaxAttempts = maxAttempts;
    }

    public Guid Id { get; private set; }
    public Guid LocalAccountId { get; private set; }
    public long SelectionEpoch { get; private set; }
    public RecoveryNotificationKind Kind { get; private set; }
    public string Recipient { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset NextAttemptAtUtc { get; private set; }
    public int Attempts { get; private set; }
    public int MaxAttempts { get; private set; }
    public Guid? LeaseId { get; private set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; private set; }
    public DateTimeOffset? DeliveredAtUtc { get; private set; }
    public DateTimeOffset? AbandonedAtUtc { get; private set; }
    public long Version { get; private set; } = 1;

    public bool TryClaim(Guid leaseId, DateTimeOffset now, DateTimeOffset leaseExpiresAtUtc)
    {
        RecoveryStateGuard.Account(leaseId);
        if (DeliveredAtUtc is not null || AbandonedAtUtc is not null || Attempts >= MaxAttempts ||
            ExpiresAtUtc <= now || NextAttemptAtUtc > now || LeaseExpiresAtUtc > now ||
            leaseExpiresAtUtc <= now || leaseExpiresAtUtc > ExpiresAtUtc) return false;
        LeaseId = leaseId;
        LeaseExpiresAtUtc = leaseExpiresAtUtc;
        Attempts++;
        Version++;
        return true;
    }

    public bool TryAbandon(DateTimeOffset now)
    {
        if (DeliveredAtUtc is not null || AbandonedAtUtc is not null || LeaseExpiresAtUtc > now ||
            ExpiresAtUtc > now && Attempts < MaxAttempts) return false;
        AbandonedAtUtc = now;
        LeaseId = null;
        LeaseExpiresAtUtc = null;
        Version++;
        return true;
    }

    public bool TryComplete(Guid leaseId, DateTimeOffset now)
    {
        if (!OwnsLease(leaseId, now)) return false;
        DeliveredAtUtc = now;
        LeaseId = null;
        LeaseExpiresAtUtc = null;
        Version++;
        return true;
    }

    public bool TryFail(Guid leaseId, DateTimeOffset now, DateTimeOffset retryAtUtc)
    {
        if (!OwnsLease(leaseId, now) || retryAtUtc <= now) return false;
        if (Attempts >= MaxAttempts || retryAtUtc >= ExpiresAtUtc) AbandonedAtUtc = now;
        NextAttemptAtUtc = retryAtUtc;
        LeaseId = null;
        LeaseExpiresAtUtc = null;
        Version++;
        return true;
    }

    private bool OwnsLease(Guid leaseId, DateTimeOffset now) => leaseId != Guid.Empty && LeaseId == leaseId &&
        LeaseExpiresAtUtc > now && DeliveredAtUtc is null && AbandonedAtUtc is null && ExpiresAtUtc > now;
}

public enum RecoveryNotificationKind { CustomChangedOldDestination, CustomChangedNewDestination, DefaultSelected }
