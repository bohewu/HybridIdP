namespace Core.Domain.Entities;

public sealed class DirectorySettlementPreparation
{
    private DirectorySettlementPreparation()
    {
    }

    public DirectorySettlementPreparation(
        Guid attemptId,
        long expectedAttemptVersion,
        NativeDirectoryCredentialOperationKind operationKind,
        NativeDirectoryRecoveryStatus expectedAttemptStatus,
        Guid localAccountId,
        Guid directoryObjectId,
        Guid bindingId,
        Guid migrationId,
        long migrationVersion,
        Guid recoveryEmailId,
        long recoveryEmailVersion,
        string accountSecurityStamp,
        Guid operatorAccountId,
        string operatorSecurityStamp,
        DateTimeOffset authorizedAtUtc,
        DateTimeOffset authorizationExpiresAtUtc,
        DirectorySettlementDisposition disposition,
        DirectorySettlementEvidenceCategory evidenceCategory,
        string evidenceReference,
        string continuationHash,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (attemptId == Guid.Empty || expectedAttemptVersion < 1 || localAccountId == Guid.Empty ||
            directoryObjectId == Guid.Empty || bindingId == Guid.Empty || migrationId == Guid.Empty ||
            migrationVersion < 1 || recoveryEmailId == Guid.Empty || recoveryEmailVersion < 1 ||
            operatorAccountId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptId));
        }

        if (string.IsNullOrWhiteSpace(accountSecurityStamp) || string.IsNullOrWhiteSpace(operatorSecurityStamp) ||
            string.IsNullOrWhiteSpace(evidenceReference) || string.IsNullOrWhiteSpace(continuationHash))
        {
            throw new ArgumentException("A complete settlement preparation binding is required.");
        }

        if (authorizationExpiresAtUtc <= authorizedAtUtc || expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        }

        Id = Guid.NewGuid();
        AttemptId = attemptId;
        ExpectedAttemptVersion = expectedAttemptVersion;
        OperationKind = operationKind;
        ExpectedAttemptStatus = expectedAttemptStatus;
        LocalAccountId = localAccountId;
        DirectoryObjectId = directoryObjectId;
        BindingId = bindingId;
        MigrationId = migrationId;
        MigrationVersion = migrationVersion;
        RecoveryEmailId = recoveryEmailId;
        RecoveryEmailVersion = recoveryEmailVersion;
        AccountSecurityStamp = accountSecurityStamp;
        OperatorAccountId = operatorAccountId;
        OperatorSecurityStamp = operatorSecurityStamp;
        AuthorizedAtUtc = authorizedAtUtc;
        AuthorizationExpiresAtUtc = authorizationExpiresAtUtc;
        MfaAuthorized = true;
        UsersUpdateAuthorized = true;
        OriginalWritersDrained = true;
        Disposition = disposition;
        EvidenceCategory = evidenceCategory;
        EvidenceReference = evidenceReference;
        ContinuationHash = continuationHash;
        Status = DirectorySettlementPreparationStatus.Prepared;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid AttemptId { get; private set; }
    public long ExpectedAttemptVersion { get; private set; }
    public NativeDirectoryCredentialOperationKind OperationKind { get; private set; }
    public NativeDirectoryRecoveryStatus ExpectedAttemptStatus { get; private set; }
    public Guid LocalAccountId { get; private set; }
    public Guid DirectoryObjectId { get; private set; }
    public Guid BindingId { get; private set; }
    public Guid MigrationId { get; private set; }
    public long MigrationVersion { get; private set; }
    public Guid RecoveryEmailId { get; private set; }
    public long RecoveryEmailVersion { get; private set; }
    public string AccountSecurityStamp { get; private set; } = string.Empty;
    public Guid OperatorAccountId { get; private set; }
    public string OperatorSecurityStamp { get; private set; } = string.Empty;
    public DateTimeOffset AuthorizedAtUtc { get; private set; }
    public DateTimeOffset AuthorizationExpiresAtUtc { get; private set; }
    public bool MfaAuthorized { get; private set; }
    public bool UsersUpdateAuthorized { get; private set; }
    public bool OriginalWritersDrained { get; private set; }
    public DirectorySettlementDisposition Disposition { get; private set; }
    public DirectorySettlementEvidenceCategory EvidenceCategory { get; private set; }
    public string EvidenceReference { get; private set; } = string.Empty;
    public string ContinuationHash { get; private set; } = string.Empty;
    public DirectorySettlementPreparationStatus Status { get; private set; }
    public Guid? OwnershipChallengeId { get; private set; }
    public string? ContextHash { get; private set; }
    public string? CsrfHash { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ClaimedAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public long Version { get; private set; }

    public bool TryClaim(Guid ownershipChallengeId, string contextHash, string csrfHash, DateTimeOffset now)
    {
        if (Status != DirectorySettlementPreparationStatus.Prepared || ownershipChallengeId == Guid.Empty ||
            string.IsNullOrWhiteSpace(contextHash) || string.IsNullOrWhiteSpace(csrfHash) || ExpiresAtUtc <= now)
        {
            return false;
        }

        OwnershipChallengeId = ownershipChallengeId;
        ContextHash = contextHash;
        CsrfHash = csrfHash;
        ClaimedAtUtc = now;
        Status = DirectorySettlementPreparationStatus.OwnershipPending;
        Version++;
        return true;
    }

    public bool TryReserveVerification(DateTimeOffset now)
    {
        if (Status != DirectorySettlementPreparationStatus.OwnershipPending || ExpiresAtUtc <= now)
        {
            return false;
        }

        Status = DirectorySettlementPreparationStatus.Verifying;
        Version++;
        return true;
    }

    public bool TryConsume(DateTimeOffset now)
    {
        if (Status != DirectorySettlementPreparationStatus.Verifying || ExpiresAtUtc <= now)
        {
            return false;
        }

        Status = DirectorySettlementPreparationStatus.Consumed;
        ConsumedAtUtc = now;
        Version++;
        return true;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status is DirectorySettlementPreparationStatus.Prepared or
            DirectorySettlementPreparationStatus.OwnershipPending or
            DirectorySettlementPreparationStatus.Verifying)
        {
            Status = DirectorySettlementPreparationStatus.Cancelled;
            CancelledAtUtc = now;
            Version++;
        }
    }
}

public enum DirectorySettlementPreparationStatus
{
    Prepared,
    OwnershipPending,
    Verifying,
    Consumed,
    Cancelled
}

public enum DirectorySettlementDisposition
{
    OriginalOperationSettled,
    ApprovedOutOfBandRecoveryCompleted
}

public enum DirectorySettlementEvidenceCategory
{
    ApprovedDirectoryOperation,
    ApprovedRecoveryOperation
}
