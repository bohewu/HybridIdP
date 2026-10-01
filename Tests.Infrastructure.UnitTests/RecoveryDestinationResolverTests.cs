using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class RecoveryDestinationResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(RecoveryEmailProvenance.UserVerified, RecoveryDestinationKind.Custom)]
    [InlineData(RecoveryEmailProvenance.AdminAssistedVerified, RecoveryDestinationKind.Custom)]
    [InlineData(RecoveryEmailProvenance.LegacyUnknown, RecoveryDestinationKind.Legacy)]
    public async Task ResolveAsync_VerifiedCustom_DoesNotReadDefaultEvenWithSourceOptOut(
        RecoveryEmailProvenance provenance, RecoveryDestinationKind kind)
    {
        await using var db = Context();
        var user = User(db);
        user.RecoverySourceBootstrapRevokedAtUtc = Now.AddDays(-2);
        var preference = Preference(db, user.Id);
        preference.SetSourceDefaultOptOut(true, Now.AddMinutes(-1));
        var active = Custom(db, user.Id, provenance);
        Snapshot(db, user.Id);
        await db.SaveChangesAsync();
        var source = new Mock<IRecoveryDefaultDestinationEvaluator>(MockBehavior.Strict);

        var result = await Resolver(db, source.Object).ResolveAsync(user.Id);

        Assert.Equal(RecoveryDestinationFailure.None, result.Failure);
        Assert.Equal(active.Address, result.Destination!.Address);
        Assert.Equal(kind, result.Destination.Kind);
        Assert.Equal(active.Id, result.Destination.RecoveryEmailId);
        Assert.DoesNotContain(active.Address, result.ToString());
        source.VerifyNoOtherCalls();
        Assert.NotNull(user.RecoverySourceBootstrapRevokedAtUtc);
    }

    [Theory]
    [InlineData(false, "pending")]
    [InlineData(false, "expired")]
    [InlineData(false, "cancelled")]
    [InlineData(false, "failed")]
    [InlineData(false, "replaced")]
    [InlineData(true, "pending")]
    [InlineData(true, "expired")]
    [InlineData(true, "cancelled")]
    [InlineData(true, "failed")]
    [InlineData(true, "replaced")]
    public async Task ResolveAsync_PendingDoesNotChangeEffectiveDestination(bool hasCustom, string state)
    {
        await using var db = Context();
        var user = User(db);
        if (hasCustom) Custom(db, user.Id);
        Snapshot(db, user.Id);
        await db.SaveChangesAsync();
        var before = (await Resolver(db).ResolveAsync(user.Id)).Destination!;
        var pending = Pending(user.Id, state == "expired" ? Now.AddHours(-2) : Now.AddMinutes(-2));
        if (state is "cancelled" or "failed" or "replaced") pending.Revoke(Now.AddMinutes(-1));
        db.RecoveryEmailChangeRequests.Add(pending);
        if (state == "replaced") db.RecoveryEmailChangeRequests.Add(Pending(user.Id, Now));
        await db.SaveChangesAsync();

        var after = (await Resolver(db).ResolveAsync(user.Id)).Destination!;

        Assert.True(before.Matches(after));
        Assert.Equal(hasCustom ? "custom@example.test" : "source@example.test", after.Address);
        Assert.Equal(hasCustom ? 1 : 0, await db.RecoveryEmails.CountAsync());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unverified")]
    [InlineData("corrupt")]
    [InlineData("normalization")]
    [InlineData("future")]
    public async Task ResolveAsync_BrokenUseCustom_NeverFallsBack(string state)
    {
        await using var db = Context();
        var user = User(db);
        var preference = Preference(db, user.Id);
        Assert.True(preference.TrySelect(RecoveryEmailSelectionMode.UseCustom, 1, Now));
        if (state != "missing")
        {
            var active = Custom(db, user.Id);
            if (state == "unverified") active.ReplaceAddress(active.Address, active.NormalizedAddress, Now, Now);
            if (state == "corrupt") db.Entry(active).Property(record => record.Address).CurrentValue = "not-an-email";
            if (state == "normalization") db.Entry(active).Property(record => record.NormalizedAddress).CurrentValue = "OTHER@EXAMPLE.TEST";
            if (state == "future") active.MarkVerified(Now.AddMinutes(1));
        }
        Snapshot(db, user.Id);
        await db.SaveChangesAsync();
        var source = new Mock<IRecoveryDefaultDestinationEvaluator>(MockBehavior.Strict);

        var result = await Resolver(db, source.Object).ResolveAsync(user.Id);

        Assert.Equal(RecoveryDestinationFailure.InvalidCustom, result.Failure);
        Assert.Null(result.Destination);
        source.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("historical")]
    [InlineData("opt-out")]
    [InlineData("disabled")]
    [InlineData("administrator")]
    public async Task ResolveAsync_RevocationsWithoutCustom_ArePreserved(string denial)
    {
        await using var db = Context();
        var user = User(db);
        var preference = Preference(db, user.Id);
        if (denial == "historical") user.RecoverySourceBootstrapRevokedAtUtc = Now;
        if (denial == "opt-out") preference.SetSourceDefaultOptOut(true, Now);
        if (denial == "disabled") preference.TrySelect(RecoveryEmailSelectionMode.Disabled, 1, Now);
        if (denial == "administrator") preference.SetAdministrativeBlock(true, Now);
        Snapshot(db, user.Id);
        await db.SaveChangesAsync();
        var source = new Mock<IRecoveryDefaultDestinationEvaluator>(MockBehavior.Strict);

        var result = await Resolver(db, source.Object).ResolveAsync(user.Id);

        Assert.Null(result.Destination);
        Assert.Equal(denial is "disabled" or "administrator" ? RecoveryDestinationFailure.Blocked :
            RecoveryDestinationFailure.SourceOptOut, result.Failure);
        source.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResolveAsync_AdministratorBlock_AlsoBlocksValidCustom()
    {
        await using var db = Context();
        var user = User(db);
        Custom(db, user.Id);
        Preference(db, user.Id).SetAdministrativeBlock(true, Now);
        await db.SaveChangesAsync();
        Assert.Equal(RecoveryDestinationFailure.Blocked, (await Resolver(db).ResolveAsync(user.Id)).Failure);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("locked")]
    [InlineData("person-ineligible")]
    [InlineData("person-missing")]
    public async Task ResolveAsync_LocalLifecycleDeniesEvenWithTrustedSource(string condition)
    {
        await using var db = Context();
        var user = User(db);
        if (condition == "inactive") user.IsActive = false;
        if (condition == "deleted") user.IsDeleted = true;
        if (condition == "locked") { user.LockoutEnabled = true; user.LockoutEnd = Now.AddMinutes(1); }
        if (condition.StartsWith("person")) user.PersonId = Guid.NewGuid();
        if (condition == "person-ineligible") db.Persons.Add(new Person { Id = user.PersonId!.Value, Status = PersonStatus.Suspended });
        Snapshot(db, user.Id);
        await db.SaveChangesAsync();
        Assert.Equal(RecoveryDestinationFailure.IneligibleAccount, (await Resolver(db).ResolveAsync(user.Id)).Failure);
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(true, false, true, true)]
    public async Task ResolveAsync_PersistentDefaultRequiresSeparateOptIn(bool enabled, bool fallback, bool bootstrap, bool expected)
    {
        await using var db = Context();
        var user = User(db);
        Snapshot(db, user.Id);
        await db.SaveChangesAsync();
        var policy = Policy();
        policy.BootstrapEnabled = bootstrap;
        policy.BootstrapUntilUtc = Now.AddMinutes(1);
        var selection = new RecoveryEmailSelectionOptions { Enabled = enabled, TrustedDefaultFallbackEnabled = fallback };

        var result = await Resolver(db, policy: policy, selection: selection).ResolveAsync(user.Id);

        Assert.Equal(expected, result.Destination is not null);
        if (expected) Assert.Equal(RecoveryDestinationKind.TrustedDefault, result.Destination!.Kind);
        Assert.Empty(db.RecoveryEmails);
        Assert.Empty(db.RecoveryEmailPreferences);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("future-refresh")]
    [InlineData("future-verified")]
    [InlineData("origin-disabled")]
    [InlineData("unknown")]
    [InlineData("unavailable")]
    [InlineData("conflict")]
    [InlineData("contact-only")]
    public async Task ResolveAsync_UntrustedDefault_IsNeverPromoted(string state)
    {
        await using var db = Context();
        var user = User(db);
        var policy = Policy();
        var snapshot = state == "contact-only" ? null : Snapshot(db, user.Id);
        if (state == "stale") snapshot!.Refresh("source@example.test", ProviderEmailTrustOrigin.SourceVerified, null, Now.AddMonths(-1));
        if (state == "future-refresh") snapshot!.Refresh("source@example.test", ProviderEmailTrustOrigin.SourceVerified, null, Now.AddSeconds(1));
        if (state == "future-verified") snapshot!.Refresh("source@example.test", ProviderEmailTrustOrigin.SourceVerified, Now.AddMinutes(1), Now.AddMinutes(2));
        if (state == "origin-disabled") policy.AcceptSourceVerifiedEmails = false;
        if (state == "unknown") snapshot!.Refresh("source@example.test", ProviderEmailTrustOrigin.Unknown, null, Now);
        if (state == "unavailable") snapshot!.Invalidate(Now, ProviderMetadataEvidenceState.Unavailable);
        if (state == "conflict") Snapshot(db, user.Id, "other@example.test");
        await db.SaveChangesAsync();

        Assert.Equal(RecoveryDestinationFailure.NoEligibleDefault, (await Resolver(db, policy: policy).ResolveAsync(user.Id)).Failure);
        Assert.Empty(db.RecoveryEmails);
    }

    [Fact]
    public async Task ResolveAsync_PolicyTrustedOriginRequiresItsOwnSwitch()
    {
        await using var db = Context();
        var user = User(db);
        Snapshot(db, user.Id).Refresh("source@example.test", ProviderEmailTrustOrigin.PolicyTrusted, null, Now);
        await db.SaveChangesAsync();
        var policy = Policy();
        Assert.Null((await Resolver(db, policy: policy).ResolveAsync(user.Id)).Destination);
        policy.AcceptPolicyTrustedEmails = true;
        Assert.NotNull((await Resolver(db, policy: policy).ResolveAsync(user.Id)).Destination);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResolveAsync_ExplicitUseDefault_DeselectsRetainedCustomIncludingOnRollback(bool enabled)
    {
        await using var db = Context();
        var user = User(db);
        var active = Custom(db, user.Id);
        Preference(db, user.Id).TrySelect(RecoveryEmailSelectionMode.UseDefault, 1, Now);
        Snapshot(db, user.Id);
        await db.SaveChangesAsync();
        var policy = Policy();
        policy.BootstrapEnabled = true;
        policy.BootstrapUntilUtc = Now.AddHours(1);
        var result = await Resolver(db, policy: policy, selection: new() { Enabled = enabled }).ResolveAsync(user.Id);
        Assert.Equal("source@example.test", result.Destination!.Address);
        Assert.Null(result.Destination.RecoveryEmailId);
        Assert.Equal("custom@example.test", active.Address);
        Assert.Equal(RecoveryDestinationKind.TrustedDefault, result.Destination.Kind);
    }

    [Fact]
    public async Task ResolveAsync_RollbackPreservesSelectedCustomAndEpoch()
    {
        await using var db = Context();
        var user = User(db);
        Custom(db, user.Id);
        var preference = Preference(db, user.Id);
        preference.TrySelect(RecoveryEmailSelectionMode.UseCustom, 1, Now);
        Snapshot(db, user.Id);
        await db.SaveChangesAsync();
        var result = await Resolver(db, selection: new()).ResolveAsync(user.Id);
        Assert.Equal(RecoveryDestinationKind.Custom, result.Destination!.Kind);
        Assert.Equal(preference.SelectionEpoch, result.Destination.SelectionEpoch);
    }

    [Fact]
    public async Task ResolveAsync_LegacyBootstrapRow_RemainsDefaultAndIsNotPromoted()
    {
        await using var db = Context();
        var user = User(db);
        var record = new RecoveryEmailRecord(user.Id, "source@example.test", "SOURCE@EXAMPLE.TEST", Now.AddDays(-1));
        db.RecoveryEmails.Add(record);
        Snapshot(db, user.Id);
        await db.SaveChangesAsync();
        var policy = Policy();
        policy.BootstrapEnabled = true;
        policy.BootstrapUntilUtc = Now.AddHours(1);
        var result = await Resolver(db, policy: policy).ResolveAsync(user.Id);
        Assert.Equal(RecoveryDestinationKind.TrustedDefault, result.Destination!.Kind);
        Assert.Null(result.Destination.RecoveryEmailId);
        Assert.Null(record.VerifiedAtUtc);
        Assert.Equal(RecoveryEmailProvenance.LegacyUnknown, record.Provenance);
        Assert.Empty(db.RecoveryEmailPreferences);
        policy.BootstrapUntilUtc = Now;
        Assert.Null((await Resolver(db, policy: policy).ResolveAsync(user.Id)).Destination);
    }

    [Fact]
    public async Task ResolveAsync_SourceProvenancePossession_DoesNotCreateCustomPreference()
    {
        await using var db = Context();
        var user = User(db);
        var record = Custom(db, user.Id);
        db.Entry(record).Property(email => email.Provenance).CurrentValue = RecoveryEmailProvenance.SourceDefault;
        Snapshot(db, user.Id);
        await db.SaveChangesAsync();
        var result = await Resolver(db).ResolveAsync(user.Id);
        Assert.Equal("source@example.test", result.Destination!.Address);
        Assert.Equal(RecoveryDestinationKind.TrustedDefault, result.Destination.Kind);
        Assert.Empty(db.RecoveryEmailPreferences);
        Preference(db, user.Id).TrySelect(RecoveryEmailSelectionMode.UseCustom, 1, Now);
        await db.SaveChangesAsync();
        Assert.Equal(RecoveryDestinationFailure.InvalidCustom, (await Resolver(db).ResolveAsync(user.Id)).Failure);
    }

    [Fact]
    public async Task ResolveAsync_CustomLookupFailure_IsNotAbsenceAndDoesNotReadDefault()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var setup = new ApplicationDbContext(options);
        var user = User(setup);
        await setup.SaveChangesAsync();
        await using var db = new FailingCustomLookupContext(options);
        var source = new Mock<IRecoveryDefaultDestinationEvaluator>(MockBehavior.Strict);
        var result = await Resolver(db, source.Object).ResolveAsync(user.Id);
        Assert.Equal(RecoveryDestinationFailure.LookupFailed, result.Failure);
        Assert.Null(result.Destination);
        source.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResolveAsync_DefaultLookupFailure_AndCallerCancellationFailClosed()
    {
        await using var db = Context();
        var user = User(db);
        await db.SaveChangesAsync();
        var source = new Mock<IRecoveryDefaultDestinationEvaluator>();
        source.Setup(evaluator => evaluator.EvaluateDefaultAsync(user.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("synthetic metadata failure"));
        Assert.Equal(RecoveryDestinationFailure.LookupFailed, (await Resolver(db, source.Object).ResolveAsync(user.Id)).Failure);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Resolver(db).ResolveAsync(user.Id, cancellation.Token));
    }

    [Fact]
    public async Task ResolveAsync_AccountIsolationAndBindingChangesAreExact()
    {
        await using var db = Context();
        var person = new Person { Id = Guid.NewGuid(), Status = PersonStatus.Active };
        db.Persons.Add(person);
        var user = User(db); user.PersonId = person.Id;
        var other = User(db); other.PersonId = person.Id;
        var active = Custom(db, user.Id);
        Custom(db, other.Id);
        var preference = Preference(db, user.Id);
        await db.SaveChangesAsync();
        var resolver = Resolver(db);
        var before = (await resolver.ResolveAsync(user.Id)).Destination!;
        var otherBefore = (await resolver.ResolveAsync(other.Id)).Destination!;
        Assert.False(before.Matches(otherBefore));
        active.TryReserveSend(Now, Now.AddMinutes(1));
        await db.SaveChangesAsync();
        Assert.True(before.Matches((await resolver.ResolveAsync(user.Id)).Destination!));
        active.ActivateVerifiedCustom("new@example.test", "NEW@EXAMPLE.TEST", Now, RecoveryEmailProvenance.UserVerified);
        preference.Advance(Now);
        await db.SaveChangesAsync();
        Assert.False(before.Matches((await resolver.ResolveAsync(user.Id)).Destination!));
        Assert.True(otherBefore.Matches((await resolver.ResolveAsync(other.Id)).Destination!));
    }

    [Fact]
    public async Task ResolveAsync_DefaultOrPolicyChangesInvalidateBinding()
    {
        await using var db = Context();
        var user = User(db);
        var snapshot = Snapshot(db, user.Id);
        await db.SaveChangesAsync();
        var policy = Policy();
        var resolver = Resolver(db, policy: policy);
        var before = (await resolver.ResolveAsync(user.Id)).Destination!;
        Assert.True(before.Matches((await resolver.ResolveAsync(user.Id)).Destination!));
        policy.CurrentPeriodId = "period-b";
        Assert.False(before.Matches((await resolver.ResolveAsync(user.Id)).Destination!));
        policy.CurrentPeriodId = "period-a";
        snapshot.Refresh("changed@example.test", ProviderEmailTrustOrigin.SourceVerified, null, Now);
        await db.SaveChangesAsync();
        Assert.False(before.Matches((await resolver.ResolveAsync(user.Id)).Destination!));
        Assert.Equal(64, before.Fingerprint.Length);
        Assert.Equal(64, before.EffectivePolicyDigest.Length);
    }

    [Fact]
    public async Task ResolveAsync_VerificationPolicyDisabled_DoesNotReadState()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().Options);
        var source = new Mock<IRecoveryDefaultDestinationEvaluator>(MockBehavior.Strict);
        Assert.Equal(RecoveryDestinationFailure.PolicyDisabled,
            (await Resolver(db, source.Object, new RecoveryVerificationPolicyOptions()).ResolveAsync(Guid.NewGuid())).Failure);
        source.VerifyNoOtherCalls();
    }

    [Fact]
    public void Options_DefaultsAreOffAndStepUpWindowIsBounded()
    {
        var options = new RecoveryEmailSelectionOptions();
        Assert.False(options.Enabled);
        Assert.False(options.SelfServiceEnabled);
        Assert.False(options.TrustedDefaultFallbackEnabled);
        Assert.Equal(5, options.RecentStepUpMinutes);
        var validator = new RecoveryEmailSelectionOptionsValidator();
        Assert.True(validator.Validate(null, options).Succeeded);
        options.Enabled = true;
        options.RecentStepUpMinutes = 0;
        Assert.False(validator.Validate(null, options).Succeeded);
        options.RecentStepUpMinutes = 6;
        Assert.False(validator.Validate(null, options).Succeeded);
    }

    private static ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ApplicationUser User(ApplicationDbContext db)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "contact@example.test", IsActive = true };
        db.Users.Add(user);
        return user;
    }

    private static RecoveryEmailRecord Custom(ApplicationDbContext db, Guid accountId,
        RecoveryEmailProvenance provenance = RecoveryEmailProvenance.UserVerified)
    {
        var record = new RecoveryEmailRecord(accountId, "custom@example.test", "CUSTOM@EXAMPLE.TEST", Now.AddDays(-2));
        if (provenance == RecoveryEmailProvenance.LegacyUnknown) record.MarkVerified(Now.AddDays(-1));
        else record.ActivateVerifiedCustom(record.Address, record.NormalizedAddress, Now.AddDays(-1), provenance);
        db.RecoveryEmails.Add(record);
        return record;
    }

    private static RecoveryEmailPreference Preference(ApplicationDbContext db, Guid accountId)
    {
        var preference = new RecoveryEmailPreference(accountId, Now.AddDays(-1));
        db.RecoveryEmailPreferences.Add(preference);
        return preference;
    }

    private static RecoveryEmailPendingChange Pending(Guid accountId, DateTimeOffset created) =>
        new(accountId, "pending@example.test", "PENDING@EXAMPLE.TEST", "synthetic-hash", 1, "stamp", "browser",
            "csrf", Guid.NewGuid(), created, created.AddHours(1), created.AddMinutes(1), 5);

    private static ProviderMetadataSnapshot Snapshot(ApplicationDbContext db, Guid accountId, string email = "source@example.test")
    {
        var binding = new ProviderSubjectDirectoryBinding(accountId, "example.provider", Guid.NewGuid().ToString(), Guid.NewGuid(), Now.UtcDateTime);
        db.ProviderSubjectDirectoryBindings.Add(binding);
        var snapshot = new ProviderMetadataSnapshot(binding.Id, Now.AddHours(-1));
        snapshot.Refresh(email, ProviderEmailTrustOrigin.SourceVerified, null, Now.AddHours(-1));
        db.ProviderMetadataSnapshots.Add(snapshot);
        return snapshot;
    }

    private static RecoveryVerificationPolicyOptions Policy() => new()
    {
        Enabled = true, CurrentPeriodId = "period-a", EffectiveAtUtc = Now.AddDays(-10),
        GraceEndsAtUtc = Now.AddDays(-5), AcceptSourceVerifiedEmails = true
    };

    private static RecoveryDestinationResolver Resolver(ApplicationDbContext db, IRecoveryDefaultDestinationEvaluator? source = null,
        RecoveryVerificationPolicyOptions? policy = null, RecoveryEmailSelectionOptions? selection = null)
    {
        var policyOptions = Options.Create(policy ?? Policy());
        return new(db, source ?? new RecoveryVerificationPolicyEvaluator(db, policyOptions, new Clock()),
            Options.Create(selection ?? new() { Enabled = true, TrustedDefaultFallbackEnabled = true }), policyOptions, new Clock());
    }

    private sealed class FailingCustomLookupContext(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        public override DbSet<TEntity> Set<TEntity>() => typeof(TEntity) == typeof(RecoveryEmailRecord)
            ? throw new InvalidOperationException("synthetic custom lookup failure")
            : base.Set<TEntity>();
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}
