namespace Core.Domain.Entities;

/// <summary>
/// A reasoned, identity-checked approval for one exact native recovery challenge.
/// </summary>
public sealed class NativeRecoveryResetApproval
{
    private NativeRecoveryResetApproval()
    {
    }

    public NativeRecoveryResetApproval(
        Guid recoveryProofChallengeId,
        Guid localAccountId,
        Guid recoveryEmailId,
        long recoveryEmailVersion,
        Guid actorAccountId,
        string contextHash,
        string csrfHash,
        bool directoryAuthority,
        Guid? directoryObjectId,
        string securityStamp,
        string reason,
        string identityCheckEvidence,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (recoveryProofChallengeId == Guid.Empty || localAccountId == Guid.Empty ||
            recoveryEmailId == Guid.Empty || actorAccountId == Guid.Empty || recoveryEmailVersion < 1 ||
            directoryAuthority != directoryObjectId.HasValue || directoryObjectId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(localAccountId));
        }

        if (string.IsNullOrWhiteSpace(contextHash) || string.IsNullOrWhiteSpace(csrfHash) ||
            string.IsNullOrWhiteSpace(securityStamp) || string.IsNullOrWhiteSpace(reason) ||
            string.IsNullOrWhiteSpace(identityCheckEvidence))
        {
            throw new ArgumentException("A complete approval binding, reason, and identity-check evidence are required.");
        }

        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        }

        Id = Guid.NewGuid();
        RecoveryProofChallengeId = recoveryProofChallengeId;
        LocalAccountId = localAccountId;
        RecoveryEmailId = recoveryEmailId;
        RecoveryEmailVersion = recoveryEmailVersion;
        ActorAccountId = actorAccountId;
        ContextHash = contextHash;
        CsrfHash = csrfHash;
        DirectoryAuthority = directoryAuthority;
        DirectoryObjectId = directoryObjectId;
        SecurityStamp = securityStamp;
        Reason = reason;
        IdentityCheckEvidence = identityCheckEvidence;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid RecoveryProofChallengeId { get; private set; }
    public Guid LocalAccountId { get; private set; }
    public Guid RecoveryEmailId { get; private set; }
    public long RecoveryEmailVersion { get; private set; }
    public Guid ActorAccountId { get; private set; }
    public string ContextHash { get; private set; } = string.Empty;
    public string CsrfHash { get; private set; } = string.Empty;
    public bool DirectoryAuthority { get; private set; }
    public Guid? DirectoryObjectId { get; private set; }
    public string SecurityStamp { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public string IdentityCheckEvidence { get; private set; } = string.Empty;
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
