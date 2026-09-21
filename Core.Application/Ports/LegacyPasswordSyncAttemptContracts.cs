using Core.Domain.Entities;

namespace Core.Application.Ports;

public interface ILegacyPasswordSyncAttemptStore
{
    Task<LegacyPasswordSyncReservationResult> ReserveAsync(
        LegacyPasswordSyncSourceReference source,
        LegacyPasswordSyncAttemptIdentity identity,
        CancellationToken cancellationToken = default);

    Task<LegacyPasswordSyncClaimResult> ClaimAsync(
        Guid operationId,
        long expectedVersion,
        LegacyPasswordSyncSourceReference source,
        LegacyPasswordSyncAttemptIdentity identity,
        CancellationToken cancellationToken = default);

    Task<bool> RecordResultAsync(
        Guid operationId,
        long expectedVersion,
        LegacyPasswordSyncTerminalStatus status,
        string sanitizedOutcome,
        CancellationToken cancellationToken = default);

    Task<LegacyPasswordSyncAttemptRecord?> FindAsync(
        Guid operationId,
        CancellationToken cancellationToken = default);
}

public sealed record LegacyPasswordSyncSourceReference(
    LegacyPasswordSyncSourceKind Kind,
    Guid AttemptId,
    long Version);

public sealed record LegacyPasswordSyncAttemptIdentity(
    Guid LocalAccountId,
    LegacyPasswordSyncBindingReference Binding,
    Guid AccountIdentity,
    string MappingVersion);

public sealed record LegacyPasswordSyncBindingReference(
    Guid Id,
    string ProviderNamespace,
    string StableSubject,
    Guid DirectoryObjectId);

public sealed record LegacyPasswordSyncAttemptRecord(
    Guid OperationId,
    LegacyPasswordSyncSourceReference Source,
    LegacyPasswordSyncSourceCompletion SourceCompletion,
    DateTimeOffset SourceCreatedAtUtc,
    DateTimeOffset SourceCompletedAtUtc,
    LegacyPasswordSyncAttemptIdentity Identity,
    long Generation,
    LegacyPasswordSyncAttemptStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? SanitizedOutcome,
    long Version);

public sealed record LegacyPasswordSyncReservationResult(
    LegacyPasswordSyncReservationOutcome Outcome,
    LegacyPasswordSyncAttemptRecord? Attempt = null);

public sealed record LegacyPasswordSyncClaimResult(
    LegacyPasswordSyncClaimOutcome Outcome,
    LegacyPasswordSyncAttemptRecord? Attempt = null);

public enum LegacyPasswordSyncReservationOutcome
{
    Reserved,
    DuplicateSource,
    StaleSource,
    SourceIneligible,
    Contended
}

public enum LegacyPasswordSyncClaimOutcome
{
    Claimed,
    Blocked,
    Stale,
    SourceIneligible,
    Contended
}

public enum LegacyPasswordSyncTerminalStatus
{
    Succeeded,
    Failed,
    Unknown
}
