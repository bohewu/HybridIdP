using Core.Domain.Entities;
using Infrastructure;
using Core.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class RecoverySelectionPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PendingCandidate_LeavesActiveAndSamePersonOtherAccountUnchangedOnCancel()
    {
        await using var connection = await OpenAsync();
        await using var db = Context(connection);
        var person = new Person { Id = Guid.NewGuid() };
        var account = new ApplicationUser { Id = Guid.NewGuid(), PersonId = person.Id };
        var other = new ApplicationUser { Id = Guid.NewGuid(), PersonId = person.Id };
        db.Persons.Add(person);
        db.Users.AddRange(account, other);
        var active = Email(account.Id);
        var otherActive = Email(other.Id);
        db.RecoveryEmails.AddRange(active, otherActive);
        var stepUp = StepUp(account.Id);
        db.RecoveryStepUpGrants.Add(stepUp);
        var pending = Pending(account.Id, stepUp.Id);
        db.RecoveryEmailChangeRequests.Add(pending);
        await db.SaveChangesAsync();
        pending.Revoke(Now.AddSeconds(1));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal("active@example.test", (await db.RecoveryEmails.SingleAsync(e => e.LocalAccountId == account.Id)).Address);
        Assert.Equal("active@example.test", (await db.RecoveryEmails.SingleAsync(e => e.LocalAccountId == other.Id)).Address);
        Assert.Empty(await db.RecoveryEmailPreferences.ToListAsync());
        Assert.False(pending.TryConsume(1, Now.AddSeconds(2)));
    }

    [Fact]
    public async Task ConcurrentPreferenceSwitch_OnlyOneEpochAndNoPartialSecondAddressWrite()
    {
        await using var connection = await OpenAsync();
        var account = Guid.NewGuid();
        await using (var setup = Context(connection))
        {
            setup.Users.Add(new ApplicationUser { Id = account });
            setup.RecoveryEmails.Add(Email(account));
            setup.RecoveryEmailPreferences.Add(new RecoveryEmailPreference(account, Now));
            await setup.SaveChangesAsync();
        }
        await using var first = Context(connection);
        await using var second = Context(connection);
        var p1 = await first.RecoveryEmailPreferences.SingleAsync();
        var p2 = await second.RecoveryEmailPreferences.SingleAsync();
        var e1 = await first.RecoveryEmails.SingleAsync();
        var e2 = await second.RecoveryEmails.SingleAsync();
        Assert.True(p1.TrySelect(RecoveryEmailSelectionMode.UseCustom, 1, Now));
        Assert.True(p2.TrySelect(RecoveryEmailSelectionMode.UseCustom, 1, Now));
        e1.ActivateVerifiedCustom("winner@example.test", "WINNER@example.test", Now, RecoveryEmailProvenance.UserVerified);
        e2.ActivateVerifiedCustom("loser@example.test", "LOSER@example.test", Now, RecoveryEmailProvenance.UserVerified);
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var read = Context(connection);
        Assert.Equal(2, (await read.RecoveryEmailPreferences.SingleAsync()).SelectionEpoch);
        Assert.Equal("winner@example.test", (await read.RecoveryEmails.SingleAsync()).Address);
    }

    [Fact]
    public async Task GrantReservation_AtomicRollbackThenSingleCommittedChallengeAcrossContexts()
    {
        await using var connection = await OpenAsync();
        var account = Guid.NewGuid();
        await using (var setup = Context(connection))
        {
            setup.Users.Add(new ApplicationUser { Id = account });
            setup.RecoveryPrecheckGrants.Add(Grant(account));
            await setup.SaveChangesAsync();
        }
        await using (var rolledBack = Context(connection))
        {
            await using var tx = await rolledBack.Database.BeginTransactionAsync();
            var grant = await rolledBack.RecoveryPrecheckGrants.SingleAsync();
            var challenge = Challenge(account);
            Assert.True(grant.TryReserveChallenge(challenge, Now));
            rolledBack.RecoveryProofChallenges.Add(challenge);
            await rolledBack.SaveChangesAsync();
            await tx.RollbackAsync();
        }
        await using var first = Context(connection);
        await using var second = Context(connection);
        var g1 = await first.RecoveryPrecheckGrants.SingleAsync();
        var g2 = await second.RecoveryPrecheckGrants.SingleAsync();
        Assert.Null(g1.ConsumedAtUtc);
        Assert.Empty(await first.RecoveryProofChallenges.ToListAsync());
        var c1 = Challenge(account);
        var c2 = Challenge(account);
        Assert.True(g1.TryReserveChallenge(c1, Now));
        Assert.True(g2.TryReserveChallenge(c2, Now));
        first.RecoveryProofChallenges.Add(c1);
        second.RecoveryProofChallenges.Add(c2);
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var read = Context(connection);
        Assert.Single(await read.RecoveryProofChallenges.ToListAsync());
        Assert.Equal(c1.Id, (await read.RecoveryPrecheckGrants.SingleAsync()).ReservedChallengeId);
        Assert.False(c1.TryReserveAttempt(Now, 5));
        Assert.True(c1.TryCompleteDelivery(Now, false));
        Assert.False(c1.TryReserveAttempt(Now, 5));
        Assert.False(c1.TryMarkNativeRecoveryVerified("proof", Now));
    }

    [Fact]
    public async Task PendingUniqueness_RequiresExplicitRevocationBeforeReplacement()
    {
        await using var connection = await OpenAsync();
        await using var db = Context(connection);
        var account = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = account });
        var step = StepUp(account);
        db.RecoveryStepUpGrants.Add(step);
        var first = Pending(account, step.Id);
        db.RecoveryEmailChangeRequests.Add(first);
        await db.SaveChangesAsync();
        var replacement = Pending(account, step.Id);
        db.RecoveryEmailChangeRequests.Add(replacement);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.Entry(replacement).State = EntityState.Detached;
        first.Revoke(Now);
        await db.SaveChangesAsync();
        db.RecoveryEmailChangeRequests.Add(replacement);
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.RecoveryEmailChangeRequests.CountAsync());
    }

    [Fact]
    public void PendingResend_PreservesAttemptBudgetExpiryAndActiveDestination()
    {
        var account = Guid.NewGuid();
        var pending = Pending(account, Guid.NewGuid());
        var expiry = pending.ExpiresAtUtc;
        Assert.False(pending.TryReserveAttempt(Now));
        Assert.True(pending.TryMarkDelivered(Now));
        Assert.True(pending.TryReserveAttempt(Now));
        Assert.True(pending.TryReserveResend("new-hash", Now.AddMinutes(1), Now.AddMinutes(2)));
        Assert.Equal(1, pending.VerificationAttempts);
        Assert.Equal(expiry, pending.ExpiresAtUtc);
        Assert.False(pending.TryConsume(1, Now.AddMinutes(1)));
        Assert.True(pending.TryMarkDelivered(Now.AddMinutes(1)));
        Assert.False(pending.TryConsume(2, Now.AddMinutes(1)));
        Assert.True(pending.TryReserveAttempt(Now.AddMinutes(1)));
        Assert.False(pending.TryReserveAttempt(Now.AddMinutes(1)));
        Assert.False(pending.TryReserveResend("third", Now.AddMinutes(2), Now.AddMinutes(3)));
        Assert.False(pending.TryConsume(1, expiry));
    }

    [Fact]
    public void Preference_LegacyAndIndependentDenialsAreNotInferredOrCleared()
    {
        var account = Guid.NewGuid();
        Assert.Equal(RecoveryEmailProvenance.LegacyUnknown, Email(account).Provenance);
        var preference = new RecoveryEmailPreference(account, Now);
        Assert.Equal(RecoveryEmailSelectionMode.Legacy, preference.Mode);
        preference.SetSourceDefaultOptOut(true, Now);
        Assert.True(preference.TrySelect(RecoveryEmailSelectionMode.UseCustom, 2, Now));
        preference.SetAdministrativeBlock(true, Now);
        preference.SetSourceDefaultOptOut(false, Now);
        Assert.NotNull(preference.AdministrativeBlockedAtUtc);
        Assert.False(preference.TrySelect(RecoveryEmailSelectionMode.UseDefault, preference.SelectionEpoch, Now));
        preference.SetSourceDefaultOptOut(true, Now);
        preference.SetAdministrativeBlock(false, Now);
        Assert.NotNull(preference.SourceDefaultOptOutAtUtc);
        Assert.False(preference.TrySelect(RecoveryEmailSelectionMode.UseCustom, 1, Now));
    }

    [Fact]
    public void SelectionBinding_IsImmutableAndStaleAcrossChallengeAndBothApprovalPurposes()
    {
        var account = Guid.NewGuid();
        var challenge = Challenge(account);
        Assert.True(challenge.MatchesSelection(1, RecoveryDestinationKind.TrustedDefault, "destination", 1));
        Assert.False(challenge.MatchesSelection(2, RecoveryDestinationKind.TrustedDefault, "destination", 1));
        Assert.False(challenge.MatchesSelection(1, RecoveryDestinationKind.TrustedDefault, "changed", 1));
        Assert.Throws<InvalidOperationException>(() => challenge.BindSelection(2, RecoveryDestinationKind.Custom, "other", 1));
        var native = NativeRecoveryResetApproval.CreateForDefault(challenge, Guid.NewGuid(), "reason", "checked", Now, Now.AddMinutes(2));
        Assert.Null(native.RecoveryEmailId);
        Assert.False(native.MatchesSelection(2, RecoveryDestinationKind.TrustedDefault, "destination", 1));
        var migration = new RecoveryResetApproval(account, Guid.NewGuid(), Guid.NewGuid(), "reason", "checked", "token", Now, Now.AddMinutes(2));
        migration.BindSelection(1, RecoveryDestinationKind.TrustedDefault, "destination", 1);
        Assert.False(migration.MatchesSelection(2, RecoveryDestinationKind.TrustedDefault, "destination", 1));
        var migrationChallenge = RecoveryProofChallenge.CreateForDefault(account, "hash", Now, Now.AddMinutes(2), 1, "destination", 1,
            RecoveryProofPurpose.MigrationOtp, Guid.NewGuid());
        Assert.Null(migrationChallenge.RecoveryEmailId);
        Assert.False(migrationChallenge.TryMarkNativeRecoveryVerified("proof", Now));
        Assert.False(Grant(account).TryReserveChallenge(migrationChallenge, Now));
    }

    [Fact]
    public void Precheck_RejectsWrongAccountContextEpochExpiredAndReplay()
    {
        var account = Guid.NewGuid();
        var grant = Grant(account);
        Assert.False(grant.TryReserveChallenge(Challenge(Guid.NewGuid()), Now));
        var wrongContext = Challenge(account, "other");
        Assert.False(grant.TryReserveChallenge(wrongContext, Now));
        Assert.False(grant.TryReserveChallenge(Challenge(account, epoch: 2), Now));
        Assert.False(grant.TryReserveChallenge(Challenge(account), Now.AddMinutes(5)));
        Assert.True(grant.TryReserveChallenge(Challenge(account), Now));
        Assert.False(grant.TryReserveChallenge(Challenge(account), Now));
    }

    [Fact]
    public async Task StepUp_IsFreshPurposeSpecificSingleUseAndConcurrentConsumeFails()
    {
        await using var connection = await OpenAsync();
        var account = Guid.NewGuid();
        await using (var db = Context(connection))
        {
            db.Users.Add(new ApplicationUser { Id = account });
            db.RecoveryStepUpGrants.Add(StepUp(account));
            await db.SaveChangesAsync();
        }
        await using var first = Context(connection);
        await using var second = Context(connection);
        var s1 = await first.RecoveryStepUpGrants.SingleAsync();
        var s2 = await second.RecoveryStepUpGrants.SingleAsync();
        Assert.False(s1.TryConsume(account, "stamp", "browser", "csrf", "wrong-authority", Now));
        Assert.False(s1.TryConsume(account, "stamp", "browser", "csrf", "authority", Now.AddMinutes(5)));
        Assert.True(s1.TryConsume(account, "stamp", "browser", "csrf", "authority", Now));
        Assert.True(s2.TryConsume(account, "stamp", "browser", "csrf", "authority", Now));
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        Assert.False(s1.TryConsume(account, "stamp", "browser", "csrf", "authority", Now));
    }

    [Fact]
    public async Task Notification_LeaseSurvivesRestartAndHonorsRetryBudget()
    {
        await using var connection = await OpenAsync();
        var account = Guid.NewGuid();
        var oldLease = Guid.NewGuid();
        await using (var db = Context(connection))
        {
            db.Users.Add(new ApplicationUser { Id = account });
            var notification = new RecoveryNotification(account, 2, RecoveryNotificationKind.CustomChangedOldDestination,
                "old@example.test", Now, Now.AddHours(1), 2);
            Assert.True(notification.TryClaim(oldLease, Now, Now.AddMinutes(1)));
            db.RecoveryNotifications.Add(notification);
            await db.SaveChangesAsync();
        }
        await using var restarted = Context(connection);
        var restored = await restarted.RecoveryNotifications.SingleAsync();
        Assert.False(restored.TryClaim(Guid.NewGuid(), Now, Now.AddMinutes(1)));
        var newLease = Guid.NewGuid();
        Assert.True(restored.TryClaim(newLease, Now.AddMinutes(1), Now.AddMinutes(2)));
        Assert.False(restored.TryComplete(oldLease, Now.AddMinutes(1)));
        Assert.True(restored.TryFail(newLease, Now.AddMinutes(1), Now.AddMinutes(3)));
        Assert.NotNull(restored.AbandonedAtUtc);
        Assert.False(restored.TryClaim(Guid.NewGuid(), Now.AddMinutes(3), Now.AddMinutes(4)));
        await restarted.SaveChangesAsync();
    }

    [Fact]
    public async Task SharedThrottle_ConcurrentContextsCannotSpendTheSameBudget()
    {
        await using var connection = await OpenAsync();
        await using (var db = Context(connection))
        {
            db.RecoveryThrottleBuckets.Add(new RecoveryThrottleBucket("partition", Now, Now.AddMinutes(15)));
            await db.SaveChangesAsync();
        }
        await using var first = Context(connection);
        await using var second = Context(connection);
        var t1 = await first.RecoveryThrottleBuckets.SingleAsync();
        var t2 = await second.RecoveryThrottleBuckets.SingleAsync();
        Assert.True(t1.TryTake(Now, 1, TimeSpan.FromMinutes(15)));
        Assert.True(t2.TryTake(Now, 1, TimeSpan.FromMinutes(15)));
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        Assert.False(t1.TryTake(Now, 1, TimeSpan.FromMinutes(15)));
        Assert.True(t1.TryTake(Now.AddMinutes(15), 1, TimeSpan.FromMinutes(15)));
    }

    private static RecoveryEmailRecord Email(Guid account)
    {
        var record = new RecoveryEmailRecord(account, "active@example.test", "ACTIVE@example.test", Now);
        record.MarkVerified(Now);
        return record;
    }

    private static RecoveryStepUpGrant StepUp(Guid account) => new(account, "stamp", "browser", "csrf", "authority", Now, Now.AddMinutes(5));
    private static RecoveryEmailPendingChange Pending(Guid account, Guid stepUp) => new(account, "candidate@example.test", "CANDIDATE@example.test",
        "hash", 1, "stamp", "browser", "csrf", stepUp, Now, Now.AddMinutes(5), Now.AddMinutes(1), 2);
    private static RecoveryPrecheckGrant Grant(Guid account) => new(account, null, null, "stamp", "browser", "csrf", "policy",
        1, RecoveryDestinationKind.TrustedDefault, "destination", 1, Now, Now.AddMinutes(5));
    private static RecoveryProofChallenge Challenge(Guid account, string context = "browser", long epoch = 1)
    {
        var challenge = RecoveryProofChallenge.CreateForDefault(account, "hash", Now, Now.AddMinutes(5), epoch, "destination", 1);
        challenge.BindNativeAssistance(context, "csrf", false, null, 1, "stamp");
        return challenge;
    }

    private static ApplicationDbContext Context(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);

    private static async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.Database.EnsureCreatedAsync();
        return connection;
    }
}
