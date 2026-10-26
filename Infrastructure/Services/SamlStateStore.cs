using System.Security.Cryptography;
using System.Text;
using Core.Application.Interfaces;
using Core.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

/// <summary>
/// Uses the shared application database, unique keys and conditional DELETE, not a cache read/remove.
/// All nodes must share the database and Data Protection application name/key ring.
/// </summary>
public sealed class SamlStateStore(
    DbContextOptions<ApplicationDbContext> databaseOptions,
    IDataProtectionProvider protectionProvider,
    TimeProvider timeProvider) : ISamlStateStore
{
    private const int MaximumPayloadBytes = 32 * 1024;

    public async Task<bool> TryAddAsync(SamlStatePurpose purpose, string key, string payload,
        DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default)
    {
        var keyHash = GetKeyHash(purpose, key);
        ArgumentNullException.ThrowIfNull(payload);
        if (Encoding.UTF8.GetByteCount(payload) > MaximumPayloadBytes)
            throw new ArgumentException("SAML state payload exceeds the size limit.", nameof(payload));
        var now = timeProvider.GetUtcNow();
        if (expiresAtUtc <= now || expiresAtUtc > now.Add(SamlProfile.MaximumStateLifetime))
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));

        // Own context prevents unrelated tracked user/link changes from being committed here.
        await using var database = new ApplicationDbContext(databaseOptions);
        var states = database.Set<SamlStateRecord>();
        await states.Where(state => state.ExpiresAtUtc <= now.UtcDateTime)
            .ExecuteDeleteAsync(cancellationToken);
        states.Add(new SamlStateRecord
        {
            KeyHash = keyHash,
            ProtectedPayload = Protector(keyHash).Protect(payload),
            ExpiresAtUtc = expiresAtUtc.UtcDateTime
        });
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Only a confirmed existing key is a collision. Infrastructure failures stay fail-closed.
            if (await states.AsNoTracking().AnyAsync(state => state.KeyHash == keyHash, cancellationToken))
                return false;
            throw;
        }
    }

    public async Task<string?> TryTakeAsync(SamlStatePurpose purpose, string key,
        CancellationToken cancellationToken = default)
    {
        if (purpose is not (SamlStatePurpose.IdpContinuation or SamlStatePurpose.SpCorrelation))
            throw new ArgumentException("Replay markers cannot be consumed.", nameof(purpose));
        var keyHash = GetKeyHash(purpose, key);
        await using var database = new ApplicationDbContext(databaseOptions);
        var states = database.Set<SamlStateRecord>();
        var candidate = await states.AsNoTracking()
            .SingleOrDefaultAsync(state => state.KeyHash == keyHash, cancellationToken);
        if (candidate is null) return null;

        // Recheck expiry at the winning write, not merely at the earlier read.
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var removed = await states.Where(state => state.KeyHash == keyHash && state.ExpiresAtUtc > now &&
                state.ProtectedPayload == candidate.ProtectedPayload)
            .ExecuteDeleteAsync(cancellationToken);
        if (removed != 1) return null;
        try
        {
            return Protector(keyHash).Unprotect(candidate.ProtectedPayload);
        }
        catch (CryptographicException)
        {
            // Corrupt/wrong-key-ring state is consumed but cannot authorize a ceremony.
            return null;
        }
    }

    private IDataProtector Protector(string keyHash) => protectionProvider
        .CreateProtector("HybridIdP.SamlState.v1", keyHash);

    private static string GetKeyHash(SamlStatePurpose purpose, string key)
    {
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        if (string.IsNullOrWhiteSpace(key) || key.Length > 2048)
            throw new ArgumentException("A bounded SAML state key is required.", nameof(key));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{(int)purpose}:{key}")));
    }
}
