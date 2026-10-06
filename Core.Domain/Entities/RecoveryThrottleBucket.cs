namespace Core.Domain.Entities;

/// <summary>Shared recovery budget; the partition is a purpose-separated keyed hash, never a raw account/IP.</summary>
public sealed class RecoveryThrottleBucket
{
    private RecoveryThrottleBucket() { }

    public RecoveryThrottleBucket(string partitionHash, DateTimeOffset windowStartedAtUtc, DateTimeOffset expiresAtUtc)
    {
        RecoveryStateGuard.Text(partitionHash, 64);
        RecoveryStateGuard.Lifetime(windowStartedAtUtc, expiresAtUtc);
        PartitionHash = partitionHash;
        WindowStartedAtUtc = windowStartedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public string PartitionHash { get; private set; } = string.Empty;
    public DateTimeOffset WindowStartedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public int Attempts { get; private set; }
    public long Version { get; private set; } = 1;

    public bool TryTake(DateTimeOffset now, int limit, TimeSpan window)
    {
        if (limit < 1 || window <= TimeSpan.Zero || now < WindowStartedAtUtc) return false;
        if (ExpiresAtUtc <= now)
        {
            WindowStartedAtUtc = now;
            ExpiresAtUtc = now.Add(window);
            Attempts = 0;
        }
        if (Attempts >= limit) return false;
        Attempts++;
        Version++;
        return true;
    }
}
