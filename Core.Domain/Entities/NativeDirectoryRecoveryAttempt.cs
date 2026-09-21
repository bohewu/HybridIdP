namespace Core.Domain.Entities;

public sealed class NativeDirectoryRecoveryAttempt
{
    private NativeDirectoryRecoveryAttempt()
    {
    }

    public NativeDirectoryRecoveryAttempt(
        Guid recoveryProofChallengeId,
        Guid localAccountId,
        Guid directoryObjectId,
        DateTimeOffset createdAtUtc)
    {
        if (recoveryProofChallengeId == Guid.Empty || localAccountId == Guid.Empty || directoryObjectId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(recoveryProofChallengeId));
        }

        Id = Guid.NewGuid();
        RecoveryProofChallengeId = recoveryProofChallengeId;
        LocalAccountId = localAccountId;
        DirectoryObjectId = directoryObjectId;
        OperationKind = NativeDirectoryCredentialOperationKind.NativeReset;
        Status = NativeDirectoryRecoveryStatus.Reserved;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        Version = 1;
    }

    public NativeDirectoryRecoveryAttempt(
        Guid localAccountId,
        Guid directoryObjectId,
        NativeDirectoryCredentialOperationKind operationKind,
        DateTimeOffset createdAtUtc)
    {
        if (localAccountId == Guid.Empty || directoryObjectId == Guid.Empty ||
            operationKind == NativeDirectoryCredentialOperationKind.NativeReset)
        {
            throw new ArgumentOutOfRangeException(nameof(localAccountId));
        }

        Id = Guid.NewGuid();
        LocalAccountId = localAccountId;
        DirectoryObjectId = directoryObjectId;
        OperationKind = operationKind;
        Status = NativeDirectoryRecoveryStatus.Reserved;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid? RecoveryProofChallengeId { get; private set; }
    public Guid LocalAccountId { get; private set; }
    public Guid DirectoryObjectId { get; private set; }
    public NativeDirectoryCredentialOperationKind OperationKind { get; private set; }
    public NativeDirectoryRecoveryStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public long Version { get; private set; }

    public void ReserveSettlementPreparation(long expectedVersion, DateTimeOffset updatedAtUtc)
    {
        if (Status is not (NativeDirectoryRecoveryStatus.Reserved or NativeDirectoryRecoveryStatus.ReconciliationRequired) ||
            Version != expectedVersion)
        {
            throw new InvalidOperationException("The pending directory recovery attempt changed.");
        }

        UpdatedAtUtc = updatedAtUtc;
        Version++;
    }

    public void Complete(NativeDirectoryRecoveryStatus status, DateTimeOffset updatedAtUtc)
    {
        if (Status is not (NativeDirectoryRecoveryStatus.Reserved or NativeDirectoryRecoveryStatus.ReconciliationRequired) ||
            status is NativeDirectoryRecoveryStatus.Reserved)
        {
            throw new InvalidOperationException("Only a reserved directory recovery can be completed.");
        }

        Status = status;
        UpdatedAtUtc = updatedAtUtc;
        Version++;
    }
}

public enum NativeDirectoryCredentialOperationKind
{
    NativeReset,
    AdminTemporaryIssue,
    RequiredChange
}

public enum NativeDirectoryRecoveryStatus
{
    Reserved,
    Succeeded,
    LegacyPasswordSyncAuthorized,
    Denied,
    ReconciliationRequired,
    OperatorResolved
}
