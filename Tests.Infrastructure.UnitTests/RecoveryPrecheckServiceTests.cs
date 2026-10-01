using System.Diagnostics;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Interfaces;
using Core.Application.Options;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Core.Domain.Models;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class RecoveryPrecheckServiceTests
{
    private static readonly NativeRecoveryContext Browser = new("browser", "csrf", "budget");

    [Fact]
    public async Task PrepareAndExplicitSend_ShouldReserveOnceWithoutAuthenticationOrEvidencePersistence()
    {
        await using var f = await Fixture.CreateAsync(directory: true);
        var prepared = await f.PrepareAsync();
        Assert.NotNull(prepared.GrantId);
        Assert.Equal("  synthetic-id  ", f.Provider.Last!.Evidence.IdentityIdentifier);
        Assert.Equal("provider", f.Provider.Last.ProviderNamespace);
        Assert.Equal("subject", f.Provider.Last.StableSubject);
        Assert.Empty(await f.Db.RecoveryProofChallenges.ToListAsync());
        Assert.Equal(0, f.Mail.Count);
        Assert.Empty(await f.Db.UserSessions.ToListAsync());
        Assert.DoesNotContain("synthetic-id", prepared.ToString());
        Assert.DoesNotContain("synthetic-id", new RecoveryPrepareRequest("user", "synthetic-id", Browser, "127.0.0.1").ToString());
        var sent = await f.Service.SendOtpAsync(new(prepared.GrantId!.Value, Browser, "127.0.0.1"));
        var challenge = await f.Db.RecoveryProofChallenges.SingleAsync();
        Assert.Equal(sent.RequestId, challenge.Id);
        Assert.Equal(RecoveryChallengeDeliveryState.Delivered, challenge.DeliveryState);
        Assert.Equal(1, f.Mail.Count);
        Assert.Matches(@"\b[0-9]{6}\b", f.Mail.Body!);
        Assert.False(challenge.TryMarkNativeRecoveryVerified("unverified", f.Time.GetUtcNow().AddHours(1)));
        await using var second = f.NewContext();
        await f.CreateService(second).SendOtpAsync(new(prepared.GrantId.Value, Browser, "127.0.0.1"));
        Assert.Equal(1, f.Mail.Count);
        Assert.Single(await second.RecoveryProofChallenges.ToListAsync());
        Assert.Equal(0, (await second.Users.SingleAsync()).AccessFailedCount);
    }

    [Theory]
    [InlineData("context")]
    [InlineData("csrf")]
    [InlineData("expired")]
    [InlineData("stamp")]
    [InlineData("destination")]
    [InlineData("epoch")]
    [InlineData("policy")]
    [InlineData("inactive")]
    public async Task Send_ShouldRejectChangedGrantState(string mutation)
    {
        await using var f = await Fixture.CreateAsync();
        var prepared = await f.PrepareAsync();
        var context = Browser;
        switch (mutation)
        {
            case "context": context = Browser with { ContextHash = "other" }; break;
            case "csrf": context = Browser with { CsrfHash = "other" }; break;
            case "expired": f.Time.Now = f.Time.Now.AddMinutes(6); break;
            case "stamp": f.User.SecurityStamp = "changed"; break;
            case "destination": f.Destination = f.Destination with { Fingerprint = new string('B', 64) }; break;
            case "epoch": f.Destination = f.Destination with { SelectionEpoch = 2 }; break;
            case "policy": f.Identity.RequireForLocalAccounts = true; f.Identity.Enabled = true; break;
            case "inactive": f.User.IsActive = false; break;
        }
        await f.Db.SaveChangesAsync();
        await f.Service.SendOtpAsync(new(prepared.GrantId!.Value, context, "127.0.0.1"));
        Assert.Equal(0, f.Mail.Count);
        Assert.Empty(await f.Db.RecoveryProofChallenges.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Prepare_ShouldFailClosedForRequiredMissingBinding(bool incompleteDirectory)
    {
        await using var f = await Fixture.CreateAsync();
        f.Identity.Enabled = true;
        f.Identity.RequireForLocalAccounts = !incompleteDirectory;
        f.Identity.RequireForDirectoryAccounts = incompleteDirectory;
        if (incompleteDirectory)
        {
            var binding = new ProviderSubjectDirectoryBinding(f.User.Id, "provider", "subject", Guid.NewGuid(), f.Time.Now.UtcDateTime);
            f.Db.ProviderSubjectDirectoryBindings.Add(binding);
            f.Db.CredentialMigrationStateRecords.Add(new CredentialMigrationStateRecord(f.User.Id, binding.Id, f.Time.Now));
            await f.Db.SaveChangesAsync();
        }
        Assert.Null((await f.PrepareAsync()).GrantId);
        Assert.Null(f.Provider.Last);
        Assert.Empty(await f.Db.RecoveryPrecheckGrants.ToListAsync());
    }

    [Theory]
    [InlineData("stamp")]
    [InlineData("policy")]
    [InlineData("inactive")]
    [InlineData("denied")]
    [InlineData("tuple")]
    public async Task Prepare_ShouldRecheckAfterProvider(string mutation)
    {
        await using var f = await Fixture.CreateAsync(directory: true);
        f.Provider.After = async () =>
        {
            if (mutation == "stamp") f.User.SecurityStamp = "changed";
            if (mutation == "policy") f.Identity.RequireForDirectoryAccounts = false;
            if (mutation == "inactive") f.User.IsActive = false;
            await f.Db.SaveChangesAsync();
        };
        f.Provider.Denied = mutation == "denied";
        f.Provider.WrongTuple = mutation == "tuple";
        Assert.Null((await f.PrepareAsync()).GrantId);
        Assert.Empty(await f.Db.RecoveryPrecheckGrants.ToListAsync());
        Assert.Empty(await f.Db.RecoveryProofChallenges.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Send_ShouldLeaveFailureUnredeemableAndNeverFallback(bool missingSmtp)
    {
        await using var f = await Fixture.CreateAsync();
        f.Mail.Fail = !missingSmtp;
        if (missingSmtp) f.EmailOptions.SmtpHost = "";
        var prepared = await f.PrepareAsync();
        var result = await f.Service.SendOtpAsync(new(prepared.GrantId!.Value, Browser, "127.0.0.1"));
        var challenge = await f.Db.RecoveryProofChallenges.SingleAsync();
        Assert.NotEqual(challenge.Id, result.RequestId);
        Assert.Equal(RecoveryChallengeDeliveryState.Failed, challenge.DeliveryState);
        Assert.False(challenge.TryReserveAttempt(f.Time.Now, 5));
        Assert.False(challenge.TryConsumeWithAdministrativeApproval(f.Time.Now));
        Assert.Equal(missingSmtp ? 0 : 1, f.Mail.Count);
        Assert.Equal("custom@example.test", f.Destination.Address);
    }

    [Fact]
    public async Task SharedBudgets_ShouldRejectSixthBrowserAttemptWithoutChangingLoginLockout()
    {
        await using var f = await Fixture.CreateAsync();
        for (var i = 0; i < 5; i++)
        {
            await using var next = f.NewContext();
            Assert.NotNull((await f.CreateService(next).PrepareAsync(new("user", "", Browser, "127.0.0.1"))).GrantId);
        }
        Assert.Null((await f.PrepareAsync()).GrantId);
        Assert.Equal(0, (await f.Db.Users.AsNoTracking().SingleAsync()).AccessFailedCount);
        var buckets = await f.Db.RecoveryThrottleBuckets.ToListAsync();
        Assert.All(buckets, b => Assert.Matches("^[0-9A-F]{64}$", b.PartitionHash));
    }

    [Fact]
    public async Task ConcurrentSend_ShouldAllowAtMostOneReservationAcrossInstances()
    {
        await using var f = await Fixture.CreateAsync();
        var grant = (await f.PrepareAsync()).GrantId!.Value;
        async Task Send()
        {
            await using var db = f.NewContext();
            await f.CreateService(db).SendOtpAsync(new(grant, Browser, "127.0.0.1"));
        }
        await Task.WhenAll(Task.Run(Send), Task.Run(Send));
        await using var check = f.NewContext();
        Assert.Single(await check.RecoveryProofChallenges.ToListAsync());
        Assert.Equal(1, f.Mail.Count);
        Assert.NotNull((await check.RecoveryPrecheckGrants.SingleAsync()).ConsumedAtUtc);
    }

    [Fact]
    public async Task LegacyStart_ShouldRejectEnabledCeremonyBeforeSending()
    {
        await using var f = await Fixture.CreateAsync();
        var mail = new Mock<IEmailService>(MockBehavior.Strict);
        var policy = new Mock<IRecoveryVerificationPolicyEvaluator>(MockBehavior.Strict);
        var options = Options.Create(new ForgotPasswordRecoveryOptions { NativeRecoveryEnabled = true, DeploymentCeiling = ForgotPasswordMode.Native });
        var service = new NativePasswordRecoveryProofService(f.Db, mail.Object, new PasswordHasher<ApplicationUser>(),
            new UpperInvariantLookupNormalizer(), policy.Object, new ForgotPasswordRoutingEvaluator(options), options, f.Time,
            Options.Create(new RecoveryIdentityVerificationOptions { Enabled = true, RequireForLocalAccounts = true }));
        await service.StartAsync(new("user", Browser));
        Assert.Empty(await f.Db.RecoveryProofChallenges.ToListAsync());
        mail.VerifyNoOtherCalls();
        policy.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("ip", 20)]
    [InlineData("subject", 5)]
    public async Task Throttle_ShouldSharePartitionAcrossInstances(string dimension, int limit)
    {
        await using var f = await Fixture.CreateAsync();
        for (var i = 0; i <= limit; i++)
        {
            await using var db = f.NewContext();
            var throttle = new RecoveryThrottleService(db, Options.Create(new RecoveryThrottleOptions { HashKey = f.ThrottleKey }), f.Time);
            Assert.Equal(i < limit, await throttle.TakeAsync("prepare", dimension, "synthetic-internal-value", limit, TimeSpan.FromMinutes(15), default));
        }
    }

    [Fact]
    public async Task DefaultSend_ShouldNotCreateOrPromoteCustomPreference()
    {
        await using var f = await Fixture.CreateAsync();
        f.Destination = f.Destination with { Kind = RecoveryDestinationKind.TrustedDefault, RecoveryEmailId = null, Address = "source@example.test" };
        var prepared = await f.PrepareAsync();
        await f.Service.SendOtpAsync(new(prepared.GrantId!.Value, Browser, "127.0.0.1"));
        var challenge = await f.Db.RecoveryProofChallenges.SingleAsync();
        Assert.Null(challenge.RecoveryEmailId);
        Assert.Empty(await f.Db.RecoveryEmailPreferences.ToListAsync());
        Assert.Single(await f.Db.RecoveryEmails.ToListAsync());
        Assert.Equal("source@example.test", f.Mail.To);
    }

    [Fact]
    public async Task PrepareFailures_ShouldHaveSameShapeAndBoundedTiming()
    {
        await using var f = await Fixture.CreateAsync();
        var durations = new List<double>();
        foreach (var identifier in new[] { "missing", "user" })
        {
            f.User.IsActive = false;
            await f.Db.SaveChangesAsync();
            var stopwatch = Stopwatch.StartNew();
            var result = await f.Service.PrepareAsync(new(identifier, "synthetic", Browser, "127.0.0.1"));
            durations.Add(stopwatch.Elapsed.TotalMilliseconds);
            Assert.Equal(new RecoveryPrepareResult(), result);
        }
        Assert.All(durations, duration => Assert.InRange(duration, 100, 3000));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new($"Data Source=recovery-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=2");
        public ApplicationDbContext Db { get; private set; } = null!;
        public ApplicationUser User { get; private set; } = null!;
        public RecoveryDestination Destination { get; set; } = null!;
        public Clock Time { get; } = new();
        public Provider Provider { get; } = new();
        public Mail Mail { get; } = new();
        public EmailOptions EmailOptions { get; } = new() { SmtpHost = "fake-only" };
        public RecoveryIdentityVerificationOptions Identity { get; } = new();
        public string ThrottleKey { get; } = Guid.NewGuid().ToString("N");
        public RecoveryPrecheckService Service => CreateService(Db);
        public ApplicationDbContext NewContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection.ConnectionString).Options);
        public Task<RecoveryPrepareResult> PrepareAsync() => Service.PrepareAsync(new("user", "  synthetic-id  ", Browser, "127.0.0.1"));

        public RecoveryPrecheckService CreateService(ApplicationDbContext db)
        {
            var destination = new Mock<IRecoveryDestinationResolver>();
            destination.Setup(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new RecoveryDestinationResult(RecoveryDestinationFailure.None, Destination));
            var mailOptions = new Mock<IOptionsSnapshot<EmailOptions>>();
            mailOptions.SetupGet(o => o.Value).Returns(EmailOptions);
            var native = Options.Create(new ForgotPasswordRecoveryOptions { NativeRecoveryEnabled = true,
                NativeDirectoryRecoveryEnabled = true, DeploymentCeiling = ForgotPasswordMode.Native });
            return new(db, destination.Object, Provider,
                new RecoveryThrottleService(db, Options.Create(new RecoveryThrottleOptions { HashKey = ThrottleKey }), Time),
                new RecoveryOtpDeliveryService(Mail, mailOptions.Object), new PasswordHasher<ApplicationUser>(),
                new UpperInvariantLookupNormalizer(), new ForgotPasswordRoutingEvaluator(native), Options.Create(Identity),
                Options.Create(new RecoveryEmailSelectionOptions { Enabled = true }), native, Time);
        }

        public static async Task<Fixture> CreateAsync(bool directory = false)
        {
            var f = new Fixture();
            await f._connection.OpenAsync();
            f.Db = f.NewContext();
            await f.Db.Database.EnsureCreatedAsync();
            f.User = new ApplicationUser { Id = Guid.NewGuid(), UserName = "user", NormalizedUserName = "USER",
                IsActive = true, PasswordHash = "local-hash", SecurityStamp = "stamp" };
            f.Db.Users.Add(f.User);
            f.Db.SecurityPolicies.Add(new SecurityPolicy { ForgotPasswordMode = ForgotPasswordMode.Native });
            var email = new RecoveryEmailRecord(f.User.Id, "custom@example.test", "CUSTOM@EXAMPLE.TEST", f.Time.Now);
            email.MarkVerified(f.Time.Now);
            f.Db.RecoveryEmails.Add(email);
            f.Destination = new(f.User.Id, email.Address, "c***@example.test", RecoveryDestinationKind.Legacy,
                new string('A', 64), f.Time.Now.UtcTicks, 1, new string('C', 64), email.Id);
            if (directory)
            {
                var binding = new ProviderSubjectDirectoryBinding(f.User.Id, "provider", "subject", Guid.NewGuid(), f.Time.Now.UtcDateTime);
                var migration = new CredentialMigrationStateRecord(f.User.Id, binding.Id, f.Time.Now);
                migration.Advance(CredentialMigrationState.ProofValidated, EffectiveEmailOtpRequirement.NotRequired, f.Time.Now);
                migration.Advance(CredentialMigrationState.DirectoryCredentialCommitted, f.Time.Now);
                migration.Advance(CredentialMigrationState.LocalFinalized, f.Time.Now);
                f.Db.ProviderSubjectDirectoryBindings.Add(binding);
                f.Db.CredentialMigrationStateRecords.Add(migration);
                f.Identity.Enabled = true;
                f.Identity.RequireForDirectoryAccounts = true;
            }
            await f.Db.SaveChangesAsync();
            return f;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Provider : IRecoveryIdentityVerificationClient
    {
        public RecoveryIdentityVerificationRequest? Last { get; private set; }
        public Func<Task>? After { get; set; }
        public bool Denied { get; set; }
        public bool WrongTuple { get; set; }
        public async Task<RecoveryIdentityVerificationResponse?> VerifyAsync(RecoveryIdentityVerificationRequest request, CancellationToken cancellationToken = default)
        {
            Last = request;
            if (After is not null) await After();
            return new() { RequestId = request.RequestId, Outcome = Denied ? RecoveryIdentityVerificationOutcome.Denied : RecoveryIdentityVerificationOutcome.Verified,
                Binding = Denied ? null : new() { ProviderNamespace = request.ProviderNamespace, StableSubject = WrongTuple ? "wrong" : request.StableSubject } };
        }
    }
    private sealed class Mail : IEmailDispatcher
    {
        public bool Fail { get; set; }
        public int Count { get; private set; }
        public string? To { get; private set; }
        public string? Body { get; private set; }
        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Count++; To = message.To; Body = message.Body;
            if (Fail) throw new InvalidOperationException("synthetic smtp failure");
            return Task.CompletedTask;
        }
        public Task SendTestAsync(EmailMessage message, MailSettingsDto settings, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
