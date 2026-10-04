namespace Core.Domain.Entities;

/// <summary>
/// A bounded destination-verification or migration proof challenge. Only the code hash is stored.
/// </summary>
public sealed class RecoveryProofChallenge
{
    private RecoveryProofChallenge()
    {
    }

    public RecoveryProofChallenge(
        Guid recoveryEmailId,
        Guid localAccountId,
        RecoveryProofPurpose purpose,
        string codeHash,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc,
        Guid? credentialMigrationContinuationId = null)
    {
        if (recoveryEmailId == Guid.Empty || localAccountId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(localAccountId));
        }

        if (string.IsNullOrWhiteSpace(codeHash))
        {
            throw new ArgumentException("A code hash is required.", nameof(codeHash));
        }

        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        }

        Id = Guid.NewGuid();
        RecoveryEmailId = recoveryEmailId;
        LocalAccountId = localAccountId;
        CredentialMigrationContinuationId = credentialMigrationContinuationId;
        Purpose = purpose;
        CodeHash = codeHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        SentAtUtc = createdAtUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid RecoveryEmailId { get; private set; }
    public Guid LocalAccountId { get; private set; }
    public Guid? CredentialMigrationContinuationId { get; private set; }
    public RecoveryProofPurpose Purpose { get; private set; }
    public string CodeHash { get; private set; } = string.Empty;
    public string? ProofTokenHash { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset SentAtUtc { get; private set; }
    public int VerificationAttempts { get; private set; }
    public DateTimeOffset? VerifiedAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public long Version { get; private set; }
    public Guid? NativeRecoveryChallengeId { get; private set; }
    public string? NativeContextHash { get; private set; }
    public string? NativeCsrfHash { get; private set; }
    public bool? NativeDirectoryAuthority { get; private set; }
    public Guid? NativeDirectoryObjectId { get; private set; }
    public long? NativeRecoveryEmailVersion { get; private set; }
    public string? NativeSecurityStamp { get; private set; }

    public void BindNativeAssistance(
        string contextHash,
        string csrfHash,
        bool directoryAuthority,
        Guid? directoryObjectId,
        long recoveryEmailVersion,
        string securityStamp,
        Guid? nativeRecoveryChallengeId = null)
    {
        if (string.IsNullOrWhiteSpace(contextHash) || string.IsNullOrWhiteSpace(csrfHash) ||
            string.IsNullOrWhiteSpace(securityStamp) || recoveryEmailVersion < 1 ||
            directoryAuthority != directoryObjectId.HasValue || directoryObjectId == Guid.Empty)
        {
            throw new ArgumentException("A complete native recovery assistance binding is required.");
        }

        NativeContextHash = contextHash;
        NativeCsrfHash = csrfHash;
        NativeDirectoryAuthority = directoryAuthority;
        NativeDirectoryObjectId = directoryObjectId;
        NativeRecoveryEmailVersion = recoveryEmailVersion;
        NativeSecurityStamp = securityStamp;
        NativeRecoveryChallengeId = nativeRecoveryChallengeId;
        Version++;
    }

    public bool TrySupersedeCode(string codeHash, DateTimeOffset sentAtUtc)
    {
        if (Purpose != RecoveryProofPurpose.NativePasswordRecovery ||
            string.IsNullOrWhiteSpace(codeHash) || VerifiedAtUtc is not null ||
            ConsumedAtUtc is not null || RevokedAtUtc is not null || ExpiresAtUtc <= sentAtUtc)
        {
            return false;
        }

        CodeHash = codeHash;
        SentAtUtc = sentAtUtc;
        VerificationAttempts = 0;
        Version++;
        return true;
    }

    public bool TryConsumeWithAdministrativeApproval(DateTimeOffset now)
    {
        if (Purpose != RecoveryProofPurpose.NativePasswordRecovery || VerifiedAtUtc is not null ||
            ConsumedAtUtc is not null || RevokedAtUtc is not null || ExpiresAtUtc <= now)
        {
            return false;
        }

        VerifiedAtUtc = now;
        ConsumedAtUtc = now;
        Version++;
        return true;
    }

    public bool TryReserveAttempt(DateTimeOffset now, int maxAttempts)
    {
        if (RevokedAtUtc is not null || ConsumedAtUtc is not null || VerifiedAtUtc is not null ||
            ExpiresAtUtc <= now || VerificationAttempts >= maxAttempts)
        {
            return false;
        }

        VerificationAttempts++;
        Version++;
        return true;
    }

    public bool TryMarkAddressVerified(DateTimeOffset now)
    {
        if (RevokedAtUtc is not null || ConsumedAtUtc is not null || VerifiedAtUtc is not null || ExpiresAtUtc <= now)
        {
            return false;
        }

        VerifiedAtUtc = now;
        ConsumedAtUtc = now;
        Version++;
        return true;
    }

    public bool TryMarkMigrationVerified(string proofTokenHash, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(proofTokenHash) || RevokedAtUtc is not null || ConsumedAtUtc is not null ||
            VerifiedAtUtc is not null || ExpiresAtUtc <= now)
        {
            return false;
        }

        ProofTokenHash = proofTokenHash;
        VerifiedAtUtc = now;
        Version++;
        return true;
    }

    public bool TryMarkNativeRecoveryVerified(string proofTokenHash, DateTimeOffset now)
    {
        if (Purpose != RecoveryProofPurpose.NativePasswordRecovery ||
            string.IsNullOrWhiteSpace(proofTokenHash) || RevokedAtUtc is not null || ConsumedAtUtc is not null ||
            VerifiedAtUtc is not null || ExpiresAtUtc <= now)
        {
            return false;
        }

        ProofTokenHash = proofTokenHash;
        VerifiedAtUtc = now;
        Version++;
        return true;
    }

    public bool TryConsumePendingSettlement(DateTimeOffset now)
    {
        if (Purpose != RecoveryProofPurpose.PendingDirectorySettlement ||
            VerifiedAtUtc is not null || ConsumedAtUtc is not null || RevokedAtUtc is not null || ExpiresAtUtc <= now)
        {
            return false;
        }

        VerifiedAtUtc = now;
        ConsumedAtUtc = now;
        Version++;
        return true;
    }

    public bool TryConsume(DateTimeOffset now)
    {
        if (VerifiedAtUtc is null || ConsumedAtUtc is not null || RevokedAtUtc is not null || ExpiresAtUtc <= now)
        {
            return false;
        }

        ConsumedAtUtc = now;
        Version++;
        return true;
    }

    public void Revoke(DateTimeOffset now)
    {
        if (RevokedAtUtc is null && ConsumedAtUtc is null)
        {
            RevokedAtUtc = now;
            Version++;
        }
    }
}

public enum RecoveryProofPurpose
{
    RecoveryAddressVerification,
    MigrationOtp,
    NativePasswordRecovery,
    PendingDirectorySettlement
}
