namespace Core.Domain.Entities;

/// <summary>
/// A reasoned, identity-checked approval for exactly one migration continuation.
/// </summary>
public sealed class RecoveryResetApproval
{
    private RecoveryResetApproval()
    {
    }

    public RecoveryResetApproval(
        Guid localAccountId,
        Guid credentialMigrationContinuationId,
        Guid actorAccountId,
        string reason,
        string identityCheckEvidence,
        string tokenHash,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (localAccountId == Guid.Empty || credentialMigrationContinuationId == Guid.Empty || actorAccountId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(localAccountId));
        }

        if (string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(identityCheckEvidence))
        {
            throw new ArgumentException("A reason and identity-check evidence are required.");
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("A token hash is required.", nameof(tokenHash));
        }

        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        }

        Id = Guid.NewGuid();
        LocalAccountId = localAccountId;
        CredentialMigrationContinuationId = credentialMigrationContinuationId;
        ActorAccountId = actorAccountId;
        Reason = reason;
        IdentityCheckEvidence = identityCheckEvidence;
        TokenHash = tokenHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid LocalAccountId { get; private set; }
    public Guid CredentialMigrationContinuationId { get; private set; }
    public Guid ActorAccountId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string IdentityCheckEvidence { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public long Version { get; private set; }

    public bool TryConsume(DateTimeOffset now)
    {
        if (ConsumedAtUtc is not null || RevokedAtUtc is not null || ExpiresAtUtc <= now)
        {
            return false;
        }

        ConsumedAtUtc = now;
        Version++;
        return true;
    }

    public void Revoke(DateTimeOffset now)
    {
        if (ConsumedAtUtc is null && RevokedAtUtc is null)
        {
            RevokedAtUtc = now;
            Version++;
        }
    }
}
