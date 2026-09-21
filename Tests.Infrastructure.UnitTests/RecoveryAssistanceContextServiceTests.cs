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

public sealed class RecoveryAssistanceContextServiceTests
{
    [Fact]
    public async Task GetAsync_EligibleLocalAccount_ExposesOrdinaryAndTemporaryActions()
    {
        await using var fixture = CreateFixture();
        var user = await fixture.AddLocalUserAsync(withOrdinaryChallenge: true);

        var result = await fixture.Service.GetAsync(fixture.ActorId, user.Id);

        Assert.Equal(RecoveryAssistanceContextOutcome.Available, result.Outcome);
        Assert.Equal("available", result.Context!.Ordinary.State);
        Assert.True(result.Context.Ordinary.ResendOtp);
        Assert.True(result.Context.Ordinary.ReplaceRecoveryEmail);
        Assert.True(result.Context.Ordinary.ApproveReset);
        Assert.Equal("available", result.Context.Temporary.State);
        Assert.True(result.Context.Temporary.IssueTemporaryCredential);
        Assert.Equal("unavailable", result.Context.Migration.State);
        Assert.Equal("unavailable", result.Context.Pending.State);
    }

    [Fact]
    public async Task GetAsync_ExactActiveMigration_ExposesOnlyMigrationActions()
    {
        await using var fixture = CreateFixture();
        var user = await fixture.AddMigrationUserAsync(
            activeContinuationCount: 1,
            recoveryEmailVerified: true);

        var result = await fixture.Service.GetAsync(fixture.ActorId, user.Id);

        Assert.Equal("unavailable", result.Context!.Ordinary.State);
        Assert.Equal("unavailable", result.Context.Temporary.State);
        Assert.Equal("available", result.Context.Migration.State);
        Assert.True(result.Context.Migration.ResendOtp);
        Assert.True(result.Context.Migration.ReplaceRecoveryEmail);
        Assert.True(result.Context.Migration.ApproveReset);
        Assert.Equal("unavailable", result.Context.Pending.State);
    }

    [Fact]
    public async Task GetAsync_ActiveMigrationWithUnverifiedRecoveryEmail_DisablesOnlyResend()
    {
        await using var fixture = CreateFixture();
        var user = await fixture.AddMigrationUserAsync(
            activeContinuationCount: 1,
            recoveryEmailVerified: false);

        var result = await fixture.Service.GetAsync(fixture.ActorId, user.Id);

        Assert.Equal("available", result.Context!.Migration.State);
        Assert.False(result.Context.Migration.ResendOtp);
        Assert.True(result.Context.Migration.ReplaceRecoveryEmail);
        Assert.True(result.Context.Migration.ApproveReset);
    }

    [Fact]
    public async Task GetAsync_AmbiguousActiveMigration_FailsClosed()
    {
        await using var fixture = CreateFixture();
        var user = await fixture.AddMigrationUserAsync(activeContinuationCount: 2);

        var result = await fixture.Service.GetAsync(fixture.ActorId, user.Id);

        Assert.Equal("unavailable", result.Context!.Migration.State);
        Assert.False(result.Context.Migration.ResendOtp);
        Assert.False(result.Context.Migration.ReplaceRecoveryEmail);
        Assert.False(result.Context.Migration.ApproveReset);
    }

    [Fact]
    public async Task GetAsync_Stage1BindingWithStaleLocalHash_GrantsNoAuthority()
    {
        await using var fixture = CreateFixture();
        var user = await fixture.AddStage1UserAsync();

        var result = await fixture.Service.GetAsync(fixture.ActorId, user.Id);

        Assert.Equal("unavailable", result.Context!.Ordinary.State);
        Assert.Equal("unavailable", result.Context.Temporary.State);
        Assert.Equal("unavailable", result.Context.Migration.State);
        Assert.Equal("unavailable", result.Context.Pending.State);
    }

    [Fact]
    public async Task GetAsync_ExactPendingAttempt_SuppressesEveryOtherActionFamily()
    {
        await using var fixture = CreateFixture();
        var account = await fixture.AddFinalizedDirectoryUserAsync(withVerifiedRecoveryEmail: true);
        var attempt = new NativeDirectoryRecoveryAttempt(
            account.User.Id,
            account.Binding.DirectoryObjectId,
            NativeDirectoryCredentialOperationKind.AdminTemporaryIssue,
            fixture.Time.GetUtcNow());
        fixture.Context.NativeDirectoryRecoveryAttempts.Add(attempt);
        await fixture.Context.SaveChangesAsync();
        fixture.OperatorResolution
            .Setup(service => service.GetPendingAsync(
                fixture.ActorId,
                account.User.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryCredentialOperatorResolutionLookupResult(
                DirectoryCredentialOperatorResolutionOutcome.Available,
                new DirectoryCredentialOperatorResolutionAttempt(
                    attempt.Id,
                    attempt.Version,
                    attempt.OperationKind,
                    attempt.Status,
                    attempt.DirectoryObjectId)));

        var result = await fixture.Service.GetAsync(fixture.ActorId, account.User.Id);

        Assert.Equal("unavailable", result.Context!.Ordinary.State);
        Assert.Equal("unavailable", result.Context.Temporary.State);
        Assert.Equal("unavailable", result.Context.Migration.State);
        Assert.Equal("available", result.Context.Pending.State);
        Assert.True(result.Context.Pending.Inspect);
        Assert.True(result.Context.Pending.PrepareSettlement);
    }

    [Fact]
    public async Task GetAsync_PendingAttemptWithoutVerifiedDestination_PreservesInspectionOnly()
    {
        await using var fixture = CreateFixture();
        var account = await fixture.AddFinalizedDirectoryUserAsync(withVerifiedRecoveryEmail: false);
        var attempt = new NativeDirectoryRecoveryAttempt(
            account.User.Id,
            account.Binding.DirectoryObjectId,
            NativeDirectoryCredentialOperationKind.AdminTemporaryIssue,
            fixture.Time.GetUtcNow());
        fixture.Context.NativeDirectoryRecoveryAttempts.Add(attempt);
        await fixture.Context.SaveChangesAsync();
        fixture.OperatorResolution
            .Setup(service => service.GetPendingAsync(
                fixture.ActorId,
                account.User.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryCredentialOperatorResolutionLookupResult(
                DirectoryCredentialOperatorResolutionOutcome.Available,
                new DirectoryCredentialOperatorResolutionAttempt(
                    attempt.Id,
                    attempt.Version,
                    attempt.OperationKind,
                    attempt.Status,
                    attempt.DirectoryObjectId)));

        var result = await fixture.Service.GetAsync(fixture.ActorId, account.User.Id);

        Assert.Equal("available", result.Context!.Pending.State);
        Assert.True(result.Context.Pending.Inspect);
        Assert.False(result.Context.Pending.PrepareSettlement);
    }

    [Fact]
    public async Task GetAsync_PendingAttemptWithActivePreparation_PreservesInspectionOnly()
    {
        await using var fixture = CreateFixture();
        var account = await fixture.AddFinalizedDirectoryUserAsync(withVerifiedRecoveryEmail: true);
        var attempt = new NativeDirectoryRecoveryAttempt(
            account.User.Id,
            account.Binding.DirectoryObjectId,
            NativeDirectoryCredentialOperationKind.RequiredChange,
            fixture.Time.GetUtcNow());
        fixture.Context.NativeDirectoryRecoveryAttempts.Add(attempt);
        fixture.Context.DirectorySettlementPreparations.Add(new DirectorySettlementPreparation(
            attempt.Id,
            attempt.Version,
            attempt.OperationKind,
            attempt.Status,
            account.User.Id,
            account.Binding.DirectoryObjectId,
            account.Binding.Id,
            account.Migration.Id,
            account.Migration.Version,
            account.RecoveryEmail!.Id,
            account.RecoveryEmail.Version,
            account.User.SecurityStamp!,
            fixture.ActorId,
            "operator-security-stamp",
            fixture.Time.GetUtcNow(),
            fixture.Time.GetUtcNow().AddMinutes(5),
            DirectorySettlementDisposition.OriginalOperationSettled,
            DirectorySettlementEvidenceCategory.ApprovedDirectoryOperation,
            "evidence-reference",
            "hashed-continuation",
            fixture.Time.GetUtcNow(),
            fixture.Time.GetUtcNow().AddMinutes(10)));
        await fixture.Context.SaveChangesAsync();
        fixture.OperatorResolution
            .Setup(service => service.GetPendingAsync(
                fixture.ActorId,
                account.User.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryCredentialOperatorResolutionLookupResult(
                DirectoryCredentialOperatorResolutionOutcome.Available,
                new DirectoryCredentialOperatorResolutionAttempt(
                    attempt.Id,
                    attempt.Version,
                    attempt.OperationKind,
                    attempt.Status,
                    attempt.DirectoryObjectId)));

        var result = await fixture.Service.GetAsync(fixture.ActorId, account.User.Id);

        Assert.Equal("available", result.Context!.Pending.State);
        Assert.True(result.Context.Pending.Inspect);
        Assert.False(result.Context.Pending.PrepareSettlement);
    }

    [Fact]
    public async Task GetAsync_DisabledPendingCapability_StillSuppressesOtherActions()
    {
        await using var fixture = CreateFixture(pendingEnabled: false);
        var user = await fixture.AddLocalUserAsync(withOrdinaryChallenge: true);
        fixture.Context.NativeDirectoryRecoveryAttempts.Add(new NativeDirectoryRecoveryAttempt(
            user.Id,
            Guid.NewGuid(),
            NativeDirectoryCredentialOperationKind.RequiredChange,
            fixture.Time.GetUtcNow()));
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Service.GetAsync(fixture.ActorId, user.Id);

        Assert.Equal("unavailable", result.Context!.Ordinary.State);
        Assert.Equal("unavailable", result.Context.Temporary.State);
        Assert.Equal("unavailable", result.Context.Migration.State);
        Assert.Equal("disabled", result.Context.Pending.State);
        fixture.OperatorResolution.Verify(
            service => service.GetPendingAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetAsync_UnknownPendingState_FailsClosedWithoutIdentifiers()
    {
        await using var fixture = CreateFixture();
        var user = await fixture.AddLocalUserAsync(withOrdinaryChallenge: false);
        fixture.Context.NativeDirectoryRecoveryAttempts.AddRange(
            new NativeDirectoryRecoveryAttempt(
                user.Id,
                Guid.NewGuid(),
                NativeDirectoryCredentialOperationKind.AdminTemporaryIssue,
                fixture.Time.GetUtcNow()),
            new NativeDirectoryRecoveryAttempt(
                user.Id,
                Guid.NewGuid(),
                NativeDirectoryCredentialOperationKind.RequiredChange,
                fixture.Time.GetUtcNow()));
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Service.GetAsync(fixture.ActorId, user.Id);

        Assert.Equal("unavailable", result.Context!.Pending.State);
        Assert.False(result.Context.Pending.Inspect);
        Assert.False(result.Context.Pending.PrepareSettlement);
        Assert.DoesNotContain(user.Id.ToString(), System.Text.Json.JsonSerializer.Serialize(result.Context));
    }

    [Fact]
    public async Task GetAsync_UnauthorizedActor_ReturnsUnauthorized()
    {
        await using var fixture = CreateFixture(authorized: false);

        var result = await fixture.Service.GetAsync(fixture.ActorId, Guid.NewGuid());

        Assert.Equal(RecoveryAssistanceContextOutcome.Unauthorized, result.Outcome);
        Assert.Null(result.Context);
    }

    private static Fixture CreateFixture(
        bool authorized = true,
        bool pendingEnabled = true)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var context = new ApplicationDbContext(options);
        var authorizer = new Mock<IRecoveryProofAuthorizer>();
        authorizer.Setup(service => service.IsAdministratorAuthorizedAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(authorized);
        var operatorResolution = new Mock<IDirectoryCredentialOperatorResolutionService>(MockBehavior.Strict);
        var recoveryOptions = Options.Create(new ForgotPasswordRecoveryOptions
        {
            DeploymentCeiling = ForgotPasswordMode.Native,
            NativeRecoveryEnabled = true,
            NativeDirectoryRecoveryEnabled = true,
            AdminTemporaryCredentialsEnabled = true,
            OrdinaryRecoveryAssistanceEnabled = true,
            PendingDirectorySettlementEnabled = pendingEnabled
        });
        var migrationOptions = Options.Create(new CredentialMigrationOptions
        {
            Enabled = true,
            RecoveryEmailEnabled = true,
            MigrationEmailOtpEnabled = true,
            RecoveryAdminAssistanceEnabled = true
        });
        var directoryOptions = Options.Create(new DirectoryIntegrationOptions
        {
            Enabled = true,
            TemporaryCredentialCapabilityEnabled = true
        });
        var time = new FixedTimeProvider();
        var actorId = Guid.NewGuid();
        context.Users.Add(new ApplicationUser
        {
            Id = actorId,
            UserName = "operator-user",
            NormalizedUserName = "OPERATOR-USER",
            Email = "operator-user@example.test",
            NormalizedEmail = "OPERATOR-USER@EXAMPLE.TEST",
            IsActive = true,
            IsDeleted = false,
            SecurityStamp = "operator-security-stamp",
            PasswordHash = "operator-password-hash"
        });
        context.SaveChanges();
        var service = new RecoveryAssistanceContextService(
            context,
            authorizer.Object,
            new FixedPolicyEvaluator(context),
            new ForgotPasswordRoutingEvaluator(recoveryOptions),
            operatorResolution.Object,
            recoveryOptions,
            migrationOptions,
            directoryOptions,
            time);
        return new Fixture(context, operatorResolution, service, time, actorId);
    }

    private sealed class Fixture(
        ApplicationDbContext context,
        Mock<IDirectoryCredentialOperatorResolutionService> operatorResolution,
        RecoveryAssistanceContextService service,
        FixedTimeProvider time,
        Guid actorId) : IAsyncDisposable
    {
        public ApplicationDbContext Context { get; } = context;
        public Mock<IDirectoryCredentialOperatorResolutionService> OperatorResolution { get; } = operatorResolution;
        public RecoveryAssistanceContextService Service { get; } = service;
        public FixedTimeProvider Time { get; } = time;
        public Guid ActorId { get; } = actorId;

        public async Task<ApplicationUser> AddLocalUserAsync(bool withOrdinaryChallenge)
        {
            var user = CreateUser("local-user");
            Context.Users.Add(user);
            Context.SecurityPolicies.Add(new SecurityPolicy { ForgotPasswordMode = ForgotPasswordMode.Native });
            if (withOrdinaryChallenge)
            {
                var recoveryEmail = new RecoveryEmailRecord(
                    user.Id,
                    "recovery@example.test",
                    "RECOVERY@EXAMPLE.TEST",
                    Time.GetUtcNow().AddHours(-1));
                recoveryEmail.MarkVerified(Time.GetUtcNow().AddMinutes(-30));
                var challenge = new RecoveryProofChallenge(
                    recoveryEmail.Id,
                    user.Id,
                    RecoveryProofPurpose.NativePasswordRecovery,
                    "hashed-code",
                    Time.GetUtcNow(),
                    Time.GetUtcNow().AddMinutes(10));
                challenge.BindNativeAssistance(
                    "browser-context",
                    "csrf-context",
                    false,
                    null,
                    recoveryEmail.Version,
                    user.SecurityStamp!);
                Context.RecoveryEmails.Add(recoveryEmail);
                Context.RecoveryProofChallenges.Add(challenge);
            }
            await Context.SaveChangesAsync();
            return user;
        }

        public async Task<ApplicationUser> AddStage1UserAsync()
        {
            var user = CreateUser("stage1-user");
            var binding = CreateBinding(user);
            Context.Users.Add(user);
            Context.ProviderSubjectDirectoryBindings.Add(binding);
            Context.CredentialMigrationStateRecords.Add(
                new CredentialMigrationStateRecord(user.Id, binding.Id, Time.GetUtcNow()));
            Context.SecurityPolicies.Add(new SecurityPolicy { ForgotPasswordMode = ForgotPasswordMode.Native });
            await Context.SaveChangesAsync();
            return user;
        }

        public async Task<ApplicationUser> AddMigrationUserAsync(
            int activeContinuationCount,
            bool? recoveryEmailVerified = null)
        {
            var user = CreateUser("migration-user");
            var binding = CreateBinding(user);
            var state = new CredentialMigrationStateRecord(user.Id, binding.Id, Time.GetUtcNow());
            state.Advance(
                CredentialMigrationState.ProofValidated,
                EffectiveEmailOtpRequirement.Required,
                Time.GetUtcNow());
            Context.Users.Add(user);
            Context.ProviderSubjectDirectoryBindings.Add(binding);
            Context.CredentialMigrationStateRecords.Add(state);
            Context.SecurityPolicies.Add(new SecurityPolicy { ForgotPasswordMode = ForgotPasswordMode.Native });
            if (recoveryEmailVerified is not null)
            {
                var recoveryEmail = new RecoveryEmailRecord(
                    user.Id,
                    "migration-recovery@example.test",
                    "MIGRATION-RECOVERY@EXAMPLE.TEST",
                    Time.GetUtcNow().AddHours(-1));
                if (recoveryEmailVerified.Value)
                {
                    recoveryEmail.MarkVerified(Time.GetUtcNow().AddMinutes(-30));
                }
                Context.RecoveryEmails.Add(recoveryEmail);
            }
            for (var index = 0; index < activeContinuationCount; index++)
            {
                Context.CredentialMigrationContinuations.Add(new CredentialMigrationContinuationRecord(
                    state.Id,
                    $"token-hash-{index}",
                    $"context-hash-{index}",
                    $"csrf-hash-{index}",
                    Time.GetUtcNow(),
                    Time.GetUtcNow().AddMinutes(10)));
            }
            await Context.SaveChangesAsync();
            return user;
        }

        public async Task<(
            ApplicationUser User,
            ProviderSubjectDirectoryBinding Binding,
            CredentialMigrationStateRecord Migration,
            RecoveryEmailRecord? RecoveryEmail)> AddFinalizedDirectoryUserAsync(bool withVerifiedRecoveryEmail)
        {
            var user = CreateUser("directory-user");
            var binding = CreateBinding(user);
            var migration = new CredentialMigrationStateRecord(user.Id, binding.Id, Time.GetUtcNow());
            migration.Advance(
                CredentialMigrationState.ProofValidated,
                EffectiveEmailOtpRequirement.NotRequired,
                Time.GetUtcNow());
            migration.Advance(CredentialMigrationState.DirectoryCredentialCommitted, Time.GetUtcNow());
            migration.Advance(CredentialMigrationState.LocalFinalized, Time.GetUtcNow());
            RecoveryEmailRecord? recoveryEmail = null;
            if (withVerifiedRecoveryEmail)
            {
                recoveryEmail = new RecoveryEmailRecord(
                    user.Id,
                    "directory-recovery@example.test",
                    "DIRECTORY-RECOVERY@EXAMPLE.TEST",
                    Time.GetUtcNow().AddHours(-1));
                recoveryEmail.MarkVerified(Time.GetUtcNow().AddMinutes(-30));
                Context.RecoveryEmails.Add(recoveryEmail);
            }
            Context.Users.Add(user);
            Context.ProviderSubjectDirectoryBindings.Add(binding);
            Context.CredentialMigrationStateRecords.Add(migration);
            Context.SecurityPolicies.Add(new SecurityPolicy { ForgotPasswordMode = ForgotPasswordMode.Native });
            await Context.SaveChangesAsync();
            return (user, binding, migration, recoveryEmail);
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();

        private static ApplicationUser CreateUser(string name) => new()
        {
            Id = Guid.NewGuid(),
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            Email = $"{name}@example.test",
            NormalizedEmail = $"{name.ToUpperInvariant()}@EXAMPLE.TEST",
            IsActive = true,
            IsDeleted = false,
            SecurityStamp = "current-security-stamp",
            PasswordHash = "stale-or-local-password-hash"
        };

        private ProviderSubjectDirectoryBinding CreateBinding(ApplicationUser user) => new(
            user.Id,
            "directory",
            user.UserName!,
            Guid.NewGuid(),
            Time.GetUtcNow().UtcDateTime,
            user.UserName);
    }

    private sealed class FixedPolicyEvaluator(ApplicationDbContext context) : IRecoveryVerificationPolicyEvaluator
    {
        public async Task<RecoveryVerificationPolicyDecision> EvaluateAsync(
            Guid localAccountId,
            CancellationToken cancellationToken = default)
        {
            var address = await context.RecoveryEmails.AsNoTracking()
                .Where(email => email.LocalAccountId == localAccountId)
                .Select(email => email.Address)
                .SingleAsync(cancellationToken);
            return new RecoveryVerificationPolicyDecision(
                true,
                "period",
                true,
                true,
                false,
                false,
                RecoveryPeriodDisposition.Satisfied,
                new RecoveryEmailPolicyDecision(
                    address,
                    RecoveryEmailAddressSource.LocalRecord,
                    RecoveryEmailPolicyTrustOrigin.LocallyVerified,
                    true,
                    false,
                    false));
        }
    }

    public sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(2026, 9, 12, 4, 0, 0, TimeSpan.Zero);
    }
}
