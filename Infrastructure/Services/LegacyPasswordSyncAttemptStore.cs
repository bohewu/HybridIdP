using System.Data;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

public sealed class LegacyPasswordSyncAttemptStore(
    ApplicationDbContext dbContext,
    ILegacyPasswordSyncTargetResolver targetResolver,
    TimeProvider? timeProvider = null) : ILegacyPasswordSyncAttemptStore
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<LegacyPasswordSyncReservationResult> ReserveAsync(
        LegacyPasswordSyncSourceReference source,
        LegacyPasswordSyncAttemptIdentity identity,
        CancellationToken cancellationToken = default)
    {
        if (!IsValid(source, identity))
        {
            return new(LegacyPasswordSyncReservationOutcome.SourceIneligible);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var binding = await LoadExactBindingAsync(identity, cancellationToken);
        if (binding is null)
        {
            return new(LegacyPasswordSyncReservationOutcome.SourceIneligible);
        }
        if (!IsExactEnabledMapping(identity))
        {
            return new(LegacyPasswordSyncReservationOutcome.SourceIneligible);
        }

        var duplicate = await dbContext.LegacyPasswordSyncAttempts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                attempt => attempt.SourceKind == source.Kind && attempt.SourceAttemptId == source.AttemptId,
                cancellationToken);
        if (duplicate is not null)
        {
            return Matches(duplicate, source, identity)
                ? new(LegacyPasswordSyncReservationOutcome.DuplicateSource, ToRecord(duplicate, binding))
                : new(LegacyPasswordSyncReservationOutcome.SourceIneligible);
        }

        var sourceMarker = await LoadEligibleSourceAsync(source, identity, cancellationToken);
        if (sourceMarker is null)
        {
            return new(LegacyPasswordSyncReservationOutcome.SourceIneligible);
        }

        if (await HasNewerOrAmbiguousSourceAsync(sourceMarker, identity, cancellationToken))
        {
            return new(LegacyPasswordSyncReservationOutcome.StaleSource);
        }

        var latest = await dbContext.LegacyPasswordSyncAttempts
            .AsNoTracking()
            .Where(attempt =>
                attempt.LocalAccountId == identity.LocalAccountId &&
                attempt.AccountIdentity == identity.AccountIdentity)
            .OrderByDescending(attempt => attempt.Generation)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is not null &&
            (latest.SourceCompletedAtUtc > sourceMarker.CompletedAtUtc ||
             (latest.SourceCompletedAtUtc == sourceMarker.CompletedAtUtc &&
              (latest.SourceKind != source.Kind || latest.SourceAttemptId != source.AttemptId))))
        {
            return new(LegacyPasswordSyncReservationOutcome.StaleSource);
        }

        var now = _timeProvider.GetUtcNow();
        await dbContext.LegacyPasswordSyncAttempts
            .Where(attempt =>
                attempt.LocalAccountId == identity.LocalAccountId &&
                attempt.AccountIdentity == identity.AccountIdentity &&
                attempt.Status == LegacyPasswordSyncAttemptStatus.Reserved)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(attempt => attempt.Status, LegacyPasswordSyncAttemptStatus.Superseded)
                    .SetProperty(attempt => attempt.UpdatedAtUtc, now)
                    .SetProperty(attempt => attempt.Version, attempt => attempt.Version + 1),
                cancellationToken);

        var attempt = new LegacyPasswordSyncAttempt(
            Guid.NewGuid(),
            source.Kind,
            source.AttemptId,
            source.Version,
            sourceMarker.Completion,
            sourceMarker.CreatedAtUtc,
            sourceMarker.CompletedAtUtc,
            identity.LocalAccountId,
            identity.Binding.Id,
            identity.AccountIdentity,
            identity.MappingVersion,
            (latest?.Generation ?? 0) + 1,
            now);
        dbContext.LegacyPasswordSyncAttempts.Add(attempt);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(LegacyPasswordSyncReservationOutcome.Reserved, ToRecord(attempt, binding));
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            var concurrent = await dbContext.LegacyPasswordSyncAttempts
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.SourceKind == source.Kind && candidate.SourceAttemptId == source.AttemptId,
                    cancellationToken);
            return concurrent is not null && Matches(concurrent, source, identity)
                ? new(LegacyPasswordSyncReservationOutcome.DuplicateSource, await ToRecordAsync(concurrent, cancellationToken))
                : new(LegacyPasswordSyncReservationOutcome.Contended);
        }
    }

    public async Task<LegacyPasswordSyncClaimResult> ClaimAsync(
        Guid operationId,
        long expectedVersion,
        LegacyPasswordSyncSourceReference source,
        LegacyPasswordSyncAttemptIdentity identity,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty || expectedVersion <= 0 || !IsValid(source, identity))
        {
            return new(LegacyPasswordSyncClaimOutcome.SourceIneligible);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var attempt = await dbContext.LegacyPasswordSyncAttempts
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.OperationId == operationId, cancellationToken);
        if (attempt is null || attempt.Status != LegacyPasswordSyncAttemptStatus.Reserved ||
            attempt.Version != expectedVersion || !Matches(attempt, source, identity))
        {
            return new(LegacyPasswordSyncClaimOutcome.Stale);
        }

        if (await LoadExactBindingAsync(identity, cancellationToken) is null)
        {
            return new(LegacyPasswordSyncClaimOutcome.SourceIneligible);
        }
        if (!IsExactEnabledMapping(identity))
        {
            return new(LegacyPasswordSyncClaimOutcome.SourceIneligible);
        }

        var sourceMarker = await LoadEligibleSourceAsync(source, identity, cancellationToken);
        if (sourceMarker is null || sourceMarker.Completion != attempt.SourceCompletion ||
            sourceMarker.CreatedAtUtc != attempt.SourceCreatedAtUtc ||
            sourceMarker.CompletedAtUtc != attempt.SourceCompletedAtUtc)
        {
            return new(LegacyPasswordSyncClaimOutcome.SourceIneligible);
        }

        if (await HasNewerOrAmbiguousSourceAsync(sourceMarker, identity, cancellationToken))
        {
            return new(LegacyPasswordSyncClaimOutcome.Stale);
        }

        var latestOperationId = await dbContext.LegacyPasswordSyncAttempts
            .Where(candidate =>
                candidate.LocalAccountId == identity.LocalAccountId &&
                candidate.AccountIdentity == identity.AccountIdentity)
            .OrderByDescending(candidate => candidate.Generation)
            .Select(candidate => candidate.OperationId)
            .FirstAsync(cancellationToken);
        if (latestOperationId != operationId)
        {
            return new(LegacyPasswordSyncClaimOutcome.Stale);
        }

        var hasBarrier = await dbContext.LegacyPasswordSyncAttempts.AnyAsync(
            candidate =>
                candidate.LocalAccountId == identity.LocalAccountId &&
                candidate.AccountIdentity == identity.AccountIdentity &&
                candidate.OperationId != operationId &&
                (candidate.Status == LegacyPasswordSyncAttemptStatus.Claimed ||
                 candidate.Status == LegacyPasswordSyncAttemptStatus.Unknown),
            cancellationToken);
        if (hasBarrier)
        {
            return new(LegacyPasswordSyncClaimOutcome.Blocked);
        }

        var now = _timeProvider.GetUtcNow();
        var updated = await dbContext.LegacyPasswordSyncAttempts
            .Where(candidate =>
                candidate.OperationId == operationId &&
                candidate.Status == LegacyPasswordSyncAttemptStatus.Reserved &&
                candidate.Version == expectedVersion)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.Status, LegacyPasswordSyncAttemptStatus.Claimed)
                    .SetProperty(candidate => candidate.ClaimedAtUtc, now)
                    .SetProperty(candidate => candidate.UpdatedAtUtc, now)
                    .SetProperty(candidate => candidate.Version, candidate => candidate.Version + 1),
                cancellationToken);
        if (updated != 1)
        {
            return new(LegacyPasswordSyncClaimOutcome.Contended);
        }

        await transaction.CommitAsync(cancellationToken);
        return new(
            LegacyPasswordSyncClaimOutcome.Claimed,
            await FindAsync(operationId, cancellationToken));
    }

    public async Task<bool> RecordResultAsync(
        Guid operationId,
        long expectedVersion,
        LegacyPasswordSyncTerminalStatus status,
        string sanitizedOutcome,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty || expectedVersion <= 0 || string.IsNullOrWhiteSpace(sanitizedOutcome) ||
            sanitizedOutcome.Length > 200 || sanitizedOutcome.IndexOfAny(['\r', '\n']) >= 0)
        {
            return false;
        }

        var persistedStatus = status switch
        {
            LegacyPasswordSyncTerminalStatus.Succeeded => LegacyPasswordSyncAttemptStatus.Succeeded,
            LegacyPasswordSyncTerminalStatus.Failed => LegacyPasswordSyncAttemptStatus.Failed,
            LegacyPasswordSyncTerminalStatus.Unknown => LegacyPasswordSyncAttemptStatus.Unknown,
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
        var now = _timeProvider.GetUtcNow();
        var updated = await dbContext.LegacyPasswordSyncAttempts
            .Where(attempt =>
                attempt.OperationId == operationId &&
                attempt.Status == LegacyPasswordSyncAttemptStatus.Claimed &&
                attempt.Version == expectedVersion)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(attempt => attempt.Status, persistedStatus)
                    .SetProperty(attempt => attempt.SanitizedOutcome, sanitizedOutcome.Trim())
                    .SetProperty(attempt => attempt.CompletedAtUtc, now)
                    .SetProperty(attempt => attempt.UpdatedAtUtc, now)
                    .SetProperty(attempt => attempt.Version, attempt => attempt.Version + 1),
                cancellationToken);
        return updated == 1;
    }

    public async Task<LegacyPasswordSyncAttemptRecord?> FindAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty)
        {
            return null;
        }

        var attempt = await dbContext.LegacyPasswordSyncAttempts
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.OperationId == operationId, cancellationToken);
        return attempt is null ? null : await ToRecordAsync(attempt, cancellationToken);
    }

    private async Task<SourceMarker?> LoadEligibleSourceAsync(
        LegacyPasswordSyncSourceReference source,
        LegacyPasswordSyncAttemptIdentity identity,
        CancellationToken cancellationToken)
    {
        var binding = await LoadExactBindingAsync(identity, cancellationToken);
        if (binding is null)
        {
            return null;
        }

        if (source.Kind == LegacyPasswordSyncSourceKind.Stage2Migration)
        {
            var migration = await dbContext.CredentialMigrationStateRecords
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == source.AttemptId, cancellationToken);
            if (migration is not null)
            {
                return migration.LocalAccountId == identity.LocalAccountId &&
                       migration.ProviderSubjectDirectoryBindingId == identity.Binding.Id &&
                       migration.State == CredentialMigrationState.LocalFinalized &&
                       migration.Version == source.Version
                    ? new(
                        source.Kind,
                        source.AttemptId,
                        LegacyPasswordSyncSourceCompletion.MigrationLocalFinalized,
                        migration.CreatedAtUtc,
                        migration.UpdatedAtUtc,
                        MigrationStateId: migration.Id)
                    : null;
            }

            var continuation = await (
                from candidate in dbContext.CredentialMigrationContinuations.AsNoTracking()
                join state in dbContext.CredentialMigrationStateRecords.AsNoTracking()
                    on candidate.CredentialMigrationStateRecordId equals state.Id
                where candidate.Id == source.AttemptId
                select new { Continuation = candidate, State = state })
                .SingleOrDefaultAsync(cancellationToken);
            return continuation is not null &&
                   continuation.Continuation.ConsumedAtUtc is not null &&
                   continuation.Continuation.Version == source.Version &&
                   continuation.State.LocalAccountId == identity.LocalAccountId &&
                   continuation.State.ProviderSubjectDirectoryBindingId == identity.Binding.Id &&
                   continuation.State.State == CredentialMigrationState.ProofValidated
                ? new(
                    source.Kind,
                    source.AttemptId,
                    LegacyPasswordSyncSourceCompletion.MigrationContinuationConsumed,
                    continuation.Continuation.CreatedAtUtc,
                    continuation.Continuation.ConsumedAtUtc.Value,
                    MigrationStateId: continuation.State.Id)
                : null;
        }

        if (source.Kind == LegacyPasswordSyncSourceKind.NativeRecovery)
        {
            var challenge = await dbContext.RecoveryProofChallenges
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == source.AttemptId, cancellationToken);
            if (challenge is not null)
            {
                return challenge.LocalAccountId == identity.LocalAccountId &&
                       challenge.Purpose == RecoveryProofPurpose.NativePasswordRecovery &&
                       challenge.VerifiedAtUtc is not null &&
                       challenge.ConsumedAtUtc is not null &&
                       challenge.RevokedAtUtc is null &&
                       challenge.NativeDirectoryAuthority == true &&
                       challenge.NativeDirectoryObjectId == binding.DirectoryObjectId &&
                       challenge.Version == source.Version
                    ? new(
                        source.Kind,
                        source.AttemptId,
                        LegacyPasswordSyncSourceCompletion.NativeRecoveryProofConsumed,
                        challenge.CreatedAtUtc,
                        challenge.ConsumedAtUtc.Value,
                        RecoveryProofChallengeId: challenge.Id)
                    : null;
            }
        }

        var expectedKind = source.Kind switch
        {
            LegacyPasswordSyncSourceKind.NativeRecovery => NativeDirectoryCredentialOperationKind.NativeReset,
            LegacyPasswordSyncSourceKind.RequiredChange => NativeDirectoryCredentialOperationKind.RequiredChange,
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
        var directoryAttempt = await dbContext.NativeDirectoryRecoveryAttempts
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == source.AttemptId, cancellationToken);
        if (directoryAttempt is null ||
            directoryAttempt.LocalAccountId != identity.LocalAccountId ||
            directoryAttempt.DirectoryObjectId != binding.DirectoryObjectId ||
            directoryAttempt.OperationKind != expectedKind ||
            directoryAttempt.Version != source.Version)
        {
            return null;
        }

        var completion = directoryAttempt.Status switch
        {
            NativeDirectoryRecoveryStatus.Succeeded =>
                LegacyPasswordSyncSourceCompletion.DirectoryAttemptSucceeded,
            NativeDirectoryRecoveryStatus.LegacyPasswordSyncAuthorized
                when source.Kind == LegacyPasswordSyncSourceKind.RequiredChange =>
                LegacyPasswordSyncSourceCompletion.RequiredChangeAuthorized,
            _ => (LegacyPasswordSyncSourceCompletion?)null
        };
        return completion is null
            ? null
            : new(
                source.Kind,
                source.AttemptId,
                completion.Value,
                directoryAttempt.CreatedAtUtc,
                directoryAttempt.UpdatedAtUtc,
                RecoveryProofChallengeId: directoryAttempt.RecoveryProofChallengeId);
    }

    private async Task<bool> HasNewerOrAmbiguousSourceAsync(
        SourceMarker source,
        LegacyPasswordSyncAttemptIdentity identity,
        CancellationToken cancellationToken)
    {
        var directorySources = await dbContext.NativeDirectoryRecoveryAttempts
            .AsNoTracking()
            .Where(candidate => candidate.LocalAccountId == identity.LocalAccountId)
            .Select(candidate => new { candidate.Id, candidate.UpdatedAtUtc })
            .ToListAsync(cancellationToken);
        if (directorySources.Any(candidate =>
                !(source.Kind != LegacyPasswordSyncSourceKind.Stage2Migration && candidate.Id == source.AttemptId) &&
                candidate.UpdatedAtUtc >= source.CompletedAtUtc))
        {
            return true;
        }

        var recoverySources = await dbContext.RecoveryProofChallenges
            .AsNoTracking()
            .Where(candidate =>
                candidate.LocalAccountId == identity.LocalAccountId &&
                candidate.Purpose == RecoveryProofPurpose.NativePasswordRecovery &&
                candidate.ConsumedAtUtc != null &&
                candidate.RevokedAtUtc == null &&
                candidate.NativeDirectoryAuthority == true &&
                candidate.NativeDirectoryObjectId == identity.Binding.DirectoryObjectId)
            .Select(candidate => new { candidate.Id, CompletedAtUtc = candidate.ConsumedAtUtc!.Value })
            .ToListAsync(cancellationToken);
        if (recoverySources.Any(candidate =>
                candidate.Id != source.RecoveryProofChallengeId &&
                candidate.CompletedAtUtc >= source.CompletedAtUtc))
        {
            return true;
        }

        var continuationSources = await (
            from continuation in dbContext.CredentialMigrationContinuations.AsNoTracking()
            join state in dbContext.CredentialMigrationStateRecords.AsNoTracking()
                on continuation.CredentialMigrationStateRecordId equals state.Id
            where state.LocalAccountId == identity.LocalAccountId &&
                  state.ProviderSubjectDirectoryBindingId == identity.Binding.Id &&
                  continuation.ConsumedAtUtc != null
            select new
            {
                continuation.Id,
                MigrationStateId = state.Id,
                CompletedAtUtc = continuation.ConsumedAtUtc!.Value
            })
            .ToListAsync(cancellationToken);
        if (continuationSources.Any(candidate =>
                candidate.Id != source.AttemptId &&
                !(source.Completion == LegacyPasswordSyncSourceCompletion.MigrationLocalFinalized &&
                  candidate.MigrationStateId == source.MigrationStateId) &&
                candidate.CompletedAtUtc >= source.CompletedAtUtc))
        {
            return true;
        }

        var migrationSources = await dbContext.CredentialMigrationStateRecords
            .AsNoTracking()
            .Where(candidate =>
                candidate.LocalAccountId == identity.LocalAccountId &&
                candidate.ProviderSubjectDirectoryBindingId == identity.Binding.Id)
            .Select(candidate => new { candidate.Id, candidate.UpdatedAtUtc })
            .ToListAsync(cancellationToken);
        return migrationSources.Any(candidate =>
            candidate.Id != source.MigrationStateId &&
            candidate.UpdatedAtUtc >= source.CompletedAtUtc);
    }

    private static bool IsValid(
        LegacyPasswordSyncSourceReference source,
        LegacyPasswordSyncAttemptIdentity identity) =>
        source.AttemptId != Guid.Empty && source.Version > 0 &&
        (source.Kind is LegacyPasswordSyncSourceKind.NativeRecovery or
            LegacyPasswordSyncSourceKind.RequiredChange or
            LegacyPasswordSyncSourceKind.Stage2Migration) &&
        identity.LocalAccountId != Guid.Empty &&
        identity.Binding is not null &&
        identity.Binding.Id != Guid.Empty &&
        !string.IsNullOrWhiteSpace(identity.Binding.ProviderNamespace) &&
        !string.IsNullOrWhiteSpace(identity.Binding.StableSubject) &&
        identity.Binding.DirectoryObjectId != Guid.Empty &&
        identity.AccountIdentity != Guid.Empty &&
        !string.IsNullOrWhiteSpace(identity.MappingVersion) &&
        identity.MappingVersion.Length <= 128;

    private bool IsExactEnabledMapping(LegacyPasswordSyncAttemptIdentity identity)
    {
        var resolution = targetResolver.Resolve(
            identity.Binding.ProviderNamespace,
            identity.Binding.StableSubject,
            identity.MappingVersion);
        return resolution.Outcome == LegacyPasswordSyncMappingOutcome.Resolved &&
               resolution.Target is not null &&
               resolution.Target.AccountIdentityGuid == identity.AccountIdentity &&
               string.Equals(resolution.Target.MappingVersion, identity.MappingVersion, StringComparison.Ordinal);
    }

    private static bool Matches(
        LegacyPasswordSyncAttempt attempt,
        LegacyPasswordSyncSourceReference source,
        LegacyPasswordSyncAttemptIdentity identity) =>
        attempt.SourceKind == source.Kind &&
        attempt.SourceAttemptId == source.AttemptId &&
        attempt.SourceAttemptVersion == source.Version &&
        attempt.LocalAccountId == identity.LocalAccountId &&
        attempt.ProviderSubjectDirectoryBindingId == identity.Binding.Id &&
        attempt.AccountIdentity == identity.AccountIdentity &&
        string.Equals(attempt.MappingVersion, identity.MappingVersion, StringComparison.Ordinal);

    private async Task<ProviderSubjectDirectoryBinding?> LoadExactBindingAsync(
        LegacyPasswordSyncAttemptIdentity identity,
        CancellationToken cancellationToken) =>
        await dbContext.ProviderSubjectDirectoryBindings
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.Id == identity.Binding.Id &&
                candidate.LocalAccountId == identity.LocalAccountId &&
                candidate.ProviderNamespace == identity.Binding.ProviderNamespace &&
                candidate.StableSubject == identity.Binding.StableSubject &&
                candidate.DirectoryObjectId == identity.Binding.DirectoryObjectId,
                cancellationToken);

    private async Task<LegacyPasswordSyncAttemptRecord> ToRecordAsync(
        LegacyPasswordSyncAttempt attempt,
        CancellationToken cancellationToken)
    {
        var binding = await dbContext.ProviderSubjectDirectoryBindings
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == attempt.ProviderSubjectDirectoryBindingId, cancellationToken);
        return ToRecord(attempt, binding);
    }

    private static LegacyPasswordSyncAttemptRecord ToRecord(
        LegacyPasswordSyncAttempt attempt,
        ProviderSubjectDirectoryBinding binding) => new(
        attempt.OperationId,
        new(attempt.SourceKind, attempt.SourceAttemptId, attempt.SourceAttemptVersion),
        attempt.SourceCompletion,
        attempt.SourceCreatedAtUtc,
        attempt.SourceCompletedAtUtc,
        new(
            attempt.LocalAccountId,
            new(binding.Id, binding.ProviderNamespace, binding.StableSubject, binding.DirectoryObjectId),
            attempt.AccountIdentity,
            attempt.MappingVersion),
        attempt.Generation,
        attempt.Status,
        attempt.CreatedAtUtc,
        attempt.UpdatedAtUtc,
        attempt.ClaimedAtUtc,
        attempt.CompletedAtUtc,
        attempt.SanitizedOutcome,
        attempt.Version);

    private sealed record SourceMarker(
        LegacyPasswordSyncSourceKind Kind,
        Guid AttemptId,
        LegacyPasswordSyncSourceCompletion Completion,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset CompletedAtUtc,
        Guid? MigrationStateId = null,
        Guid? RecoveryProofChallengeId = null);
}
