using System.Text.RegularExpressions;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Interfaces;
using Core.Application.Options;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Models;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class RecoveryEmailPreferenceServiceTests
{
    [Theory]
    [InlineData(RecoveryEmailSelectionMode.UseDefault, false)]
    [InlineData(RecoveryEmailSelectionMode.UseDefault, true)]
    [InlineData(RecoveryEmailSelectionMode.Disabled, false)]
    public async Task GetStatus_ReadOnlyPersistedModeNeverDisplaysDeselectedCustom(RecoveryEmailSelectionMode mode, bool bootstrap)
    {
        await using var f = await Fixture.CreateAsync();
        var context = await f.GrantAsync();
        Assert.Equal(RecoveryProofOutcome.Success, await f.Service.BeginAsync(f.User.Id, "pending@example.test", context));
        var preference = await f.Db.RecoveryEmailPreferences.SingleAsync();
        Assert.True(preference.TrySelect(mode, preference.SelectionEpoch, f.Time.Now));
        await f.Db.SaveChangesAsync();
        f.OptionsValue.Enabled = false;
        f.OptionsValue.SelfServiceEnabled = false;
        f.OptionsValue.TrustedDefaultFallbackEnabled = false;
        f.Default = f.Default! with { BootstrapActive = bootstrap };

        var status = await f.Service.GetStatusAsync(f.User.Id);

        Assert.False(status.Enabled);
        Assert.Equal(mode.ToString(), status.Mode);
        Assert.Equal(mode == RecoveryEmailSelectionMode.UseDefault && bootstrap ? "d***@example.test" : null, status.MaskedAddress);
        Assert.Equal(status.MaskedAddress, status.MaskedDefaultAddress);
        Assert.Equal("p***@example.test", status.MaskedPendingAddress);
        Assert.NotNull(status.PendingExpiresAtUtc);
        Assert.Equal("old@example.test", (await f.Db.RecoveryEmails.SingleAsync()).Address);
        Assert.Equal(RecoveryProofOutcome.Unavailable, await f.Service.BeginAsync(f.User.Id, "another@example.test", context));
        Assert.Single(f.Mail.Messages);
    }

    [Fact]
    public async Task GetStatus_ReadOnlyPolicyRetainsAvailableDefaultAndCurrentCustom()
    {
        await using var f = await Fixture.CreateAsync();
        f.OptionsValue.SelfServiceEnabled = false;

        var status = await f.Service.GetStatusAsync(f.User.Id);

        Assert.False(status.Enabled);
        Assert.Equal("Legacy", status.Mode);
        Assert.Equal("o***@example.test", status.MaskedAddress);
        Assert.Equal("d***@example.test", status.MaskedDefaultAddress);
        Assert.Empty(await f.Db.RecoveryEmailPreferences.ToListAsync());
        Assert.Empty(f.Mail.Messages);
    }

    [Fact]
    public async Task GetStatus_NoSelectionStateAndFlagsOffReturnsLegacySentinel()
    {
        await using var f = await Fixture.CreateAsync();
        f.OptionsValue.Enabled = false;
        f.OptionsValue.SelfServiceEnabled = false;
        f.OptionsValue.TrustedDefaultFallbackEnabled = false;

        var status = await f.Service.GetStatusAsync(f.User.Id);

        Assert.False(status.Enabled);
        Assert.Equal("unavailable", status.Mode);
        Assert.Null(status.MaskedAddress);
        Assert.Null(status.MaskedDefaultAddress);
        Assert.Empty(await f.Db.RecoveryEmailPreferences.ToListAsync());
    }

    [Fact]
    public async Task BeginAndCancel_PreserveActiveAndOtherAccount()
    {
        await using var f = await Fixture.CreateAsync();
        var ctx = await f.GrantAsync();
        Assert.Equal(RecoveryProofOutcome.Success, await f.Service.BeginAsync(f.User.Id, "new@example.test", ctx));
        Assert.Equal("old@example.test", (await f.Db.RecoveryEmails.SingleAsync()).Address);
        Assert.Equal(1, (await f.Db.RecoveryEmailPreferences.SingleAsync()).SelectionEpoch);
        var other = new ApplicationUser { Id = Guid.NewGuid(), PasswordHash = "hash", SecurityStamp = "other" };
        f.Db.Users.Add(other); await f.Db.SaveChangesAsync();
        Assert.Equal(RecoveryProofOutcome.Unauthorized, await f.Service.CancelAsync(other.Id, ctx));
        Assert.Null((await f.Db.RecoveryEmailChangeRequests.SingleAsync()).RevokedAtUtc);
        Assert.Equal(RecoveryProofOutcome.Success, await f.Service.CancelAsync(f.User.Id, ctx));
        Assert.Equal("old@example.test", (await f.Db.RecoveryEmails.SingleAsync()).Address);
    }

    [Fact]
    public async Task Begin_WithoutCustom_KeepsDefaultEffectiveUntilVerification()
    {
        await using var f = await Fixture.CreateAsync();
        f.Db.RecoveryEmails.RemoveRange(f.Db.RecoveryEmails); await f.Db.SaveChangesAsync();
        var ctx = await f.GrantAsync();
        Assert.Equal(RecoveryProofOutcome.Success, await f.Service.BeginAsync(f.User.Id, "new@example.test", ctx));
        Assert.Equal("d***@example.test", (await f.Service.GetStatusAsync(f.User.Id)).MaskedAddress);
        Assert.Empty(await f.Db.RecoveryEmails.ToListAsync());
    }

    [Fact]
    public async Task Verify_PromotesExactlyOnceAndRevokesPriorProofs_WithDurableNotifications()
    {
        await using var f = await Fixture.CreateAsync();
        var ctx = await f.GrantAsync();
        var email = await f.Db.RecoveryEmails.SingleAsync();
        var challenge = new RecoveryProofChallenge(email.Id, f.User.Id, RecoveryProofPurpose.NativePasswordRecovery,
            "hash", f.Time.Now, f.Time.Now.AddMinutes(5));
        f.Db.RecoveryProofChallenges.Add(challenge);
        f.Db.RecoveryPrecheckGrants.Add(new RecoveryPrecheckGrant(f.User.Id, null, null, "stamp", "browser", "csrf", "policy",
            1, RecoveryDestinationKind.Legacy, "destination", 1, f.Time.Now, f.Time.Now.AddMinutes(5)));
        f.Db.NativeRecoveryResetApprovals.Add(new NativeRecoveryResetApproval(challenge.Id, f.User.Id, email.Id, email.Version,
            f.User.Id, "browser", "csrf", false, null, "stamp", "synthetic", "synthetic", f.Time.Now, f.Time.Now.AddMinutes(5)));
        await f.Db.SaveChangesAsync();
        Assert.Equal(RecoveryProofOutcome.Success, await f.Service.BeginAsync(f.User.Id, " New@example.test ", ctx));
        var code = f.Mail.Code;
        Assert.Equal(RecoveryProofOutcome.Success, await f.Service.VerifyAsync(f.User.Id, code, ctx));
        Assert.NotEqual(RecoveryProofOutcome.Success, await f.Service.VerifyAsync(f.User.Id, code, ctx));
        f.Db.ChangeTracker.Clear();
        Assert.Equal("New@example.test", (await f.Db.RecoveryEmails.SingleAsync()).Address);
        Assert.Equal(RecoveryEmailProvenance.UserVerified, (await f.Db.RecoveryEmails.SingleAsync()).Provenance);
        Assert.Equal(2, (await f.Db.RecoveryEmailPreferences.SingleAsync()).SelectionEpoch);
        Assert.NotNull((await f.Db.RecoveryPrecheckGrants.SingleAsync()).RevokedAtUtc);
        Assert.NotNull((await f.Db.RecoveryProofChallenges.SingleAsync()).RevokedAtUtc);
        Assert.NotNull((await f.Db.NativeRecoveryResetApprovals.SingleAsync()).RevokedAtUtc);
        Assert.Equal(2, await f.Db.RecoveryNotifications.CountAsync());
        Assert.Single(f.Mail.Messages);
    }

    [Theory]
    [InlineData("csrf")]
    [InlineData("browser")]
    [InlineData("stamp")]
    [InlineData("stale")]
    [InlineData("mfa")]
    [InlineData("authority")]
    [InlineData("epoch")]
    [InlineData("requiredChange")]
    public async Task Verify_RejectsChangedSecurityState(string change)
    {
        await using var f = await Fixture.CreateAsync();
        var ctx = await f.GrantAsync();
        Assert.Equal(RecoveryProofOutcome.Success, await f.Service.BeginAsync(f.User.Id, "new@example.test", ctx));
        var code = f.Mail.Code;
        switch (change)
        {
            case "csrf": ctx = ctx with { CsrfHash = "wrong" }; break;
            case "browser": ctx = ctx with { ContextHash = "wrong" }; break;
            case "stamp": f.User.SecurityStamp = "changed"; break;
            case "stale": f.Time.Now = f.Time.Now.AddMinutes(6); break;
            case "mfa": f.Authorized = false; break;
            case "authority": f.Db.ProviderSubjectDirectoryBindings.Add(new ProviderSubjectDirectoryBinding(f.User.Id, "p", "s", Guid.NewGuid(), f.Time.Now.UtcDateTime)); break;
            case "epoch": (await f.Db.RecoveryEmailPreferences.SingleAsync()).Advance(f.Time.Now); break;
            case "requiredChange": f.User.RequiresPasswordChange = true; break;
        }
        await f.Db.SaveChangesAsync();
        Assert.NotEqual(RecoveryProofOutcome.Success, await f.Service.VerifyAsync(f.User.Id, code, ctx));
        Assert.Equal("old@example.test", (await f.Db.RecoveryEmails.SingleAsync()).Address);
        Assert.Empty(await f.Db.RecoveryNotifications.ToListAsync());
    }

    [Fact]
    public async Task Resend_PreservesExpiryAndAttempts_AndOldCodeFails()
    {
        await using var f = await Fixture.CreateAsync(); var ctx = await f.GrantAsync();
        Assert.Equal(RecoveryProofOutcome.Success, await f.Service.BeginAsync(f.User.Id, "new@example.test", ctx));
        var oldCode = f.Mail.Code;
        Assert.Equal(RecoveryProofOutcome.Invalid, await f.Service.VerifyAsync(f.User.Id, "notOTP", ctx));
        var before = await f.Db.RecoveryEmailChangeRequests.SingleAsync(); var expiry = before.ExpiresAtUtc;
        Assert.Equal(RecoveryProofOutcome.Cooldown, await f.Service.ResendAsync(f.User.Id, ctx));
        f.Time.Now = f.Time.Now.AddMinutes(1);
        Assert.Equal(RecoveryProofOutcome.Success, await f.Service.ResendAsync(f.User.Id, ctx));
        Assert.Equal(expiry, before.ExpiresAtUtc); Assert.Equal(1, before.VerificationAttempts);
        Assert.Equal(RecoveryProofOutcome.Invalid, await f.Service.VerifyAsync(f.User.Id, oldCode, ctx));
    }

    [Fact]
    public async Task SendFailureAndReplacement_KeepOldEffectiveDestination()
    {
        await using var f = await Fixture.CreateAsync(); var ctx = await f.GrantAsync();
        f.Mail.Fail = true;
        Assert.Equal(RecoveryProofOutcome.Unavailable, await f.Service.BeginAsync(f.User.Id, "first@example.test", ctx));
        Assert.NotNull((await f.Db.RecoveryEmailChangeRequests.SingleAsync()).RevokedAtUtc);
        f.Mail.Fail = false; ctx = await f.GrantAsync();
        Assert.Equal(RecoveryProofOutcome.Success, await f.Service.BeginAsync(f.User.Id, "second@example.test", ctx));
        Assert.Equal("old@example.test", (await f.Db.RecoveryEmails.SingleAsync()).Address);
        Assert.Equal(2, await f.Db.RecoveryEmailChangeRequests.CountAsync());
        Assert.Equal(RecoveryProofOutcome.Unauthorized, await f.Service.BeginAsync(f.User.Id, "third@example.test", ctx));
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("missing")]
    [InlineData("stale")]
    [InlineData("other")]
    [InlineData("csrf")]
    public async Task UseDefault_RejectsStaleOrUnboundConfirmation(string change)
    {
        await using var f = await Fixture.CreateAsync(); var ctx = await f.GrantAsync();
        var confirmation = await f.Service.PrepareDefaultAsync(f.User.Id, ctx); Assert.NotNull(confirmation);
        switch (change)
        {
            case "changed": f.Default = f.Default! with { Fingerprint = "changed", Address = "changed@example.test" }; break;
            case "missing": f.Default = null; break;
            case "stale": f.Time.Now = f.Time.Now.AddMinutes(6); break;
            case "other": ctx = ctx with { StepUpGrantId = Guid.NewGuid() }; break;
            case "csrf": ctx = ctx with { CsrfHash = "wrong" }; break;
        }
        Assert.NotEqual(RecoveryProofOutcome.Success, await f.Service.UseDefaultAsync(f.User.Id, confirmation.Confirmation, ctx));
        Assert.Equal("old@example.test", (await f.Db.RecoveryEmails.SingleAsync()).Address);
        Assert.Empty(await f.Db.RecoveryNotifications.ToListAsync());
    }

    [Fact]
    public async Task UseDefault_ConsumesFreshGrantAndRetainsHistoricalRecord()
    {
        await using var f = await Fixture.CreateAsync(); var ctx = await f.GrantAsync();
        var confirmation = await f.Service.PrepareDefaultAsync(f.User.Id, ctx); Assert.NotNull(confirmation);
        Assert.DoesNotContain("default@example.test", confirmation.Confirmation);
        Assert.Equal(RecoveryProofOutcome.Success, await f.Service.UseDefaultAsync(f.User.Id, confirmation.Confirmation, ctx));
        Assert.NotEqual(RecoveryProofOutcome.Success, await f.Service.UseDefaultAsync(f.User.Id, confirmation.Confirmation, ctx));
        Assert.Equal(RecoveryEmailSelectionMode.UseDefault, (await f.Db.RecoveryEmailPreferences.SingleAsync()).Mode);
        Assert.Equal(2, (await f.Db.RecoveryEmailPreferences.SingleAsync()).SelectionEpoch);
        Assert.Equal("old@example.test", (await f.Db.RecoveryEmails.SingleAsync()).Address);
    }

    [Fact]
    public async Task ConcurrentVerify_IndependentContextsYieldOneWinner()
    {
        await using var f = await Fixture.CreateAsync(); var ctx = await f.GrantAsync();
        Assert.Equal(RecoveryProofOutcome.Success, await f.Service.BeginAsync(f.User.Id, "new@example.test", ctx));
        var code = f.Mail.Code;
        async Task<RecoveryProofOutcome> Verify()
        {
            await using var db = f.NewContext();
            return await f.CreateService(db).VerifyAsync(f.User.Id, code, ctx);
        }
        var outcomes = await Task.WhenAll(Task.Run(Verify), Task.Run(Verify));
        Assert.Single(outcomes, o => o == RecoveryProofOutcome.Success);
        await using var read = f.NewContext();
        Assert.Equal(2, (await read.RecoveryEmailPreferences.SingleAsync()).SelectionEpoch);
        Assert.Equal(2, await read.RecoveryNotifications.CountAsync());
    }

    [Fact]
    public async Task Notifications_RestartRecoversLeaseAndBoundsRetriesWithoutOtp()
    {
        await using var f = await Fixture.CreateAsync();
        var row = new RecoveryNotification(f.User.Id, 1, RecoveryNotificationKind.DefaultSelected,
            "notice@example.test", f.Time.Now, f.Time.Now.AddDays(1), 2);
        row.TryClaim(Guid.NewGuid(), f.Time.Now, f.Time.Now.AddSeconds(30));
        f.Db.RecoveryNotifications.Add(row); await f.Db.SaveChangesAsync();
        f.Time.Now = f.Time.Now.AddMinutes(1); f.Mail.Fail = true;
        await using (var restarted = f.NewContext()) await f.Notifications(restarted).ProcessBatchAsync(default);
        f.Db.ChangeTracker.Clear();
        Assert.NotNull((await f.Db.RecoveryNotifications.SingleAsync()).AbandonedAtUtc);
        f.Mail.Fail = false;
        await f.Notifications(f.Db).ProcessBatchAsync(default);
        Assert.Single(f.Mail.Messages);
        Assert.DoesNotMatch("[0-9]{6}", f.Mail.Messages.Single().Body);
    }

    [Fact]
    public async Task Cancel_RequiresFreshAccountProof_EvenForExpiredPending()
    {
        await using var f = await Fixture.CreateAsync(); var ctx = await f.GrantAsync();
        Assert.Equal(RecoveryProofOutcome.Success, await f.Service.BeginAsync(f.User.Id, "new@example.test", ctx));
        f.Time.Now = f.Time.Now.AddMinutes(11);
        Assert.Equal(RecoveryProofOutcome.Unauthorized, await f.Service.CancelAsync(f.User.Id, ctx));
        ctx = await f.GrantAsync();
        Assert.Equal(RecoveryProofOutcome.Success, await f.Service.CancelAsync(f.User.Id, ctx));
        Assert.Equal("old@example.test", (await f.Db.RecoveryEmails.SingleAsync()).Address);
    }

    [Fact]
    public async Task Notifications_ExpiredAndExhaustedRowsBecomeTerminal_ThenLaterIntentDelivers()
    {
        await using var f = await Fixture.CreateAsync();
        for (var i = 0; i < 101; i++)
            f.Db.RecoveryNotifications.Add(new RecoveryNotification(f.User.Id, i + 1, RecoveryNotificationKind.DefaultSelected,
                "expired@example.test", f.Time.Now.AddDays(-2), f.Time.Now.AddDays(-1), 2));
        var live = new RecoveryNotification(f.User.Id, 102, RecoveryNotificationKind.DefaultSelected,
            "live@example.test", f.Time.Now, f.Time.Now.AddDays(1), 2);
        f.Db.RecoveryNotifications.Add(live); await f.Db.SaveChangesAsync();
        await f.Notifications(f.Db).ProcessBatchAsync(default);
        await f.Notifications(f.Db).ProcessBatchAsync(default);
        Assert.Equal(101, await f.Db.RecoveryNotifications.CountAsync(n => n.AbandonedAtUtc != null));
        Assert.NotNull(live.DeliveredAtUtc);
        Assert.Single(f.Mail.Messages);
        Assert.Equal("live@example.test", f.Mail.Messages[0].To);
    }

    [Fact]
    public async Task SamePersonOtherAccount_ProofAndDestinationRemainIsolated()
    {
        await using var f = await Fixture.CreateAsync();
        var person = new Person { Id = Guid.NewGuid(), Status = Core.Domain.Enums.PersonStatus.Active };
        f.Db.Persons.Add(person); f.User.PersonId = person.Id;
        var other = new ApplicationUser { Id = Guid.NewGuid(), PersonId = person.Id, PasswordHash = "hash", SecurityStamp = "other" };
        f.Db.Users.Add(other);
        var email = new RecoveryEmailRecord(other.Id, "other@example.test", "OTHER@EXAMPLE.TEST", f.Time.Now);
        email.MarkVerified(f.Time.Now); f.Db.RecoveryEmails.Add(email); await f.Db.SaveChangesAsync();
        var ctx = await f.GrantAsync();
        Assert.Equal(RecoveryProofOutcome.Success, await f.Service.BeginAsync(f.User.Id, "new@example.test", ctx));
        var code = f.Mail.Code;
        Assert.Equal(RecoveryProofOutcome.Unauthorized, await f.Service.VerifyAsync(other.Id, code, ctx));
        Assert.Equal(RecoveryProofOutcome.Success, await f.Service.VerifyAsync(f.User.Id, code, ctx));
        Assert.Equal("other@example.test", (await f.Db.RecoveryEmails.SingleAsync(e => e.LocalAccountId == other.Id)).Address);
        Assert.False(await f.Db.RecoveryEmailPreferences.AnyAsync(p => p.LocalAccountId == other.Id));
    }

    [Fact]
    public async Task SelectionStatePreventsLegacyMutationsEvenWhenFeatureDisabled()
    {
        await using var f = await Fixture.CreateAsync();
        f.Db.RecoveryEmailPreferences.Add(new RecoveryEmailPreference(f.User.Id, f.Time.Now)); await f.Db.SaveChangesAsync();
        var legacy = new RecoveryEmailService(f.Db, Mock.Of<IRecoveryProofAuthorizer>(), Mock.Of<IEmailService>(),
            new PasswordHasher<ApplicationUser>(), Mock.Of<IRecoveryProofAudit>(), Options.Create(new CredentialMigrationOptions { RecoveryEmailEnabled = true }));
        Assert.Equal(RecoveryProofOutcome.Unavailable, (await legacy.BeginAuthenticatedChangeAsync(new(f.User.Id, "new@example.test"))).Outcome);
        Assert.Equal(RecoveryProofOutcome.Unavailable, await legacy.VerifyAuthenticatedAsync(new(f.User.Id, "123456")));
        Assert.Equal(RecoveryProofOutcome.Unavailable, await legacy.RevokeAuthenticatedAsync(f.User.Id));
        Assert.Equal("old@example.test", (await f.Db.RecoveryEmails.SingleAsync()).Address);
    }

    [Fact]
    public async Task Verify_RevokesExistingMigrationApprovalWithoutChangingMigrationState()
    {
        await using var f = await Fixture.CreateAsync();
        var binding = new ProviderSubjectDirectoryBinding(f.User.Id, "p", "s", Guid.NewGuid(), f.Time.Now.UtcDateTime);
        var migration = new CredentialMigrationStateRecord(f.User.Id, binding.Id, f.Time.Now);
        migration.Advance(CredentialMigrationState.ProofValidated, EffectiveEmailOtpRequirement.NotRequired, f.Time.Now);
        migration.Advance(CredentialMigrationState.DirectoryCredentialCommitted, f.Time.Now);
        migration.Advance(CredentialMigrationState.LocalFinalized, f.Time.Now);
        var continuation = new CredentialMigrationContinuationRecord(migration.Id, "token", "browser", "csrf", f.Time.Now, f.Time.Now.AddMinutes(10));
        f.Db.ProviderSubjectDirectoryBindings.Add(binding); f.Db.CredentialMigrationStateRecords.Add(migration);
        f.Db.CredentialMigrationContinuations.Add(continuation);
        var approval = new RecoveryResetApproval(f.User.Id, continuation.Id, f.User.Id, "synthetic", "synthetic", "hash", f.Time.Now, f.Time.Now.AddMinutes(10));
        f.Db.RecoveryResetApprovals.Add(approval); await f.Db.SaveChangesAsync();
        var context = await f.GrantAsync();
        Assert.Equal(RecoveryProofOutcome.Success, await f.Service.BeginAsync(f.User.Id, "new@example.test", context));
        Assert.Equal(RecoveryProofOutcome.Success, await f.Service.VerifyAsync(f.User.Id, f.Mail.Code, context));
        Assert.NotNull(approval.RevokedAtUtc);
        Assert.Equal(CredentialMigrationState.LocalFinalized, migration.State);
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new($"Data Source=preference-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=2");
        public ApplicationDbContext Db { get; private set; } = null!;
        public ApplicationUser User { get; private set; } = null!;
        public Clock Time { get; } = new();
        public Mail Mail { get; } = new();
        public bool Authorized { get; set; } = true;
        public RecoveryDefaultDestination? Default { get; set; } = new("default@example.test", "default-fingerprint", 1, false);
        public RecoveryEmailSelectionOptions OptionsValue { get; } = new() { Enabled = true, SelfServiceEnabled = true, TrustedDefaultFallbackEnabled = true };
        private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();
        private readonly string _throttleKey = Guid.NewGuid().ToString("N");
        public ApplicationDbContext NewContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection.ConnectionString).Options);
        public RecoveryEmailStepUpService StepUp(ApplicationDbContext db) => new(db, Options.Create(OptionsValue), Time);
        public RecoveryEmailPreferenceService Service => CreateService(Db);
        public RecoveryNotificationService Notifications(ApplicationDbContext db)
        {
            var options = new Mock<IOptionsSnapshot<EmailOptions>>(); options.SetupGet(o => o.Value).Returns(new EmailOptions { SmtpHost = "synthetic.invalid" });
            return new(db, Mail, options.Object, Time);
        }
        public RecoveryEmailPreferenceService CreateService(ApplicationDbContext db)
        {
            var auth = new Mock<IRecoveryProofAuthorizer>(); auth.Setup(a => a.IsSelfServiceAuthorizedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Authorized);
            var defaults = new Mock<IRecoveryDefaultDestinationEvaluator>(); defaults.Setup(d => d.EvaluateDefaultAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Default);
            var resolver = new RecoveryDestinationResolver(db, defaults.Object, Options.Create(OptionsValue), Options.Create(new RecoveryVerificationPolicyOptions { Enabled = true }), Time);
            return new(db, auth.Object, StepUp(db), resolver, defaults.Object, Notifications(db), new PasswordHasher<ApplicationUser>(), _protection,
                Options.Create(OptionsValue), Options.Create(new CredentialMigrationOptions()),
                new RecoveryThrottleService(db, Options.Create(new RecoveryThrottleOptions { HashKey = _throttleKey }), Time), Time);
        }
        public async Task<RecoveryPreferenceContext> GrantAsync()
        {
            var service = StepUp(Db); var state = await service.ResolveAsync(User.Id); Assert.NotNull(state);
            var id = await service.IssueAsync(User.Id, User.SecurityStamp!, state.Binding, "browser", "csrf", Time.Now);
            Assert.NotNull(id); return new("browser", "csrf", id.Value);
        }
        public static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture(); await f._connection.OpenAsync(); f.Db = f.NewContext(); await f.Db.Database.EnsureCreatedAsync();
            f.User = new ApplicationUser { Id = Guid.NewGuid(), UserName = "synthetic", PasswordHash = "hash", SecurityStamp = "stamp", IsActive = true };
            f.Db.Users.Add(f.User);
            var email = new RecoveryEmailRecord(f.User.Id, "old@example.test", "OLD@EXAMPLE.TEST", f.Time.Now);
            email.MarkVerified(f.Time.Now); f.Db.RecoveryEmails.Add(email); await f.Db.SaveChangesAsync(); return f;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); }
    }
    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    internal sealed class Mail : IEmailDispatcher
    {
        public List<EmailMessage> Messages { get; } = [];
        public bool Fail { get; set; }
        public string Code => Regex.Match(Messages.Last().Body, "[0-9]{6}").Value;
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        { Messages.Add(message); if (Fail) throw new InvalidOperationException("synthetic"); return Task.CompletedTask; }
        public Task SendTestAsync(EmailMessage message, MailSettingsDto settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
