using Core.Application;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class NativePasswordRecoveryProofServiceTests
{
    [Fact]
    public async Task StartAndVerify_UsesContextBoundSingleProofWithoutSessionOrPasswordSideEffects()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        await SeedRuntimeModeAsync(context, ForgotPasswordMode.Native);
        var time = new FixedTimeProvider();
        var hasher = new PasswordHasher<ApplicationUser>();
        var user = await SeedEligibleUserAsync(context, hasher);
        var recoveryEmail = await SeedVerifiedRecoveryEmailAsync(context, user.Id, time);
        var email = new CapturingEmailService();
        var policy = new FixedPolicyEvaluator(recoveryEmail.Address);
        var service = CreateService(context, email, hasher, policy, time);
        var browserContext = new NativeRecoveryContext("browser-hash", "csrf-hash");

        var started = await service.StartAsync(new NativeRecoveryStartRequest(user.UserName!, browserContext));

        Assert.NotEqual(Guid.Empty, started.RequestId);
        Assert.Equal(recoveryEmail.Address, email.LastAddress);
        Assert.NotNull(email.LastCode);
        Assert.Equal(started.RequestId, (await context.RecoveryProofChallenges.AsNoTracking().SingleAsync()).Id);

        var wrongContext = await service.VerifyAsync(new NativeRecoveryVerificationRequest(
            started.RequestId,
            email.LastCode!,
            new NativeRecoveryContext("other-browser", "csrf-hash")));
        Assert.Equal(NativeRecoveryVerificationOutcome.Denied, wrongContext.Outcome);

        var verified = await service.VerifyAsync(new NativeRecoveryVerificationRequest(
            started.RequestId,
            email.LastCode!,
            browserContext));
        Assert.Equal(NativeRecoveryVerificationOutcome.Verified, verified.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(verified.Proof));

        var replay = await service.VerifyAsync(new NativeRecoveryVerificationRequest(
            started.RequestId,
            email.LastCode!,
            browserContext));
        Assert.Equal(NativeRecoveryVerificationOutcome.Denied, replay.Outcome);
        Assert.Empty(await context.UserSessions.AsNoTracking().ToListAsync());
        Assert.Equal(user.PasswordHash, (await context.Users.AsNoTracking().SingleAsync()).PasswordHash);
    }

    [Fact]
    public async Task Start_ReturnsUniformShapeWhenDisabledUnknownOrIneligible()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        await SeedRuntimeModeAsync(context, ForgotPasswordMode.Native);
        var time = new FixedTimeProvider();
        var hasher = new PasswordHasher<ApplicationUser>();
        var user = await SeedEligibleUserAsync(context, hasher);
        var recoveryEmail = await SeedVerifiedRecoveryEmailAsync(context, user.Id, time);
        var email = new CapturingEmailService();
        var browserContext = new NativeRecoveryContext("browser-hash", "csrf-hash");

        var disabled = new NativePasswordRecoveryProofService(
            context,
            email,
            hasher,
            new TestLookupNormalizer(),
            new FixedPolicyEvaluator(recoveryEmail.Address),
            new ForgotPasswordRoutingEvaluator(Options.Create(new ForgotPasswordRecoveryOptions())),
            Options.Create(new ForgotPasswordRecoveryOptions()),
            time);
        var disabledResult = await disabled.StartAsync(new NativeRecoveryStartRequest(user.UserName!, browserContext));

        var ceilingDenied = new NativePasswordRecoveryProofService(
            context,
            email,
            hasher,
            new TestLookupNormalizer(),
            new FixedPolicyEvaluator(recoveryEmail.Address),
            new ForgotPasswordRoutingEvaluator(Options.Create(new ForgotPasswordRecoveryOptions
            {
                DeploymentCeiling = ForgotPasswordMode.External,
                NativeRecoveryEnabled = true
            })),
            Options.Create(new ForgotPasswordRecoveryOptions
            {
                DeploymentCeiling = ForgotPasswordMode.External,
                NativeRecoveryEnabled = true
            }),
            time);
        var ceilingDeniedResult = await ceilingDenied.StartAsync(
            new NativeRecoveryStartRequest(user.UserName!, browserContext));

        var enabled = CreateService(
            context,
            email,
            hasher,
            new FixedPolicyEvaluator(recoveryEmail.Address),
            time);
        var unknownResult = await enabled.StartAsync(new NativeRecoveryStartRequest("missing-account", browserContext));
        user.IsActive = false;
        context.Users.Update(user);
        await context.SaveChangesAsync();
        var inactiveResult = await enabled.StartAsync(new NativeRecoveryStartRequest(user.UserName!, browserContext));

        Assert.NotEqual(Guid.Empty, disabledResult.RequestId);
        Assert.NotEqual(Guid.Empty, ceilingDeniedResult.RequestId);
        Assert.NotEqual(Guid.Empty, unknownResult.RequestId);
        Assert.NotEqual(Guid.Empty, inactiveResult.RequestId);
        Assert.Null(email.LastAddress);
        Assert.Empty(await context.RecoveryProofChallenges.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Start_AllowsOnlyLocalAuthorityAndDoesNotRevokeMigrationProof()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        await SeedRuntimeModeAsync(context, ForgotPasswordMode.Native);
        var time = new FixedTimeProvider();
        var hasher = new PasswordHasher<ApplicationUser>();
        var localUser = await SeedEligibleUserAsync(context, hasher, "local");
        var localEmail = await SeedVerifiedRecoveryEmailAsync(context, localUser.Id, time, "local-recovery@example.test");
        var migrationChallenge = new RecoveryProofChallenge(
            localEmail.Id,
            localUser.Id,
            RecoveryProofPurpose.MigrationOtp,
            "migration-code-hash",
            time.GetUtcNow(),
            time.GetUtcNow().AddMinutes(10));
        context.RecoveryProofChallenges.Add(migrationChallenge);
        await context.SaveChangesAsync();
        var email = new CapturingEmailService();
        var service = CreateService(
            context,
            email,
            hasher,
            new FixedPolicyEvaluator(localEmail.Address),
            time);

        await service.StartAsync(new NativeRecoveryStartRequest(
            localUser.UserName!,
            new NativeRecoveryContext("browser-hash", "csrf-hash")));

        Assert.Null((await context.RecoveryProofChallenges.AsNoTracking()
            .SingleAsync(challenge => challenge.Id == migrationChallenge.Id)).RevokedAtUtc);

        var directoryUser = await SeedEligibleUserAsync(context, hasher, "directory");
        var directoryEmail = await SeedVerifiedRecoveryEmailAsync(
            context,
            directoryUser.Id,
            time,
            "directory-recovery@example.test");
        context.ProviderSubjectDirectoryBindings.Add(new ProviderSubjectDirectoryBinding(
            directoryUser.Id,
            "provider",
            "directory-subject",
            Guid.NewGuid(),
            time.GetUtcNow().UtcDateTime));
        await context.SaveChangesAsync();
        var directoryMail = new CapturingEmailService();
        var directoryService = CreateService(
            context,
            directoryMail,
            hasher,
            new FixedPolicyEvaluator(directoryEmail.Address),
            time);

        await directoryService.StartAsync(new NativeRecoveryStartRequest(
            directoryUser.UserName!,
            new NativeRecoveryContext("browser-hash", "csrf-hash")));

        Assert.Null(directoryMail.LastAddress);
        Assert.DoesNotContain(
            await context.RecoveryProofChallenges.AsNoTracking().ToListAsync(),
            challenge => challenge.LocalAccountId == directoryUser.Id);
    }

    [Fact]
    public async Task Verify_DeniesWhenRecoveryRecordChangesAfterDelivery()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        await SeedRuntimeModeAsync(context, ForgotPasswordMode.Native);
        var time = new FixedTimeProvider();
        var hasher = new PasswordHasher<ApplicationUser>();
        var user = await SeedEligibleUserAsync(context, hasher);
        var recoveryEmail = await SeedVerifiedRecoveryEmailAsync(context, user.Id, time);
        var email = new CapturingEmailService();
        var policy = new FixedPolicyEvaluator(recoveryEmail.Address);
        var service = CreateService(context, email, hasher, policy, time);
        var browserContext = new NativeRecoveryContext("browser-hash", "csrf-hash");
        var started = await service.StartAsync(new NativeRecoveryStartRequest(user.UserName!, browserContext));

        recoveryEmail.ReplaceAddress(
            "replacement@example.test",
            "REPLACEMENT@EXAMPLE.TEST",
            time.GetUtcNow(),
            time.GetUtcNow().AddMinutes(1));
        recoveryEmail.MarkVerified(time.GetUtcNow());
        policy.Address = recoveryEmail.Address;
        await context.SaveChangesAsync();

        var result = await service.VerifyAsync(new NativeRecoveryVerificationRequest(
            started.RequestId,
            email.LastCode!,
            browserContext));

        Assert.Equal(NativeRecoveryVerificationOutcome.Denied, result.Outcome);
        Assert.Null(result.Proof);
    }

    [Fact]
    public async Task StartAndVerify_SourceBootstrapCandidate_PromotesOnlyAfterExactOtp()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        await SeedRuntimeModeAsync(context, ForgotPasswordMode.Native);
        var time = new FixedTimeProvider();
        var hasher = new PasswordHasher<ApplicationUser>();
        var user = await SeedEligibleUserAsync(context, hasher, "source-bootstrap");
        await SeedCompletedDirectoryWithSourceEmailAsync(context, user, time, "source@example.test");
        var email = new CapturingEmailService();
        var evaluator = CreateSourceBootstrapEvaluator(context, time, time.GetUtcNow().AddHours(1));
        var service = CreateService(context, email, hasher, evaluator, time, directoryRecoveryEnabled: true);
        var browserContext = new NativeRecoveryContext("browser-hash", "csrf-hash");

        var started = await service.StartAsync(new NativeRecoveryStartRequest(user.UserName!, browserContext));

        var pending = await context.RecoveryEmails.AsNoTracking().SingleAsync();
        Assert.Equal("source@example.test", email.LastAddress);
        Assert.Null(pending.VerifiedAtUtc);
        Assert.Equal(
            NativeRecoveryVerificationOutcome.Denied,
            (await service.VerifyAsync(new NativeRecoveryVerificationRequest(
                started.RequestId,
                "000000",
                browserContext))).Outcome);
        Assert.Null((await context.RecoveryEmails.AsNoTracking().SingleAsync()).VerifiedAtUtc);

        var verified = await service.VerifyAsync(new NativeRecoveryVerificationRequest(
            started.RequestId,
            email.LastCode!,
            browserContext));

        Assert.Equal(NativeRecoveryVerificationOutcome.Verified, verified.Outcome);
        Assert.NotNull((await context.RecoveryEmails.AsNoTracking().SingleAsync()).VerifiedAtUtc);
        Assert.NotNull((await context.RecoveryProofChallenges.AsNoTracking().SingleAsync()).VerifiedAtUtc);
    }

    [Theory]
    [InlineData("cutoff")]
    [InlineData("revoked")]
    [InlineData("conflict")]
    public async Task Start_SourceBootstrapIneligibleState_DoesNotEnrollOrSend(string state)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        await SeedRuntimeModeAsync(context, ForgotPasswordMode.Native);
        var time = new FixedTimeProvider();
        var hasher = new PasswordHasher<ApplicationUser>();
        var user = await SeedEligibleUserAsync(context, hasher, $"source-{state}");
        await SeedCompletedDirectoryWithSourceEmailAsync(context, user, time, "source@example.test");
        if (state == "revoked")
        {
            user.RecoverySourceBootstrapRevokedAtUtc = time.GetUtcNow();
            await context.SaveChangesAsync();
        }
        else if (state == "conflict")
        {
            context.RecoveryEmails.Add(new RecoveryEmailRecord(
                user.Id,
                "different@example.test",
                "DIFFERENT@EXAMPLE.TEST",
                time.GetUtcNow()));
            await context.SaveChangesAsync();
        }

        var email = new CapturingEmailService();
        var cutoff = state == "cutoff" ? time.GetUtcNow() : time.GetUtcNow().AddHours(1);
        var evaluator = CreateSourceBootstrapEvaluator(context, time, cutoff);
        var service = CreateService(context, email, hasher, evaluator, time, directoryRecoveryEnabled: true);

        await service.StartAsync(new NativeRecoveryStartRequest(
            user.UserName!,
            new NativeRecoveryContext("browser-hash", "csrf-hash")));

        Assert.Null(email.LastAddress);
        Assert.Empty(await context.RecoveryProofChallenges.AsNoTracking().ToListAsync());
        if (state != "conflict")
        {
            Assert.Empty(await context.RecoveryEmails.AsNoTracking().ToListAsync());
        }
    }

    [Theory]
    [InlineData(ForgotPasswordMode.Disabled)]
    [InlineData(ForgotPasswordMode.External)]
    public async Task RuntimeMode_PreventsStartAndInvalidatesPendingVerification(ForgotPasswordMode runtimeMode)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var policyRecord = await SeedRuntimeModeAsync(context, ForgotPasswordMode.Native);
        var time = new FixedTimeProvider();
        var hasher = new PasswordHasher<ApplicationUser>();
        var user = await SeedEligibleUserAsync(context, hasher);
        var recoveryEmail = await SeedVerifiedRecoveryEmailAsync(context, user.Id, time);
        var email = new CapturingEmailService();
        var service = CreateService(
            context,
            email,
            hasher,
            new FixedPolicyEvaluator(recoveryEmail.Address),
            time);
        var browserContext = new NativeRecoveryContext("browser-hash", "csrf-hash");
        var pending = await service.StartAsync(new NativeRecoveryStartRequest(user.UserName!, browserContext));
        var code = email.LastCode!;

        policyRecord.ForgotPasswordMode = runtimeMode;
        await context.SaveChangesAsync();
        var deniedVerification = await service.VerifyAsync(new NativeRecoveryVerificationRequest(
            pending.RequestId,
            code,
            browserContext));
        var sentBeforeDeniedStart = email.SendCount;
        var deniedStart = await service.StartAsync(new NativeRecoveryStartRequest(user.UserName!, browserContext));

        Assert.Equal(NativeRecoveryVerificationOutcome.Denied, deniedVerification.Outcome);
        Assert.Null(deniedVerification.Proof);
        Assert.NotEqual(Guid.Empty, deniedStart.RequestId);
        Assert.Equal(sentBeforeDeniedStart, email.SendCount);
    }

    [Fact]
    public void Options_DefaultOffAndRejectUnsafeNativeConfiguration()
    {
        var defaults = new ForgotPasswordRecoveryOptions();
        Assert.False(defaults.NativeRecoveryEnabled);
        Assert.False(defaults.NativeDirectoryRecoveryEnabled);
        Assert.False(defaults.OrdinaryRecoveryAssistanceEnabled);
        Assert.Equal(10, defaults.NativeOtpLifetimeMinutes);
        Assert.Equal(5, defaults.NativeOtpMaxAttempts);
        Assert.Equal(60, defaults.NativeOtpResendCooldownSeconds);

        var validator = new ForgotPasswordRecoveryOptionsValidator();
        Assert.True(validator.Validate(null, new ForgotPasswordRecoveryOptions
        {
            NativeRecoveryEnabled = true,
            DeploymentCeiling = ForgotPasswordMode.External
        }).Failed);
        Assert.True(validator.Validate(null, new ForgotPasswordRecoveryOptions
        {
            NativeOtpMaxAttempts = 0
        }).Failed);
        Assert.True(validator.Validate(null, new ForgotPasswordRecoveryOptions
        {
            NativeDirectoryRecoveryEnabled = true
        }).Failed);
        Assert.True(validator.Validate(null, new ForgotPasswordRecoveryOptions
        {
            OrdinaryRecoveryApprovalLifetimeMinutes = 11
        }).Failed);
    }

    private static NativePasswordRecoveryProofService CreateService(
        ApplicationDbContext context,
        CapturingEmailService email,
        IPasswordHasher<ApplicationUser> hasher,
        IRecoveryVerificationPolicyEvaluator policy,
        TimeProvider time,
        bool directoryRecoveryEnabled = false)
    {
        var options = Options.Create(new ForgotPasswordRecoveryOptions
        {
            DeploymentCeiling = ForgotPasswordMode.Native,
            NativeRecoveryEnabled = true,
            NativeDirectoryRecoveryEnabled = directoryRecoveryEnabled
        });
        return new NativePasswordRecoveryProofService(
            context,
            email,
            hasher,
            new TestLookupNormalizer(),
            policy,
            new ForgotPasswordRoutingEvaluator(options),
            options,
            time);
    }

    private static async Task<SecurityPolicy> SeedRuntimeModeAsync(
        ApplicationDbContext context,
        ForgotPasswordMode mode)
    {
        var policy = new SecurityPolicy
        {
            ForgotPasswordMode = mode
        };
        context.SecurityPolicies.Add(policy);
        await context.SaveChangesAsync();
        return policy;
    }

    private static ApplicationDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);

    private static async Task<ApplicationUser> SeedEligibleUserAsync(
        ApplicationDbContext context,
        IPasswordHasher<ApplicationUser> hasher,
        string suffix = "user")
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"{suffix}-{Guid.NewGuid():N}",
            Email = $"{suffix}-{Guid.NewGuid():N}@example.test",
            IsActive = true
        };
        user.NormalizedUserName = user.UserName.ToUpperInvariant();
        user.NormalizedEmail = user.Email.ToUpperInvariant();
        user.PasswordHash = hasher.HashPassword(user, "Current!Password1");
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static async Task<RecoveryEmailRecord> SeedVerifiedRecoveryEmailAsync(
        ApplicationDbContext context,
        Guid accountId,
        TimeProvider time,
        string address = "recovery@example.test")
    {
        var record = new RecoveryEmailRecord(accountId, address, address.ToUpperInvariant(), time.GetUtcNow());
        record.MarkVerified(time.GetUtcNow());
        context.RecoveryEmails.Add(record);
        await context.SaveChangesAsync();
        return record;
    }

    private static async Task SeedCompletedDirectoryWithSourceEmailAsync(
        ApplicationDbContext context,
        ApplicationUser user,
        TimeProvider time,
        string address)
    {
        var binding = new ProviderSubjectDirectoryBinding(
            user.Id,
            "source-provider",
            $"subject-{Guid.NewGuid():N}",
            Guid.NewGuid(),
            time.GetUtcNow().UtcDateTime,
            user.UserName);
        var migration = new CredentialMigrationStateRecord(user.Id, binding.Id, time.GetUtcNow());
        migration.Advance(
            CredentialMigrationState.ProofValidated,
            EffectiveEmailOtpRequirement.NotRequired,
            time.GetUtcNow());
        migration.Advance(CredentialMigrationState.DirectoryCredentialCommitted, time.GetUtcNow());
        migration.Advance(CredentialMigrationState.LocalFinalized, time.GetUtcNow());
        var snapshot = new ProviderMetadataSnapshot(binding.Id, time.GetUtcNow());
        snapshot.Refresh(
            address,
            ProviderEmailTrustOrigin.SourceVerified,
            time.GetUtcNow(),
            time.GetUtcNow());
        context.ProviderSubjectDirectoryBindings.Add(binding);
        context.CredentialMigrationStateRecords.Add(migration);
        context.ProviderMetadataSnapshots.Add(snapshot);
        await context.SaveChangesAsync();
    }

    private static RecoveryVerificationPolicyEvaluator CreateSourceBootstrapEvaluator(
        ApplicationDbContext context,
        TimeProvider time,
        DateTimeOffset bootstrapUntil) =>
        new(
            context,
            Options.Create(new RecoveryVerificationPolicyOptions
            {
                Enabled = true,
                CurrentPeriodId = "period",
                EffectiveAtUtc = time.GetUtcNow().AddHours(-1),
                GraceEndsAtUtc = time.GetUtcNow().AddHours(-1),
                BootstrapEnabled = true,
                BootstrapUntilUtc = bootstrapUntil,
                AcceptSourceVerifiedEmails = true
            }),
            time);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class FixedPolicyEvaluator(string address) : IRecoveryVerificationPolicyEvaluator
    {
        public string Address { get; set; } = address;

        public Task<RecoveryVerificationPolicyDecision> EvaluateAsync(
            Guid localAccountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new RecoveryVerificationPolicyDecision(
                true,
                "period",
                true,
                true,
                false,
                false,
                RecoveryPeriodDisposition.Satisfied,
                new RecoveryEmailPolicyDecision(
                    Address,
                    RecoveryEmailAddressSource.LocalRecord,
                    RecoveryEmailPolicyTrustOrigin.LocallyVerified,
                    true,
                    false,
                    false)));
    }

    private sealed class TestLookupNormalizer : ILookupNormalizer
    {
        public string? NormalizeName(string? name) => name?.ToUpperInvariant();
        public string? NormalizeEmail(string? email) => email?.ToUpperInvariant();
    }

    private sealed class CapturingEmailService : IEmailService
    {
        public string? LastAddress { get; private set; }
        public string? LastCode { get; private set; }
        public int SendCount { get; private set; }

        public Task SendEmailAsync(
            string to,
            string subject,
            string body,
            bool isHtml = false,
            CancellationToken ct = default)
        {
            LastAddress = to;
            SendCount++;
            LastCode = body.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.TrimEnd('.'))
                .Single(part => part.Length == 6 && part.All(char.IsDigit));
            return Task.CompletedTask;
        }

        public Task SendTestEmailAsync(MailSettingsDto settings, string to, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
