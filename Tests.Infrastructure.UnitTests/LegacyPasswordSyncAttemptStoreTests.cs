using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class LegacyPasswordSyncAttemptStoreTests
{
    [Fact]
    public async Task ReserveAsync_UsesExactCompletedSourceAndPersistsMetadataOnly()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var context = CreateContext(connection);
        var identity = await SeedIdentityAsync(context);
        var migration = await SeedFinalizedMigrationAsync(context, identity, Utc(1));
        var continuation = new CredentialMigrationContinuationRecord(
            migration.Id,
            "token-hash",
            "context-hash",
            "csrf-hash",
            migration.UpdatedAtUtc.AddMinutes(-1),
            Utc(8));
        Assert.True(continuation.TryConsume(migration.UpdatedAtUtc));
        context.CredentialMigrationContinuations.Add(continuation);
        await context.SaveChangesAsync();
        var store = CreateStore(context, identity);

        var wrongVersion = await store.ReserveAsync(
            new(LegacyPasswordSyncSourceKind.Stage2Migration, migration.Id, migration.Version - 1),
            identity.AttemptIdentity);
        var wrongBinding = await store.ReserveAsync(
            new(LegacyPasswordSyncSourceKind.Stage2Migration, migration.Id, migration.Version),
            identity.AttemptIdentity with { Binding = identity.AttemptIdentity.Binding with { Id = Guid.NewGuid() } });
        var reserved = await store.ReserveAsync(
            new(LegacyPasswordSyncSourceKind.Stage2Migration, migration.Id, migration.Version),
            identity.AttemptIdentity);

        Assert.Equal(LegacyPasswordSyncReservationOutcome.SourceIneligible, wrongVersion.Outcome);
        Assert.Equal(LegacyPasswordSyncReservationOutcome.SourceIneligible, wrongBinding.Outcome);
        Assert.Equal(LegacyPasswordSyncReservationOutcome.Reserved, reserved.Outcome);
        Assert.Equal(LegacyPasswordSyncSourceCompletion.MigrationLocalFinalized, reserved.Attempt!.SourceCompletion);
        Assert.Equal(1, reserved.Attempt.Generation);

        var persisted = await context.LegacyPasswordSyncAttempts.AsNoTracking().SingleAsync();
        Assert.Equal(identity.AccountId, persisted.LocalAccountId);
        Assert.Equal(identity.Binding.Id, persisted.ProviderSubjectDirectoryBindingId);
        Assert.Equal(identity.AttemptIdentity.AccountIdentity, persisted.AccountIdentity);
        Assert.Equal("mapping-v1", persisted.MappingVersion);
        var attemptModel = context.Model.FindEntityType(typeof(LegacyPasswordSyncAttempt));
        Assert.Equal("LegacyPasswordSyncAttempts", attemptModel!.GetTableName());
        Assert.NotNull(attemptModel.FindProperty(nameof(LegacyPasswordSyncAttempt.AccountIdentity)));
        Assert.DoesNotContain(
            typeof(LegacyPasswordSyncAttempt).GetProperties(),
            property => property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Proof", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReserveAndClaimAsync_AcceptsAuthorizedRequiredChangeWithoutDirectorySuccess()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var context = CreateContext(connection);
        var identity = await SeedIdentityAsync(context);
        var attempt = new NativeDirectoryRecoveryAttempt(
            identity.AccountId,
            identity.Binding.DirectoryObjectId,
            NativeDirectoryCredentialOperationKind.RequiredChange,
            Utc(1));
        attempt.Complete(NativeDirectoryRecoveryStatus.LegacyPasswordSyncAuthorized, Utc(2));
        context.NativeDirectoryRecoveryAttempts.Add(attempt);
        await context.SaveChangesAsync();

        await AssertReservableAndClaimableAsync(
            CreateStore(context, identity),
            new(LegacyPasswordSyncSourceKind.RequiredChange, attempt.Id, attempt.Version),
            identity.AttemptIdentity,
            LegacyPasswordSyncSourceCompletion.RequiredChangeAuthorized);
        Assert.Equal(NativeDirectoryRecoveryStatus.LegacyPasswordSyncAuthorized, attempt.Status);
    }

    [Fact]
    public async Task ReserveAndClaimAsync_AcceptsConsumedBoundNativeRecoveryProof()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var context = CreateContext(connection);
        var identity = await SeedIdentityAsync(context);
        var recoveryEmail = new RecoveryEmailRecord(
            identity.AccountId,
            "recovery@example.test",
            "RECOVERY@EXAMPLE.TEST",
            Utc(0));
        recoveryEmail.MarkVerified(Utc(1));
        var challenge = new RecoveryProofChallenge(
            recoveryEmail.Id,
            identity.AccountId,
            RecoveryProofPurpose.NativePasswordRecovery,
            "code-hash",
            Utc(1),
            Utc(8));
        challenge.BindNativeAssistance(
            "context-hash",
            "csrf-hash",
            true,
            identity.Binding.DirectoryObjectId,
            recoveryEmail.Version,
            "security-stamp");
        Assert.True(challenge.TryMarkNativeRecoveryVerified("proof-hash", Utc(2)));
        Assert.True(challenge.TryConsume(Utc(3)));
        context.RecoveryEmails.Add(recoveryEmail);
        context.RecoveryProofChallenges.Add(challenge);
        await context.SaveChangesAsync();

        await AssertReservableAndClaimableAsync(
            CreateStore(context, identity),
            new(LegacyPasswordSyncSourceKind.NativeRecovery, challenge.Id, challenge.Version),
            identity.AttemptIdentity,
            LegacyPasswordSyncSourceCompletion.NativeRecoveryProofConsumed);
    }

    [Fact]
    public async Task ReserveAsync_NativeDirectoryCompletionIgnoresItsEqualTimestampProofPredecessor()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var context = CreateContext(connection);
        var identity = await SeedIdentityAsync(context);
        var recoveryEmail = new RecoveryEmailRecord(
            identity.AccountId,
            "recovery@example.test",
            "RECOVERY@EXAMPLE.TEST",
            Utc(0));
        recoveryEmail.MarkVerified(Utc(1));
        var challenge = new RecoveryProofChallenge(
            recoveryEmail.Id,
            identity.AccountId,
            RecoveryProofPurpose.NativePasswordRecovery,
            "code-hash",
            Utc(0),
            Utc(8));
        challenge.BindNativeAssistance(
            "context-hash",
            "csrf-hash",
            true,
            identity.Binding.DirectoryObjectId,
            recoveryEmail.Version,
            "security-stamp");
        Assert.True(challenge.TryMarkNativeRecoveryVerified("proof-hash", Utc(1)));
        Assert.True(challenge.TryConsume(Utc(2)));
        var attempt = new NativeDirectoryRecoveryAttempt(
            challenge.Id,
            identity.AccountId,
            identity.Binding.DirectoryObjectId,
            Utc(1));
        attempt.Complete(NativeDirectoryRecoveryStatus.Succeeded, Utc(2));
        context.RecoveryEmails.Add(recoveryEmail);
        context.RecoveryProofChallenges.Add(challenge);
        context.NativeDirectoryRecoveryAttempts.Add(attempt);
        await context.SaveChangesAsync();

        var reservation = await CreateStore(context, identity).ReserveAsync(
            new(LegacyPasswordSyncSourceKind.NativeRecovery, attempt.Id, attempt.Version),
            identity.AttemptIdentity);

        Assert.Equal(LegacyPasswordSyncReservationOutcome.Reserved, reservation.Outcome);
        Assert.Equal(
            LegacyPasswordSyncSourceCompletion.DirectoryAttemptSucceeded,
            reservation.Attempt!.SourceCompletion);
    }

    [Fact]
    public async Task ReserveAndClaimAsync_AcceptsConsumedMigrationContinuationWithoutDirectoryFinalization()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var context = CreateContext(connection);
        var identity = await SeedIdentityAsync(context);
        var migration = new CredentialMigrationStateRecord(identity.AccountId, identity.Binding.Id, Utc(0));
        migration.Advance(
            CredentialMigrationState.ProofValidated,
            EffectiveEmailOtpRequirement.NotRequired,
            Utc(1));
        var continuation = new CredentialMigrationContinuationRecord(
            migration.Id,
            "token-hash",
            "context-hash",
            "csrf-hash",
            Utc(1),
            Utc(8));
        Assert.True(continuation.TryConsume(Utc(2)));
        context.CredentialMigrationStateRecords.Add(migration);
        context.CredentialMigrationContinuations.Add(continuation);
        await context.SaveChangesAsync();

        await AssertReservableAndClaimableAsync(
            CreateStore(context, identity),
            new(LegacyPasswordSyncSourceKind.Stage2Migration, continuation.Id, continuation.Version),
            identity.AttemptIdentity,
            LegacyPasswordSyncSourceCompletion.MigrationContinuationConsumed);
        Assert.Equal(CredentialMigrationState.ProofValidated, migration.State);
    }

    [Fact]
    public async Task ReserveAsync_NewerActualPrimarySupersedesOnlyUndispatchedAndRejectsStaleHooks()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var context = CreateContext(connection);
        var identity = await SeedIdentityAsync(context);
        var first = await SeedRequiredChangeAsync(context, identity, Utc(1));
        var store = CreateStore(context, identity);
        var firstSource = Source(first);

        var firstReservation = await store.ReserveAsync(firstSource, identity.AttemptIdentity);
        var newer = await SeedRequiredChangeAsync(context, identity, Utc(3));
        var newerReservation = await store.ReserveAsync(Source(newer), identity.AttemptIdentity);
        var administratorAttempt = new NativeDirectoryRecoveryAttempt(
            identity.AccountId,
            identity.Binding.DirectoryObjectId,
            NativeDirectoryCredentialOperationKind.AdminTemporaryIssue,
            Utc(4));
        context.NativeDirectoryRecoveryAttempts.Add(administratorAttempt);
        await context.SaveChangesAsync();
        var blockedByNewerExcludedMutation = await store.ClaimAsync(
            newerReservation.Attempt!.OperationId,
            newerReservation.Attempt.Version,
            newerReservation.Attempt.Source,
            newerReservation.Attempt.Identity);
        var lateOlderPrimary = await SeedRequiredChangeAsync(context, identity, Utc(2));
        var staleReservation = await store.ReserveAsync(Source(lateOlderPrimary), identity.AttemptIdentity);
        var duplicate = await store.ReserveAsync(firstSource, identity.AttemptIdentity);

        Assert.Equal(LegacyPasswordSyncReservationOutcome.Reserved, firstReservation.Outcome);
        Assert.Equal(LegacyPasswordSyncReservationOutcome.Reserved, newerReservation.Outcome);
        Assert.Equal(2, newerReservation.Attempt!.Generation);
        Assert.Equal(LegacyPasswordSyncClaimOutcome.Stale, blockedByNewerExcludedMutation.Outcome);
        Assert.Equal(LegacyPasswordSyncReservationOutcome.StaleSource, staleReservation.Outcome);
        Assert.Equal(LegacyPasswordSyncReservationOutcome.DuplicateSource, duplicate.Outcome);
        Assert.Equal(
            LegacyPasswordSyncAttemptStatus.Superseded,
            (await store.FindAsync(firstReservation.Attempt!.OperationId))!.Status);
        Assert.Equal(2, await context.LegacyPasswordSyncAttempts.CountAsync());
    }

    [Fact]
    public async Task ClaimAsync_CompetingCasClaimsOnlyOnce()
    {
        var connectionString = SharedConnectionString();
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        LegacyPasswordSyncAttemptIdentity identity;
        LegacyPasswordSyncSourceReference source;
        LegacyPasswordSyncAttemptRecord reservation;
        await using (var setup = CreateContext(connectionString))
        {
            await setup.Database.EnsureCreatedAsync();
            var seeded = await SeedIdentityAsync(setup);
            var primary = await SeedRequiredChangeAsync(setup, seeded, Utc(1));
            identity = seeded.AttemptIdentity;
            source = Source(primary);
            reservation = (await CreateStore(setup, seeded)
                .ReserveAsync(source, identity)).Attempt!;
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var contenderContext = CreateContext(connectionString);
            return await CreateStore(contenderContext, identity)
                .ClaimAsync(reservation.OperationId, reservation.Version, source, identity);
        }));

        Assert.Single(results, result => result.Outcome == LegacyPasswordSyncClaimOutcome.Claimed);
        Assert.Single(results, result => result.Outcome is LegacyPasswordSyncClaimOutcome.Stale or LegacyPasswordSyncClaimOutcome.Contended);
        await using var verification = CreateContext(connectionString);
        Assert.Equal(
            LegacyPasswordSyncAttemptStatus.Claimed,
            (await verification.LegacyPasswordSyncAttempts.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task NewerGeneration_WaitsForClaimedAndUnknownAndLateResultMutatesOnlyItsAttempt()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var context = CreateContext(connection);
        var identity = await SeedIdentityAsync(context);
        var store = CreateStore(context, identity);
        var firstPrimary = await SeedRequiredChangeAsync(context, identity, Utc(1));
        var first = (await store.ReserveAsync(Source(firstPrimary), identity.AttemptIdentity)).Attempt!;
        var firstClaim = await store.ClaimAsync(
            first.OperationId,
            first.Version,
            first.Source,
            first.Identity);

        var secondPrimary = await SeedRequiredChangeAsync(context, identity, Utc(2));
        var second = (await store.ReserveAsync(Source(secondPrimary), identity.AttemptIdentity)).Attempt!;
        var blockedBehindClaim = await store.ClaimAsync(
            second.OperationId,
            second.Version,
            second.Source,
            second.Identity);
        var firstCompleted = await store.RecordResultAsync(
            first.OperationId,
            firstClaim.Attempt!.Version,
            LegacyPasswordSyncTerminalStatus.Succeeded,
            "legacy_success");
        var secondClaim = await store.ClaimAsync(
            second.OperationId,
            second.Version,
            second.Source,
            second.Identity);
        var secondUnknown = await store.RecordResultAsync(
            second.OperationId,
            secondClaim.Attempt!.Version,
            LegacyPasswordSyncTerminalStatus.Unknown,
            "commit_unknown");

        var thirdPrimary = await SeedRequiredChangeAsync(context, identity, Utc(3));
        var third = (await store.ReserveAsync(Source(thirdPrimary), identity.AttemptIdentity)).Attempt!;
        var blockedBehindUnknown = await store.ClaimAsync(
            third.OperationId,
            third.Version,
            third.Source,
            third.Identity);
        var cannotRewriteUnknown = await store.RecordResultAsync(
            second.OperationId,
            secondClaim.Attempt.Version,
            LegacyPasswordSyncTerminalStatus.Succeeded,
            "late_success");

        Assert.Equal(LegacyPasswordSyncClaimOutcome.Claimed, firstClaim.Outcome);
        Assert.Equal(LegacyPasswordSyncClaimOutcome.Blocked, blockedBehindClaim.Outcome);
        Assert.True(firstCompleted);
        Assert.Equal(LegacyPasswordSyncClaimOutcome.Claimed, secondClaim.Outcome);
        Assert.True(secondUnknown);
        Assert.Equal(LegacyPasswordSyncClaimOutcome.Blocked, blockedBehindUnknown.Outcome);
        Assert.False(cannotRewriteUnknown);
        Assert.Equal(LegacyPasswordSyncAttemptStatus.Succeeded, (await store.FindAsync(first.OperationId))!.Status);
        Assert.Equal(LegacyPasswordSyncAttemptStatus.Unknown, (await store.FindAsync(second.OperationId))!.Status);
        Assert.Equal(LegacyPasswordSyncAttemptStatus.Reserved, (await store.FindAsync(third.OperationId))!.Status);
        Assert.Equal(3, (await store.FindAsync(third.OperationId))!.Generation);
    }

    private static async Task<SqliteConnection> OpenDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        return connection;
    }

    private static ApplicationDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);

    private static ApplicationDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connectionString).Options);

    private static async Task<SeededIdentity> SeedIdentityAsync(ApplicationDbContext context)
    {
        var accountId = Guid.NewGuid();
        var binding = new ProviderSubjectDirectoryBinding(
            accountId,
            "example.provider",
            $"subject-{accountId:N}",
            Guid.NewGuid(),
            Utc(0).UtcDateTime);
        context.Users.Add(new ApplicationUser { Id = accountId, UserName = $"sync-{accountId:N}" });
        context.ProviderSubjectDirectoryBindings.Add(binding);
        await context.SaveChangesAsync();
        return new(
            accountId,
            binding,
            new(
                accountId,
                new(binding.Id, binding.ProviderNamespace, binding.StableSubject, binding.DirectoryObjectId),
                Guid.NewGuid(),
                "mapping-v1"));
    }

    private static async Task<NativeDirectoryRecoveryAttempt> SeedRequiredChangeAsync(
        ApplicationDbContext context,
        SeededIdentity identity,
        DateTimeOffset createdAtUtc)
    {
        var attempt = new NativeDirectoryRecoveryAttempt(
            identity.AccountId,
            identity.Binding.DirectoryObjectId,
            NativeDirectoryCredentialOperationKind.RequiredChange,
            createdAtUtc);
        attempt.Complete(NativeDirectoryRecoveryStatus.Succeeded, createdAtUtc.AddMinutes(1));
        context.NativeDirectoryRecoveryAttempts.Add(attempt);
        await context.SaveChangesAsync();
        return attempt;
    }

    private static async Task<CredentialMigrationStateRecord> SeedFinalizedMigrationAsync(
        ApplicationDbContext context,
        SeededIdentity identity,
        DateTimeOffset createdAtUtc)
    {
        var migration = new CredentialMigrationStateRecord(identity.AccountId, identity.Binding.Id, createdAtUtc);
        migration.Advance(CredentialMigrationState.ProofValidated, EffectiveEmailOtpRequirement.NotRequired, createdAtUtc.AddMinutes(1));
        migration.Advance(CredentialMigrationState.DirectoryCredentialCommitted, createdAtUtc.AddMinutes(2));
        migration.Advance(CredentialMigrationState.LocalFinalized, createdAtUtc.AddMinutes(3));
        context.CredentialMigrationStateRecords.Add(migration);
        await context.SaveChangesAsync();
        return migration;
    }

    private static LegacyPasswordSyncSourceReference Source(NativeDirectoryRecoveryAttempt attempt) =>
        new(LegacyPasswordSyncSourceKind.RequiredChange, attempt.Id, attempt.Version);

    private static async Task AssertReservableAndClaimableAsync(
        LegacyPasswordSyncAttemptStore store,
        LegacyPasswordSyncSourceReference source,
        LegacyPasswordSyncAttemptIdentity identity,
        LegacyPasswordSyncSourceCompletion expectedCompletion)
    {
        var reservation = await store.ReserveAsync(source, identity);
        Assert.Equal(LegacyPasswordSyncReservationOutcome.Reserved, reservation.Outcome);
        Assert.Equal(expectedCompletion, reservation.Attempt!.SourceCompletion);

        var claim = await store.ClaimAsync(
            reservation.Attempt.OperationId,
            reservation.Attempt.Version,
            source,
            identity);
        Assert.Equal(LegacyPasswordSyncClaimOutcome.Claimed, claim.Outcome);
    }

    private static LegacyPasswordSyncAttemptStore CreateStore(
        ApplicationDbContext context,
        SeededIdentity identity) =>
        new(context, new FixedTargetResolver(identity.AttemptIdentity), new FixedTimeProvider());

    private static LegacyPasswordSyncAttemptStore CreateStore(
        ApplicationDbContext context,
        LegacyPasswordSyncAttemptIdentity identity) =>
        new(context, new FixedTargetResolver(identity), new FixedTimeProvider());

    private static DateTimeOffset Utc(int day) => new(2026, 9, 12 + day, 0, 0, 0, TimeSpan.Zero);

    private static string SharedConnectionString() =>
        $"Data Source=file:legacy-attempt-{Guid.NewGuid():N}?mode=memory&cache=shared;Default Timeout=5";

    private sealed record SeededIdentity(
        Guid AccountId,
        ProviderSubjectDirectoryBinding Binding,
        LegacyPasswordSyncAttemptIdentity AttemptIdentity);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Utc(10);
    }

    private sealed class FixedTargetResolver(LegacyPasswordSyncAttemptIdentity identity) : ILegacyPasswordSyncTargetResolver
    {
        public Core.Application.DTOs.LegacyPasswordSyncMappingResolution Resolve(
            string providerNamespace,
            string stableSubject,
            string? expectedMappingVersion = null) =>
            providerNamespace == identity.Binding.ProviderNamespace &&
            stableSubject == identity.Binding.StableSubject &&
            expectedMappingVersion == identity.MappingVersion
                ? new(
                    Core.Application.DTOs.LegacyPasswordSyncMappingOutcome.Resolved,
                    new(identity.AccountIdentity, identity.MappingVersion))
                : new(Core.Application.DTOs.LegacyPasswordSyncMappingOutcome.Missing);
    }
}
