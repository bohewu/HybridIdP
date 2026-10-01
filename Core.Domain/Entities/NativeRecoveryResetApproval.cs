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

    public static NativeRecoveryResetApproval CreateForDefault(RecoveryProofChallenge challenge,
        Guid actorAccountId, string reason, string identityCheckEvidence, DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (challenge.RecoveryEmailId is not null || challenge.Purpose != RecoveryProofPurpose.NativePasswordRecovery ||
            challenge.DestinationKind != RecoveryDestinationKind.TrustedDefault || challenge.SelectionEpoch is null ||
            challenge.NativeDirectoryAuthority is null || challenge.NativeRecoveryEmailVersion is null)
            throw new ArgumentException("A bound default recovery challenge is required.");
        var approval = new NativeRecoveryResetApproval(challenge.Id, challenge.LocalAccountId, Guid.NewGuid(),
            challenge.NativeRecoveryEmailVersion.Value, actorAccountId, challenge.NativeContextHash!,
            challenge.NativeCsrfHash!, challenge.NativeDirectoryAuthority.Value, challenge.NativeDirectoryObjectId,
            challenge.NativeSecurityStamp!, reason, identityCheckEvidence, createdAtUtc, expiresAtUtc);
        approval.RecoveryEmailId = null;
        approval.BindSelection(challenge.SelectionEpoch.Value, challenge.DestinationKind.Value,
            challenge.DestinationFingerprint!, challenge.DestinationVersion!.Value);
        return approval;
    }

    public Guid Id { get; private set; }
    public Guid RecoveryProofChallengeId { get; private set; }
    public Guid LocalAccountId { get; private set; }
    public Guid? RecoveryEmailId { get; private set; }
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
    public long? SelectionEpoch { get; private set; }
    public RecoveryDestinationKind? DestinationKind { get; private set; }
    public string? DestinationFingerprint { get; private set; }
    public long? DestinationVersion { get; private set; }

    public void BindSelection(long selectionEpoch, RecoveryDestinationKind destinationKind,
        string destinationFingerprint, long destinationVersion)
    {
        RecoveryStateGuard.Selection(selectionEpoch, destinationKind, destinationFingerprint, destinationVersion);
        if (SelectionEpoch is not null) throw new InvalidOperationException("Recovery selection is immutable once bound.");
        SelectionEpoch = selectionEpoch;
        DestinationKind = destinationKind;
        DestinationFingerprint = destinationFingerprint;
        DestinationVersion = destinationVersion;
        Version++;
    }

    public bool MatchesSelection(long epoch, RecoveryDestinationKind kind, string fingerprint, long version) =>
        SelectionEpoch == epoch && DestinationKind == kind && DestinationFingerprint == fingerprint && DestinationVersion == version;


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
