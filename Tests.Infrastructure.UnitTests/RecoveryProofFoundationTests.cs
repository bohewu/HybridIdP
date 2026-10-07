using System.Security.Cryptography;
using System.Text;
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
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class RecoveryProofFoundationTests
{
    [Theory]
    [InlineData("totp")]
    [InlineData("custom")]
    [InlineData("native")]
    public async Task MfaProof_ShouldAllowOnlyOnePersistedConcurrentConsumption(string kind)
    {
        var connectionString = $"Data Source=file:mfa-proof-{Guid.NewGuid():N}?mode=memory&cache=shared;Default Timeout=5";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using var anchor = CreateContext(keeper);
        await anchor.Database.EnsureCreatedAsync();
        var accountId = await SeedUserAsync(anchor);
        var time = new ProofTimeProvider(DateTimeOffset.UtcNow);
        var code = await SeedMfaProofAsync(anchor, accountId, kind, time.GetUtcNow());
        using var gate = new Barrier(2);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        async Task<bool> ConsumeAsync()
        {
            await using var contender = CreateContext(connectionString, new ProofSaveBarrier(gate));
            using var users = CreateProofUserManager(contender);
            var user = await contender.Users.SingleAsync(u => u.Id == accountId, timeout.Token);
            var service = CreateProofMfaService(contender, users, time);
            return await ConsumeMfaProofAsync(service, user, kind, code, timeout.Token);
        }

        var outcomes = await Task.WhenAll(Task.Run(ConsumeAsync), Task.Run(ConsumeAsync));
        Assert.Single(outcomes, succeeded => succeeded);
        Assert.Single(outcomes, succeeded => !succeeded);
        await using var replay = CreateContext(connectionString);
        using var replayUsers = CreateProofUserManager(replay);
        var replayUser = await replay.Users.SingleAsync(u => u.Id == accountId);
        Assert.False(await ConsumeMfaProofAsync(CreateProofMfaService(replay, replayUsers, time), replayUser, kind, code));
        if (kind == "totp") Assert.Equal(time.GetUtcNow().ToUnixTimeSeconds() / 30 + 2, replayUser.LastTotpValidatedWindow);
        if (kind == "custom") Assert.Equal("[]", replayUser.RecoveryCodes);
        if (kind == "native") Assert.Equal(0, await replayUsers.CountRecoveryCodesAsync(replayUser));
    }

    [Theory]
    [InlineData("totp")]
    [InlineData("custom")]
    [InlineData("native")]
    public async Task MfaProof_ShouldDiscardFailedTrackedConsumptionBeforeLaterSave(string kind)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var anchor = CreateContext(connection);
        await anchor.Database.EnsureCreatedAsync();
        var id = await SeedUserAsync(anchor);
        var time = new ProofTimeProvider(DateTimeOffset.UtcNow);
        var code = await SeedMfaProofAsync(anchor, id, kind, time.GetUtcNow());
        var failure = new FailProofSaveOnce();
        await using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).AddInterceptors(failure).Options);
        using var users = CreateProofUserManager(context);
        var user = await context.Users.SingleAsync(u => u.Id == id);
        var service = CreateProofMfaService(context, users, time);

        Assert.False(await ConsumeMfaProofAsync(service, user, kind, code));
        // This is the real caller's failure branch; it must not flush the failed proof/token edit.
        Assert.True((await users.AccessFailedAsync(user)).Succeeded);
        await using var observer = CreateContext(connection);
        var persisted = await observer.Users.SingleAsync(u => u.Id == id);
        Assert.Null(persisted.LastTotpValidatedWindow);
        if (kind == "custom") Assert.NotEqual("[]", persisted.RecoveryCodes);
        if (kind == "native")
        {
            using var observerUsers = CreateProofUserManager(observer);
            Assert.Equal(1, await observerUsers.CountRecoveryCodesAsync(persisted));
        }
        Assert.True(await ConsumeMfaProofAsync(service, user, kind, code));
        Assert.False(await ConsumeMfaProofAsync(service, user, kind, code));
    }

    private static async Task<string> SeedMfaProofAsync(ApplicationDbContext db, Guid id, string kind, DateTimeOffset now)
    {
        var user = await db.Users.SingleAsync(u => u.Id == id);
        user.TwoFactorEnabled = true;
        user.SecurityStamp = "test-mfa-stamp";
        user.ConcurrencyStamp = Guid.NewGuid().ToString();
        const string recoveryCode = "ABCDE-FGHIJ";
        if (kind == "custom") user.RecoveryCodes = System.Text.Json.JsonSerializer.Serialize(new[]
            { new PasswordHasher<ApplicationUser>().HashPassword(user, recoveryCode) });
        if (kind == "native") db.UserTokens.Add(new IdentityUserToken<Guid>
            { UserId = id, LoginProvider = "[AspNetUserStore]", Name = "RecoveryCodes", Value = recoveryCode });
        if (kind == "totp") db.UserTokens.Add(new IdentityUserToken<Guid>
            { UserId = id, LoginProvider = "[AspNetUserStore]", Name = "AuthenticatorKey", Value = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ" });
        await db.SaveChangesAsync();
        if (kind != "totp") return recoveryCode;
        var counter = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(now.ToUnixTimeSeconds() / 30 + 2));
        var hash = HMACSHA1.HashData(Encoding.ASCII.GetBytes("12345678901234567890"), counter);
        var offset = hash[^1] & 15;
        var binary = ((hash[offset] & 127) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    private static UserManager<ApplicationUser> CreateProofUserManager(ApplicationDbContext db)
    {
        var manager = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(db),
            Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(), [], [],
            new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);
        manager.RegisterTokenProvider(TokenOptions.DefaultAuthenticatorProvider, new AuthenticatorTokenProvider<ApplicationUser>());
        return manager;
    }

    private static MfaService CreateProofMfaService(ApplicationDbContext db, UserManager<ApplicationUser> users, TimeProvider time) =>
        new(users, Mock.Of<IBrandingService>(), Mock.Of<IEmailService>(), Mock.Of<IEmailTemplateService>(),
            new PasswordHasher<ApplicationUser>(), Mock.Of<IDistributedCache>(), Mock.Of<ISecurityPolicyService>(),
            Mock.Of<IEmailMfaAttemptStore>(), db, NullLogger<MfaService>.Instance, time);

    private static Task<bool> ConsumeMfaProofAsync(MfaService service, ApplicationUser user, string kind, string code,
        CancellationToken ct = default) => kind switch
        {
            "totp" => service.ValidateTotpCodeAsync(user, code, ct),
            "custom" => service.ValidateRecoveryCodeAsync(user, code, ct),
            _ => service.ValidateNativeRecoveryCodeAsync(user, code, ct)
        };

    private sealed class ProofTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ProofSaveBarrier(Barrier gate) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!gate.SignalAndWait(TimeSpan.FromSeconds(10), cancellationToken)) throw new TimeoutException("MFA contenders did not meet.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailProofSaveOnce : SaveChangesInterceptor
    {
        private bool _failed;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_failed) { _failed = true; throw new DbUpdateConcurrencyException("Test proof consumption conflict."); }
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task RecoveryEmailAndMigrationOtp_AreIndependentSingleUseProofsWithoutMfaSideEffects()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var accountId = await SeedUserAsync(context);
        var time = new FixedTimeProvider();
        var email = new CapturingEmailService();
        var audit = new CapturingAudit();
        var options = EnabledOptions();
        var authorizer = new FixedAuthorizer(selfService: true, administrator: false);
        var passwordHasher = new PasswordHasher<ApplicationUser>();
        var recoveryEmail = new RecoveryEmailService(
            context, authorizer, email, passwordHasher, audit, options, time);

        var started = await recoveryEmail.BeginAuthenticatedChangeAsync(
            new RecoveryEmailChangeRequest(accountId, "recovery@example.test"));
        Assert.Equal(RecoveryProofOutcome.Success, started.Outcome);
        var userBeforeVerification = await context.Users.AsNoTracking().SingleAsync(user => user.Id == accountId);
        Assert.Equal("profile@example.test", userBeforeVerification.Email);
        Assert.False(userBeforeVerification.EmailMfaEnabled);

        var verified = await recoveryEmail.VerifyAuthenticatedAsync(
            new RecoveryEmailVerificationRequest(accountId, email.LastCode!));
        Assert.Equal(RecoveryProofOutcome.Success, verified);
        Assert.True((await context.RecoveryEmails.AsNoTracking().SingleAsync()).VerifiedAtUtc.HasValue);

        time.Advance(TimeSpan.FromSeconds(61));
        var (continuation, migrationContext) = await SeedRequiredContinuationAsync(context, accountId, time);
        var migration = new MigrationOtpProofService(context, email, passwordHasher, options, time);
        Assert.Equal(
            RecoveryProofOutcome.Success,
            (await migration.SendAsync(new MigrationOtpSendRequest(continuation, migrationContext))).Outcome);

        var proof = await migration.VerifyAsync(
            new MigrationOtpVerificationRequest(continuation, migrationContext, email.LastCode!));
        Assert.Equal(RecoveryProofOutcome.Success, proof.Outcome);
        Assert.NotNull(proof.Proof);
        Assert.DoesNotContain(proof.Proof!, await context.RecoveryProofChallenges.Select(item => item.CodeHash).ToListAsync());
        Assert.Equal(
            RecoveryProofOutcome.Success,
            await migration.ConsumeAsync(new MigrationOtpConsumptionRequest(continuation, migrationContext, proof.Proof!)));
        Assert.Equal(
            RecoveryProofOutcome.Replayed,
            await migration.ConsumeAsync(new MigrationOtpConsumptionRequest(continuation, migrationContext, proof.Proof!)));

        var userAfterProof = await context.Users.AsNoTracking().SingleAsync(user => user.Id == accountId);
        Assert.Equal("profile@example.test", userAfterProof.Email);
        Assert.False(userAfterProof.EmailMfaEnabled);
        Assert.Equal(CredentialMigrationState.ProofValidated,
            (await context.CredentialMigrationStateRecords.AsNoTracking().SingleAsync()).State);
    }

    [Fact]
    public async Task RevokeAuthenticatedAsync_AuthorizedExistingAddress_DeletesAddressAndPersistsSourceBootstrapGuard()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var accountId = await SeedUserAsync(context);
        var time = new FixedTimeProvider();
        var email = new CapturingEmailService();
        var audit = new CapturingAudit();
        var recovery = new RecoveryEmailService(
            context,
            new FixedAuthorizer(selfService: true, administrator: false),
            email,
            new PasswordHasher<ApplicationUser>(),
            audit,
            EnabledOptions(),
            time);
        await ConfigureVerifiedRecoveryEmailAsync(recovery, email, accountId, "recovery@example.test");

        var outcome = await recovery.RevokeAuthenticatedAsync(accountId);

        Assert.Equal(RecoveryProofOutcome.Success, outcome);
        Assert.Empty(await context.RecoveryEmails.AsNoTracking().ToListAsync());
        Assert.Equal(
            time.GetUtcNow(),
            (await context.Users.AsNoTracking().SingleAsync(user => user.Id == accountId))
                .RecoverySourceBootstrapRevokedAtUtc);
        Assert.Contains(audit.Events, item => item.Category == RecoveryProofAuditCategory.RecoveryAddressRevoked);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RevokeAuthenticatedAsync_ShouldPreserveReservedProofHistory_WhenIdentityIsEnabledWithoutSelection(
        bool expired, bool rejectDelete)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var accountId = await SeedUserAsync(context);
        var time = new FixedTimeProvider();
        var email = new CapturingEmailService();
        var audit = new CapturingAudit();
        var recovery = new RecoveryEmailService(context,
            new FixedAuthorizer(selfService: true, administrator: false), email,
            new PasswordHasher<ApplicationUser>(), audit, EnabledOptions(), time);
        await ConfigureVerifiedRecoveryEmailAsync(recovery, email, accountId, "recovery@example.test");
        recovery = new RecoveryEmailService(context,
            new FixedAuthorizer(selfService: true, administrator: false), email,
            new PasswordHasher<ApplicationUser>(), audit, EnabledOptions(), time,
            identityOptions: Options.Create(new RecoveryIdentityVerificationOptions { Enabled = true }));
        var record = await context.RecoveryEmails.SingleAsync();
        var user = await context.Users.SingleAsync();
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        var now = time.GetUtcNow();
        var contextHash = Hash("browser");
        var csrfHash = Hash("csrf");
        var fingerprint = new string('F', 64);
        var challenge = new RecoveryProofChallenge(record.Id, accountId,
            RecoveryProofPurpose.NativePasswordRecovery, Hash("synthetic-otp"), now, now.AddMinutes(5));
        challenge.BindSelection(1, RecoveryDestinationKind.Legacy, fingerprint, record.Version);
        challenge.BindNativeAssistance(contextHash, csrfHash, false, null, record.Version, user.SecurityStamp);
        var grant = new RecoveryPrecheckGrant(accountId, null, null, user.SecurityStamp,
            contextHash, csrfHash, "synthetic-policy", 1, RecoveryDestinationKind.Legacy,
            fingerprint, record.Version, now, now.AddMinutes(5));
        Assert.True(grant.TryReserveChallenge(challenge, now));
        Assert.True(challenge.TryCompleteDelivery(now, true));
        context.RecoveryProofChallenges.Add(challenge);
        context.RecoveryPrecheckGrants.Add(grant);
        await context.SaveChangesAsync();
        Assert.Empty(await context.RecoveryEmailPreferences.ToListAsync());
        if (expired) time.Advance(TimeSpan.FromMinutes(6));
        if (rejectDelete)
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER RejectRecoveryEmailDelete BEFORE DELETE ON RecoveryEmails
                BEGIN SELECT RAISE(ABORT, 'synthetic delete failure'); END;
                """);
        }
        context.ChangeTracker.Clear();

        if (rejectDelete)
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => recovery.RevokeAuthenticatedAsync(accountId));
        }
        else
        {
            Assert.Equal(RecoveryProofOutcome.Success, await recovery.RevokeAuthenticatedAsync(accountId));
        }

        context.ChangeTracker.Clear();
        var retainedChallenge = await context.RecoveryProofChallenges.SingleAsync(c => c.Id == challenge.Id);
        var retainedGrant = await context.RecoveryPrecheckGrants.SingleAsync(g => g.Id == grant.Id);
        Assert.Equal(challenge.Id, retainedGrant.ReservedChallengeId);
        Assert.Equal(now, retainedGrant.ConsumedAtUtc);
        Assert.Equal(fingerprint, retainedGrant.DestinationFingerprint);
        var revokedAt = (await context.Users.SingleAsync()).RecoverySourceBootstrapRevokedAtUtc;
        if (rejectDelete)
        {
            Assert.Equal(record.Id, retainedChallenge.RecoveryEmailId);
            Assert.Null(retainedChallenge.RevokedAtUtc);
            Assert.Single(await context.RecoveryEmails.ToListAsync());
            Assert.Null(revokedAt);
            Assert.DoesNotContain(audit.Events, e => e.Category == RecoveryProofAuditCategory.RecoveryAddressRevoked);
        }
        else
        {
            Assert.Null(retainedChallenge.RecoveryEmailId);
            Assert.Equal(time.GetUtcNow(), retainedChallenge.RevokedAtUtc);
            Assert.False(retainedChallenge.TryReserveAttempt(time.GetUtcNow(), 5));
            Assert.Empty(await context.RecoveryEmails.ToListAsync());
            Assert.Equal(time.GetUtcNow(), revokedAt);
            Assert.Contains(audit.Events, e => e.Category == RecoveryProofAuditCategory.RecoveryAddressRevoked);
        }
    }

    [Theory]
    [InlineData(false, true, true, RecoveryProofOutcome.Unavailable)]
    [InlineData(true, false, true, RecoveryProofOutcome.Unauthorized)]
    [InlineData(true, true, false, RecoveryProofOutcome.Missing)]
    public async Task RevokeAuthenticatedAsync_NonSuccessfulPath_DoesNotPersistSourceBootstrapGuard(
        bool recoveryEmailEnabled,
        bool selfServiceAuthorized,
        bool hasRecoveryEmail,
        RecoveryProofOutcome expected)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var accountId = await SeedUserAsync(context);
        if (hasRecoveryEmail)
        {
            context.RecoveryEmails.Add(new RecoveryEmailRecord(
                accountId,
                "recovery@example.test",
                "RECOVERY@EXAMPLE.TEST",
                DateTimeOffset.Parse("2026-09-01T00:00:00Z")));
            await context.SaveChangesAsync();
        }

        var outcome = await new RecoveryEmailService(
                context,
                new FixedAuthorizer(selfServiceAuthorized, administrator: false),
                new CapturingEmailService(),
                new PasswordHasher<ApplicationUser>(),
                new CapturingAudit(),
                Options.Create(new CredentialMigrationOptions { RecoveryEmailEnabled = recoveryEmailEnabled }),
                new FixedTimeProvider())
            .RevokeAuthenticatedAsync(accountId);

        Assert.Equal(expected, outcome);
        Assert.Equal(hasRecoveryEmail, await context.RecoveryEmails.AsNoTracking().AnyAsync());
        Assert.Null((await context.Users.AsNoTracking().SingleAsync(user => user.Id == accountId))
            .RecoverySourceBootstrapRevokedAtUtc);
    }

    [Fact]
    public async Task AdministrativeReplacement_RequiresEvidenceAndNewAddressVerificationInSameCeremony()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var accountId = await SeedUserAsync(context);
        var actorId = await SeedUserAsync(context, "admin@example.test");
        var time = new FixedTimeProvider();
        var email = new CapturingEmailService();
        var audit = new CapturingAudit();
        var options = EnabledOptions();
        var authorizer = new FixedAuthorizer(selfService: true, administrator: true);
        var hasher = new PasswordHasher<ApplicationUser>();
        var recovery = new RecoveryEmailService(context, authorizer, email, hasher, audit, options, time);
        var migration = new MigrationOtpProofService(context, email, hasher, options, time);
        var assistance = new RecoveryAssistanceService(
            context, authorizer, recovery, migration, audit, options, time);

        Assert.Equal(
            RecoveryProofOutcome.Success,
            (await recovery.BeginAuthenticatedChangeAsync(
                new RecoveryEmailChangeRequest(accountId, "old@example.test"))).Outcome);
        Assert.Equal(
            RecoveryProofOutcome.Success,
            await recovery.VerifyAuthenticatedAsync(new RecoveryEmailVerificationRequest(accountId, email.LastCode!)));
        time.Advance(TimeSpan.FromSeconds(61));
        var (continuation, migrationContext) = await SeedRequiredContinuationAsync(context, accountId, time);
        Assert.Equal(
            RecoveryProofOutcome.Success,
            (await migration.SendAsync(new MigrationOtpSendRequest(continuation, migrationContext))).Outcome);
        var priorMigrationChallengeId = await context.RecoveryProofChallenges.AsNoTracking()
            .Where(challenge => challenge.Purpose == RecoveryProofPurpose.MigrationOtp)
            .Select(challenge => challenge.Id)
            .SingleAsync();

        var denied = await assistance.ReplaceRecoveryEmailAsync(new AdminRecoveryEmailReplacementRequest(
            actorId,
            accountId,
            "new@example.test",
            string.Empty,
            "Help desk request"));
        Assert.Equal(RecoveryProofOutcome.Invalid, denied.Outcome);

        var replaced = await assistance.ReplaceRecoveryEmailAsync(new AdminRecoveryEmailReplacementRequest(
            actorId,
            accountId,
            "new@example.test",
            "ticket-reference",
            "Identity checked"));
        Assert.Equal(RecoveryProofOutcome.Success, replaced.Outcome);
        var pending = await context.RecoveryEmails.AsNoTracking().SingleAsync();
        Assert.Null(pending.VerifiedAtUtc);
        Assert.Equal(actorId, pending.LastAdministrativeActorId);
        Assert.NotNull((await context.RecoveryProofChallenges.AsNoTracking()
            .SingleAsync(challenge => challenge.Id == priorMigrationChallengeId)).RevokedAtUtc);

        Assert.Equal(
            RecoveryProofOutcome.Success,
            await recovery.VerifyForMigrationAsync(new MigrationRecoveryEmailVerificationRequest(
                continuation,
                migrationContext,
                email.LastCode!)));
        Assert.True((await context.RecoveryEmails.AsNoTracking().SingleAsync()).VerifiedAtUtc.HasValue);
        Assert.Contains(audit.Events, item => item.Category == RecoveryProofAuditCategory.AdminRecoveryAddressReplaced);
        Assert.All(audit.Events, item =>
        {
            Assert.NotEqual("new@example.test", item.CorrelationId.ToString());
            Assert.NotEqual(email.LastCode, item.CorrelationId.ToString());
        });
    }

    [Fact]
    public async Task MigrationOtpSend_AdministrativeReplacementAtReservationBoundary_InvalidatesOldDestinationProof()
    {
        var connectionString = $"Data Source=file:recovery-proof-race-{Guid.NewGuid():N}?mode=memory&cache=shared;Default Timeout=5";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using var anchor = CreateContext(keeper);
        await anchor.Database.EnsureCreatedAsync();
        var accountId = await SeedUserAsync(anchor);
        var actorId = await SeedUserAsync(anchor, "admin@example.test");
        var time = new FixedTimeProvider();
        var options = EnabledOptions();
        var setupEmail = new CapturingEmailService();
        var setupRecovery = new RecoveryEmailService(
            anchor,
            new FixedAuthorizer(selfService: true, administrator: true),
            setupEmail,
            new PasswordHasher<ApplicationUser>(),
            new CapturingAudit(),
            options,
            time);
        await ConfigureVerifiedRecoveryEmailAsync(
            setupRecovery,
            setupEmail,
            accountId,
            "old@example.test");
        var (continuation, migrationContext) = await SeedRequiredContinuationAsync(anchor, accountId, time);
        time.Advance(TimeSpan.FromSeconds(61));

        var reservationGate = new PauseAfterFirstSaveInterceptor();
        await using var senderContext = CreateContext(connectionString, reservationGate);
        var oldAddressEmail = new CapturingEmailService();
        var sender = new MigrationOtpProofService(
            senderContext,
            oldAddressEmail,
            new PasswordHasher<ApplicationUser>(),
            options,
            time);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var sendTask = sender.SendAsync(
            new MigrationOtpSendRequest(continuation, migrationContext),
            timeout.Token);

        try
        {
            await reservationGate.Reached.WaitAsync(timeout.Token);
        }
        finally
        {
            reservationGate.Release();
        }

        var sent = await sendTask;
        Assert.Equal(RecoveryProofOutcome.Success, sent.Outcome);
        Assert.Equal("old@example.test", oldAddressEmail.LastAddress);

        var replacementEmail = new CapturingEmailService();
        await using (var replacementContext = CreateContext(connectionString))
        {
            var replaced = await CreateAssistance(
                    replacementContext,
                    time,
                    options,
                    administrator: true,
                    email: replacementEmail)
                .ReplaceRecoveryEmailAsync(
                    new AdminRecoveryEmailReplacementRequest(
                        actorId,
                        accountId,
                        "new@example.test",
                        "ticket-reference",
                        "Identity checked"),
                    timeout.Token);
            Assert.Equal(RecoveryProofOutcome.Success, replaced.Outcome);
        }

        await using var verificationContext = CreateContext(connectionString);
        var verifier = new RecoveryEmailService(
            verificationContext,
            new FixedAuthorizer(selfService: true, administrator: true),
            new CapturingEmailService(),
            new PasswordHasher<ApplicationUser>(),
            new CapturingAudit(),
            options,
            time);
        Assert.Equal(
            RecoveryProofOutcome.Success,
            await verifier.VerifyForMigrationAsync(
                new MigrationRecoveryEmailVerificationRequest(
                    continuation,
                    migrationContext,
                    replacementEmail.LastCode!),
                timeout.Token));

        var oldAddressProof = await new MigrationOtpProofService(
                verificationContext,
                new CapturingEmailService(),
                new PasswordHasher<ApplicationUser>(),
                options,
                time)
            .VerifyAsync(
                new MigrationOtpVerificationRequest(
                    continuation,
                    migrationContext,
                    oldAddressEmail.LastCode!),
                timeout.Token);
        Assert.NotEqual(RecoveryProofOutcome.Success, oldAddressProof.Outcome);
        Assert.Null(oldAddressProof.Proof);
        Assert.Equal("new@example.test", (await verificationContext.RecoveryEmails.AsNoTracking().SingleAsync(timeout.Token)).Address);
        Assert.NotNull((await verificationContext.RecoveryProofChallenges.AsNoTracking()
            .SingleAsync(
                challenge => challenge.Purpose == RecoveryProofPurpose.MigrationOtp,
                timeout.Token)).RevokedAtUtc);

        Assert.True(reservationGate.FirstSaveCompletedInsideTransaction);
    }

    [Fact]
    public async Task AdministrativeResend_SelectsTargetAccountAndPreservesCooldown()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var targetAccountId = await SeedUserAsync(context, "target-profile@example.test");
        var otherAccountId = await SeedUserAsync(context, "other-profile@example.test");
        var actorId = await SeedUserAsync(context, "admin@example.test");
        var time = new FixedTimeProvider();
        var email = new CapturingEmailService();
        var audit = new CapturingAudit();
        var options = EnabledOptions();
        var authorizer = new FixedAuthorizer(selfService: true, administrator: true);
        var hasher = new PasswordHasher<ApplicationUser>();
        var recovery = new RecoveryEmailService(context, authorizer, email, hasher, audit, options, time);
        var migration = new MigrationOtpProofService(context, email, hasher, options, time);
        var assistance = new RecoveryAssistanceService(
            context, authorizer, recovery, migration, audit, options, time);

        await ConfigureVerifiedRecoveryEmailAsync(recovery, email, targetAccountId, "target-recovery@example.test");
        await ConfigureVerifiedRecoveryEmailAsync(recovery, email, otherAccountId, "other-recovery@example.test");
        await SeedRequiredContinuationAsync(context, targetAccountId, time);
        await SeedRequiredContinuationAsync(context, otherAccountId, time);
        time.Advance(TimeSpan.FromSeconds(61));

        var sent = await assistance.ResendMigrationOtpAsync(
            new AdminMigrationOtpResendRequest(actorId, targetAccountId));
        Assert.Equal(RecoveryProofOutcome.Success, sent.Outcome);
        Assert.Equal("target-recovery@example.test", email.LastAddress);
        Assert.Contains(audit.Events, item =>
            item.Category == RecoveryProofAuditCategory.AdminMigrationOtpResent &&
            item.TargetAccountId == targetAccountId &&
            item.ActorAccountId == actorId);

        var cooldown = await assistance.ResendMigrationOtpAsync(
            new AdminMigrationOtpResendRequest(actorId, targetAccountId));
        Assert.Equal(RecoveryProofOutcome.Cooldown, cooldown.Outcome);
        Assert.True(cooldown.RetryAfterSeconds > 0);
    }

    [Fact]
    public async Task AdministrativeAssistance_RejectsUnauthorizedOperator()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var accountId = await SeedUserAsync(context);
        var actorId = await SeedUserAsync(context, "admin@example.test");
        var time = new FixedTimeProvider();
        var options = EnabledOptions();
        await SeedRequiredContinuationAsync(context, accountId, time);

        var result = await CreateAssistance(context, time, options, administrator: false)
            .IssueResetApprovalAsync(new AdminResetApprovalRequest(
                actorId,
                accountId,
                "ticket-reference",
                "Emergency migration assistance"));

        Assert.Equal(RecoveryProofOutcome.Unauthorized, result.Outcome);
        Assert.Empty(await context.RecoveryResetApprovals.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task AdministrativeAssistance_FailsClosedForNoOrMultipleActiveTargetContinuations()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var missingAccountId = await SeedUserAsync(context, "missing@example.test");
        var ambiguousAccountId = await SeedUserAsync(context, "ambiguous@example.test");
        var actorId = await SeedUserAsync(context, "admin@example.test");
        var time = new FixedTimeProvider();
        var options = EnabledOptions();
        var assistance = CreateAssistance(context, time, options, administrator: true);

        var missing = await assistance.IssueResetApprovalAsync(new AdminResetApprovalRequest(
            actorId,
            missingAccountId,
            "ticket-reference",
            "Emergency migration assistance"));
        Assert.Equal(RecoveryProofOutcome.Missing, missing.Outcome);

        await SeedRequiredContinuationAsync(context, ambiguousAccountId, time);
        await SeedAdditionalActiveContinuationAsync(context, ambiguousAccountId, time);
        var ambiguous = await assistance.IssueResetApprovalAsync(new AdminResetApprovalRequest(
            actorId,
            ambiguousAccountId,
            "ticket-reference",
            "Emergency migration assistance"));
        Assert.Equal(RecoveryProofOutcome.Missing, ambiguous.Outcome);
        Assert.Empty(await context.RecoveryResetApprovals.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ResetApproval_ConcurrentConsumption_AllowsExactlyOneRequestAndRejectsCrossCeremony()
    {
        var connectionString = $"Data Source=file:recovery-proof-{Guid.NewGuid():N}?mode=memory&cache=shared;Default Timeout=5";
        await using var anchor = CreateContext(connectionString);
        await anchor.Database.EnsureCreatedAsync();
        var accountId = await SeedUserAsync(anchor);
        var actorId = await SeedUserAsync(anchor, "admin@example.test");
        var time = new FixedTimeProvider();
        var options = EnabledOptions();
        var (continuation, migrationContext) = await SeedRequiredContinuationAsync(anchor, accountId, time);
        var issued = await CreateAssistance(anchor, time, options, true).IssueResetApprovalAsync(
            new AdminResetApprovalRequest(
                actorId,
                accountId,
                "ticket-reference",
                "Emergency migration assistance"));
        Assert.Equal(RecoveryProofOutcome.Success, issued.Outcome);

        var otherContext = new MigrationContinuationContext(Hash("other-browser"), migrationContext.CsrfHash);
        Assert.Equal(
            RecoveryProofOutcome.Missing,
            await CreateAssistance(anchor, time, options, true).ConsumeResetApprovalAsync(
                new ResetApprovalConsumptionRequest(continuation, otherContext)));

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var contender = CreateContext(connectionString);
            return await CreateAssistance(contender, time, options, true).ConsumeResetApprovalAsync(
                new ResetApprovalConsumptionRequest(
                    continuation,
                    migrationContext));
        }));

        Assert.Single(outcomes, outcome => outcome == RecoveryProofOutcome.Success);
        Assert.Equal(7, outcomes.Count(outcome => outcome == RecoveryProofOutcome.Replayed));
    }

    [Fact]
    public async Task ResetApprovalStatus_IsReadOnlyAndReportsExpiryBeforeCommit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var accountId = await SeedUserAsync(context);
        var actorId = await SeedUserAsync(context, "admin@example.test");
        var time = new FixedTimeProvider();
        var options = EnabledOptions();
        options.Value.RecoveryApprovalLifetimeMinutes = 1;
        var (continuation, migrationContext) = await SeedRequiredContinuationAsync(context, accountId, time);
        var assistance = CreateAssistance(context, time, options, true);
        Assert.Equal(
            RecoveryProofOutcome.Success,
            (await assistance.IssueResetApprovalAsync(new AdminResetApprovalRequest(
                actorId,
                accountId,
                "ticket-reference",
                "Emergency migration assistance"))).Outcome);

        Assert.Equal(
            RecoveryProofOutcome.Success,
            await assistance.GetResetApprovalStatusAsync(
                new ResetApprovalConsumptionRequest(continuation, migrationContext)));
        Assert.Null((await context.RecoveryResetApprovals.AsNoTracking().SingleAsync()).ConsumedAtUtc);

        time.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(
            RecoveryProofOutcome.Expired,
            await assistance.GetResetApprovalStatusAsync(
                new ResetApprovalConsumptionRequest(continuation, migrationContext)));
        Assert.Equal(
            RecoveryProofOutcome.Expired,
            await assistance.ConsumeResetApprovalAsync(
                new ResetApprovalConsumptionRequest(continuation, migrationContext)));
    }

    [Fact]
    public void CredentialMigrationOptions_DefaultOffAndUnsafeCombinationsFailValidation()
    {
        var defaults = new CredentialMigrationOptions();
        Assert.False(defaults.RecoveryEmailEnabled);
        Assert.False(defaults.MigrationEmailOtpEnabled);
        Assert.False(defaults.RecoveryAdminAssistanceEnabled);
        Assert.Equal(10, defaults.RecoveryOtpLifetimeMinutes);
        Assert.Equal(5, defaults.RecoveryOtpMaxAttempts);
        Assert.Equal(60, defaults.RecoveryOtpResendCooldownSeconds);

        var validator = new CredentialMigrationOptionsValidator(
            Options.Create(new DirectoryIntegrationOptions { Enabled = true, AuthenticationEnabled = true }),
            Options.Create(new LegacyPasswordSyncOptions()));
        var invalid = validator.Validate(null, new CredentialMigrationOptions
        {
            Enabled = true,
            MigrationEmailOtpEnabled = true
        });
        Assert.True(invalid.Failed);
    }

    [Theory]
    [InlineData(false, "none")]
    [InlineData(true, "none")]
    [InlineData(false, "stamp")]
    [InlineData(true, "stamp")]
    [InlineData(false, "destination")]
    [InlineData(true, "destination")]
    [InlineData(false, "epoch")]
    [InlineData(true, "epoch")]
    public async Task MigrationSelection_RequiresCurrentDestinationAtVerifyAndConsume(bool afterOtp, string change)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var id = await SeedUserAsync(context);
        var user = await context.Users.SingleAsync(u => u.Id == id);
        user.IsActive = true; user.SecurityStamp = "stamp";
        await context.SaveChangesAsync();
        var time = new FixedTimeProvider();
        var (token, browser) = await SeedRequiredContinuationAsync(context, id, time);
        var selection = Options.Create(new RecoveryEmailSelectionOptions { Enabled = true, TrustedDefaultFallbackEnabled = true });
        var defaults = new SelectionDefault();
        var policy = Options.Create(new RecoveryVerificationPolicyOptions { Enabled = true, CurrentPeriodId = "period" });
        var resolver = new RecoveryDestinationResolver(context, defaults, selection, policy, time);
        var mail = new CapturingEmailService();
        var service = new MigrationOtpProofService(context, mail, new PasswordHasher<ApplicationUser>(), EnabledOptions(), time, resolver, selection);
        Assert.Equal(RecoveryProofOutcome.Success, (await service.SendAsync(new(token, browser))).Outcome);
        var code = mail.LastCode!;
        var proof = afterOtp ? (await service.VerifyAsync(new(token, browser, code))).Proof : null;
        if (afterOtp) Assert.NotNull(proof);
        if (change == "stamp") user.SecurityStamp = "changed";
        if (change == "destination") defaults.Version++;
        if (change == "epoch")
        {
            var preference = new RecoveryEmailPreference(id, time.GetUtcNow());
            preference.TrySelect(RecoveryEmailSelectionMode.UseDefault, 1, time.GetUtcNow());
            context.RecoveryEmailPreferences.Add(preference);
        }
        await context.SaveChangesAsync();
        if (afterOtp)
        {
            var result = await service.ConsumeAsync(new(token, browser, proof!));
            Assert.Equal(change == "none", result == RecoveryProofOutcome.Success);
        }
        else
        {
            var result = await service.VerifyAsync(new(token, browser, code));
            Assert.Equal(change == "none", result.Outcome == RecoveryProofOutcome.Success);
        }
        Assert.Empty(await context.RecoveryEmails.ToListAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task MigrationApproval_SelectionAndSecurityStateRemainBound(bool changed, bool reissue)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var id = await SeedUserAsync(context);
        var user = await context.Users.SingleAsync(u => u.Id == id);
        user.IsActive = true; user.SecurityStamp = "stamp";
        await context.SaveChangesAsync();
        var time = new FixedTimeProvider();
        var (token, browser) = await SeedRequiredContinuationAsync(context, id, time);
        var selection = Options.Create(new RecoveryEmailSelectionOptions { Enabled = true, TrustedDefaultFallbackEnabled = true });
        var resolver = new RecoveryDestinationResolver(context, new SelectionDefault(), selection,
            Options.Create(new RecoveryVerificationPolicyOptions { Enabled = true }), time);
        var options = EnabledOptions(); var mail = new CapturingEmailService(); var hasher = new PasswordHasher<ApplicationUser>();
        var audit = new CapturingAudit(); var authorizer = new FixedAuthorizer(false, true);
        var email = new RecoveryEmailService(context, authorizer, mail, hasher, audit, options, time, selection);
        var migration = new MigrationOtpProofService(context, mail, hasher, options, time, resolver, selection);
        var assistance = new RecoveryAssistanceService(context, authorizer, email, migration, audit, options, time, resolver, selection);
        Assert.Equal(RecoveryProofOutcome.Success, (await assistance.IssueResetApprovalAsync(new(Guid.NewGuid(), id, "checked", "support"))).Outcome);
        if (reissue)
            Assert.Equal(RecoveryProofOutcome.Success, (await assistance.IssueResetApprovalAsync(new(Guid.NewGuid(), id, "rechecked", "support again"))).Outcome);
        if (changed) { user.SecurityStamp = "changed"; await context.SaveChangesAsync(); }
        var result = await assistance.ConsumeResetApprovalAsync(new(token, browser));
        Assert.Equal(changed ? RecoveryProofOutcome.Invalid : RecoveryProofOutcome.Success, result);
    }

    [Fact]
    public async Task MigrationSelection_ShouldReserveFirstSendAcrossIndependentContexts()
    {
        var connectionString = $"Data Source=file:migration-default-cooldown-{Guid.NewGuid():N}?mode=memory&cache=shared;Default Timeout=5";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using var anchor = CreateContext(keeper);
        await anchor.Database.EnsureCreatedAsync();
        var accountId = await SeedUserAsync(anchor);
        var user = await anchor.Users.SingleAsync();
        user.IsActive = true;
        user.SecurityStamp = "migration-cooldown-stamp";
        await anchor.SaveChangesAsync();
        var time = new FixedTimeProvider();
        var (token, browser) = await SeedRequiredContinuationAsync(anchor, accountId, time);
        var selection = Options.Create(new RecoveryEmailSelectionOptions { Enabled = true, TrustedDefaultFallbackEnabled = true });
        using var gate = new Barrier(2);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        async Task<(RecoveryProofOutcome Outcome, string? Address)> SendAsync()
        {
            await using var contender = CreateContext(connectionString);
            var mail = new CapturingEmailService();
            var resolver = new RecoveryDestinationResolver(contender, new SelectionDefault(), selection,
                Options.Create(new RecoveryVerificationPolicyOptions { Enabled = true }), time);
            var sender = new MigrationOtpProofService(contender, mail, new ConcurrentSendHasher(gate),
                EnabledOptions(), time, resolver, selection);
            var result = await sender.SendAsync(new(token, browser), timeout.Token);
            return (result.Outcome, mail.LastAddress);
        }

        var sends = await Task.WhenAll(Task.Run(SendAsync), Task.Run(SendAsync));
        Assert.Single(sends, s => s.Outcome == RecoveryProofOutcome.Success);
        Assert.Single(sends, s => s.Outcome == RecoveryProofOutcome.Cooldown);
        Assert.Single(sends, s => s.Address == "default@example.test");
        var challenge = await anchor.RecoveryProofChallenges.AsNoTracking().SingleAsync();
        Assert.Equal(accountId, challenge.LocalAccountId);
        Assert.Equal(RecoveryProofPurpose.MigrationOtp, challenge.Purpose);
        Assert.Equal((await anchor.CredentialMigrationContinuations.AsNoTracking().SingleAsync()).Id,
            challenge.CredentialMigrationContinuationId);
        Assert.Empty(await anchor.RecoveryEmails.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigrationSelectionReplacement_ShouldUseQuietActualDelivery(bool failDelivery)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var accountId = await SeedUserAsync(context);
        var user = await context.Users.SingleAsync();
        user.IsActive = true;
        user.SecurityStamp = "replacement-stamp";
        await context.SaveChangesAsync();
        var time = new FixedTimeProvider();
        var (token, browser) = await SeedRequiredContinuationAsync(context, accountId, time);
        var selection = Options.Create(new RecoveryEmailSelectionOptions { Enabled = true, TrustedDefaultFallbackEnabled = true });
        var resolver = new RecoveryDestinationResolver(context, new SelectionDefault(), selection,
            Options.Create(new RecoveryVerificationPolicyOptions { Enabled = true }), time);
        var queue = new Mock<IEmailQueue>(MockBehavior.Strict);
        var dispatcher = new Mock<IEmailDispatcher>(MockBehavior.Strict);
        EmailMessage? sent = null;
        dispatcher.Setup(d => d.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((message, _) =>
            {
                sent = message;
                return failDelivery ? Task.FromException(new InvalidOperationException(message.To + message.Body)) : Task.CompletedTask;
            });
        var settings = new Mock<IOptionsSnapshot<EmailOptions>>();
        settings.SetupGet(s => s.Value).Returns(new EmailOptions { SmtpHost = "synthetic.test" });
        var delivery = new RecoveryOtpDeliveryService(dispatcher.Object, settings.Object);
        var mail = new EmailService(queue.Object, dispatcher.Object);
        var audit = new CapturingAudit();
        var authorizer = new FixedAuthorizer(false, true);
        var hasher = new PasswordHasher<ApplicationUser>();
        var options = EnabledOptions();
        var recovery = new RecoveryEmailService(context, authorizer, mail, hasher, audit, options, time, selection, delivery: delivery);
        var migration = new MigrationOtpProofService(context, mail, hasher, options, time, resolver, selection);
        var assistance = new RecoveryAssistanceService(context, authorizer, recovery, migration, audit, options, time, resolver, selection);
        const string candidate = "selected-candidate@example.test";
        var result = await assistance.ReplaceRecoveryEmailAsync(new(Guid.NewGuid(), accountId, candidate, "checked", "support"));
        Assert.Equal(failDelivery ? RecoveryProofOutcome.Unavailable : RecoveryProofOutcome.Success, result.Outcome);
        Assert.NotNull(sent);
        Assert.Equal(candidate, sent.To);
        Assert.Equal("Verify recovery email", sent.Subject);
        Assert.False(sent.IsHtml);
        var code = System.Text.RegularExpressions.Regex.Match(sent.Body, @"\b\d{6}\b").Value;
        var challenge = await context.RecoveryProofChallenges.AsNoTracking().SingleAsync();
        Assert.Equal(RecoveryProofPurpose.RecoveryAddressVerification, challenge.Purpose);
        Assert.Equal(failDelivery, challenge.RevokedAtUtc is not null);
        var verified = await recovery.VerifyForMigrationAsync(new(token, browser, code));
        Assert.Equal(!failDelivery, verified == RecoveryProofOutcome.Success);
        Assert.NotEqual(RecoveryProofOutcome.Success, (await migration.VerifyAsync(new(token, browser, code))).Outcome);
        queue.Verify(q => q.QueueEmailAsync(It.IsAny<EmailMessage>()), Times.Never());
        var auditText = System.Text.Json.JsonSerializer.Serialize(audit.Events);
        Assert.DoesNotContain(candidate, auditText);
        Assert.DoesNotContain(code, auditText);
    }

    private sealed class ConcurrentSendHasher(Barrier gate) : IPasswordHasher<ApplicationUser>
    {
        private readonly PasswordHasher<ApplicationUser> _hasher = new();
        public string HashPassword(ApplicationUser user, string password)
        {
            if (!gate.SignalAndWait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Concurrent send rendezvous failed.");
            return _hasher.HashPassword(user, password);
        }
        public PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hash, string provided) =>
            _hasher.VerifyHashedPassword(user, hash, provided);
    }

    private sealed class SelectionDefault : IRecoveryDefaultDestinationEvaluator
    {
        public long Version { get; set; } = 1;
        public Task<RecoveryDefaultDestination?> EvaluateDefaultAsync(Guid accountId, CancellationToken ct = default) =>
            Task.FromResult<RecoveryDefaultDestination?>(new("default@example.test", new string('D', 64), Version, false));
    }

    private static RecoveryAssistanceService CreateAssistance(
        ApplicationDbContext context,
        TimeProvider time,
        IOptions<CredentialMigrationOptions> options,
        bool administrator,
        CapturingEmailService? email = null)
    {
        email ??= new CapturingEmailService();
        var audit = new CapturingAudit();
        var authorizer = new FixedAuthorizer(false, administrator);
        var hasher = new PasswordHasher<ApplicationUser>();
        var recovery = new RecoveryEmailService(context, authorizer, email, hasher, audit, options, time);
        var migration = new MigrationOtpProofService(context, email, hasher, options, time);
        return new RecoveryAssistanceService(context, authorizer, recovery, migration, audit, options, time);
    }

    private static IOptions<CredentialMigrationOptions> EnabledOptions() =>
        Options.Create(new CredentialMigrationOptions
        {
            Enabled = true,
            RecoveryEmailEnabled = true,
            MigrationEmailOtpEnabled = true,
            RecoveryAdminAssistanceEnabled = true,
            EmailOtpPolicyFloor = EmailOtpPolicy.Required
        });

    private static ApplicationDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);

    private static ApplicationDbContext CreateContext(
        string connectionString,
        params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(interceptors)
            .Options);

    private static async Task<Guid> SeedUserAsync(ApplicationDbContext context, string email = "profile@example.test")
    {
        var id = Guid.NewGuid();
        context.Users.Add(new ApplicationUser
        {
            Id = id,
            UserName = $"user-{id:N}",
            Email = email,
            EmailMfaEnabled = false
        });
        await context.SaveChangesAsync();
        return id;
    }

    private static async Task<(string Continuation, MigrationContinuationContext Context)> SeedRequiredContinuationAsync(
        ApplicationDbContext context,
        Guid accountId,
        TimeProvider time)
    {
        var binding = new DirectoryObjectBinding("test.provider", $"subject-{accountId:N}", Guid.NewGuid());
        var states = new CredentialMigrationStateStore(context, time);
        await states.EnsureRequiredAsync(accountId, binding);
        await states.AdvanceToProofValidatedAsync(accountId, EffectiveEmailOtpRequirement.Required);
        var migrationContext = new MigrationContinuationContext(Hash("browser"), Hash("csrf"));
        var continuation = await new MigrationContinuationStore(context, time).CreateAsync(
            new MigrationContinuationRequest(accountId, binding, migrationContext));
        return (continuation.ProtectedValue, migrationContext);
    }

    private static async Task SeedAdditionalActiveContinuationAsync(
        ApplicationDbContext context,
        Guid accountId,
        TimeProvider time)
    {
        var stateId = await context.CredentialMigrationStateRecords.AsNoTracking()
            .Where(state => state.LocalAccountId == accountId)
            .Select(state => state.Id)
            .SingleAsync();
        var now = time.GetUtcNow();
        context.CredentialMigrationContinuations.Add(new CredentialMigrationContinuationRecord(
            stateId,
            Hash(Guid.NewGuid().ToString("N")),
            Hash("other-browser"),
            Hash("other-csrf"),
            now,
            now.AddMinutes(10)));
        await context.SaveChangesAsync();
    }

    private static async Task ConfigureVerifiedRecoveryEmailAsync(
        RecoveryEmailService recovery,
        CapturingEmailService email,
        Guid accountId,
        string address)
    {
        Assert.Equal(
            RecoveryProofOutcome.Success,
            (await recovery.BeginAuthenticatedChangeAsync(new RecoveryEmailChangeRequest(accountId, address))).Outcome);
        Assert.Equal(
            RecoveryProofOutcome.Success,
            await recovery.VerifyAuthenticatedAsync(new RecoveryEmailVerificationRequest(accountId, email.LastCode!)));
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class FixedTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 5, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }

    private sealed class FixedAuthorizer(bool selfService, bool administrator) : IRecoveryProofAuthorizer
    {
        public Task<bool> IsSelfServiceAuthorizedAsync(Guid localAccountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(selfService);

        public Task<bool> IsAdministratorAuthorizedAsync(Guid actorAccountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(administrator);
    }

    private sealed class CapturingAudit : IRecoveryProofAudit
    {
        public List<RecoveryProofAuditEvent> Events { get; } = [];
        public Task RecordAsync(RecoveryProofAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingEmailService : IEmailService
    {
        public string? LastAddress { get; private set; }
        public string? LastCode { get; private set; }

        public Task SendEmailAsync(
            string to,
            string subject,
            string body,
            bool isHtml = false,
            CancellationToken ct = default)
        {
            LastAddress = to;
            LastCode = body.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.TrimEnd('.'))
                .Single(part => part.Length == 6 && part.All(char.IsDigit));
            return Task.CompletedTask;
        }

        public Task SendTestEmailAsync(MailSettingsDto settings, string to, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class PauseAfterFirstSaveInterceptor : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _reached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _triggered;

        public Task Reached => _reached.Task;
        public bool FirstSaveCompletedInsideTransaction { get; private set; }

        public void Release() => _release.TrySetResult();

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _triggered, 1, 0) == 0)
            {
                FirstSaveCompletedInsideTransaction = eventData.Context?.Database.CurrentTransaction is not null;
                _reached.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }
}
