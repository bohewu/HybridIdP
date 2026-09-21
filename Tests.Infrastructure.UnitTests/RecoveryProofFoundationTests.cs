using System.Security.Cryptography;
using System.Text;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class RecoveryProofFoundationTests
{
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
