namespace Core.Domain.Entities;

public sealed class LegacyPasswordSyncAttempt
{
    private LegacyPasswordSyncAttempt()
    {
    }

    public LegacyPasswordSyncAttempt(
        Guid operationId,
        LegacyPasswordSyncSourceKind sourceKind,
        Guid sourceAttemptId,
        long sourceAttemptVersion,
        LegacyPasswordSyncSourceCompletion sourceCompletion,
        DateTimeOffset sourceCreatedAtUtc,
        DateTimeOffset sourceCompletedAtUtc,
        Guid localAccountId,
        Guid providerSubjectDirectoryBindingId,
        Guid accountIdentity,
        string mappingVersion,
        long generation,
        DateTimeOffset createdAtUtc)
    {
        if (operationId == Guid.Empty || sourceAttemptId == Guid.Empty || localAccountId == Guid.Empty ||
            providerSubjectDirectoryBindingId == Guid.Empty || accountIdentity == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(operationId));
        }

        if (sourceAttemptVersion <= 0 || generation <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceAttemptVersion));
        }

        if (string.IsNullOrWhiteSpace(mappingVersion))
        {
            throw new ArgumentException("Mapping version is required.", nameof(mappingVersion));
        }

        OperationId = operationId;
        SourceKind = sourceKind;
        SourceAttemptId = sourceAttemptId;
        SourceAttemptVersion = sourceAttemptVersion;
        SourceCompletion = sourceCompletion;
        SourceCreatedAtUtc = sourceCreatedAtUtc;
        SourceCompletedAtUtc = sourceCompletedAtUtc;
        LocalAccountId = localAccountId;
        ProviderSubjectDirectoryBindingId = providerSubjectDirectoryBindingId;
        AccountIdentity = accountIdentity;
        MappingVersion = mappingVersion;
        Generation = generation;
        Status = LegacyPasswordSyncAttemptStatus.Reserved;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        Version = 1;
    }

    public Guid OperationId { get; private set; }
    public LegacyPasswordSyncSourceKind SourceKind { get; private set; }
    public Guid SourceAttemptId { get; private set; }
    public long SourceAttemptVersion { get; private set; }
    public LegacyPasswordSyncSourceCompletion SourceCompletion { get; private set; }
    public DateTimeOffset SourceCreatedAtUtc { get; private set; }
    public DateTimeOffset SourceCompletedAtUtc { get; private set; }
    public Guid LocalAccountId { get; private set; }
    public Guid ProviderSubjectDirectoryBindingId { get; private set; }
    public Guid AccountIdentity { get; private set; }
    public string MappingVersion { get; private set; } = string.Empty;
    public long Generation { get; private set; }
    public LegacyPasswordSyncAttemptStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? ClaimedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string? SanitizedOutcome { get; private set; }
    public long Version { get; private set; }
}

public enum LegacyPasswordSyncSourceKind
{
    NativeRecovery,
    RequiredChange,
    Stage2Migration
}

public enum LegacyPasswordSyncSourceCompletion
{
    DirectoryAttemptSucceeded,
    MigrationLocalFinalized,
    NativeRecoveryProofConsumed,
    RequiredChangeAuthorized,
    MigrationContinuationConsumed
}

public enum LegacyPasswordSyncAttemptStatus
{
    Reserved,
    Claimed,
    Superseded,
    Succeeded,
    Failed,
    Unknown
}
