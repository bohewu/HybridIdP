using System.Security.Cryptography;
using System.Text;
using Core.Domain.Entities;
using Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class RecoveryThrottleService(ApplicationDbContext db,
    IOptions<RecoveryThrottleOptions> options, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    // No submitted identifier is hashed. Subject partitions use a resolved internal account ID.
    public async Task<bool> TakeAsync(string operation, string dimension, string value, int limit,
        TimeSpan window, CancellationToken cancellationToken)
    {
        var key = options.Value.HashKey;
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32) return false;
        var partition = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key),
            Encoding.UTF8.GetBytes($"recovery-v1\0{operation}\0{dimension}\0{value}")));
        var now = _time.GetUtcNow();
        var bucket = await db.RecoveryThrottleBuckets.SingleOrDefaultAsync(
            candidate => candidate.PartitionHash == partition, cancellationToken);
        if (bucket is null)
        {
            bucket = new RecoveryThrottleBucket(partition, now, now.Add(window));
            db.RecoveryThrottleBuckets.Add(bucket);
        }
        if (!bucket.TryTake(now, limit, window)) return false;
        try { await db.SaveChangesAsync(cancellationToken); return true; }
        catch (DbUpdateException)
        {
            // Concurrent insert/update losers fail closed; no unbounded retries or login lockout.
            db.Entry(bucket).State = EntityState.Detached;
            return false;
        }
    }
}
