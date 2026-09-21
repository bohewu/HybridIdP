using System.Text.RegularExpressions;
using Core.Application;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class DirectoryCredentialOperatorResolutionServiceTests
{
    [Theory]
    [InlineData(NativeDirectoryCredentialOperationKind.NativeReset,
        DirectorySettlementDisposition.OriginalOperationSettled, false)]
    [InlineData(NativeDirectoryCredentialOperationKind.NativeReset,
        DirectorySettlementDisposition.ApprovedOutOfBandRecoveryCompleted, true)]
    [InlineData(NativeDirectoryCredentialOperationKind.AdminTemporaryIssue,
        DirectorySettlementDisposition.OriginalOperationSettled, true)]
    [InlineData(NativeDirectoryCredentialOperationKind.RequiredChange,
        DirectorySettlementDisposition.ApprovedOutOfBandRecoveryCompleted, true)]
    public async Task TwoActorSettlement_ExactOwnedCredential_FinalizesWithPerKindSemantics(
        NativeDirectoryCredentialOperationKind kind,
        DirectorySettlementDisposition disposition,
        bool expectedRequiredChange)
    {
        await using var fixture = await Fixture.CreateAsync(kind);
        fixture.Authorizations.Add(new object());
        fixture.Tokens.Add(new object());
        await fixture.AddTargetSessionAsync();
        var originalStamp = fixture.User.SecurityStamp;

        var prepared = await fixture.PrepareAndClaimAsync(disposition);
        var result = await fixture.Service.VerifyAndFinalizeAsync(fixture.VerificationRequest(prepared));

        Assert.Equal(DirectorySettlementVerificationOutcome.Resolved, result);
        fixture.Context.ChangeTracker.Clear();
        var user = await fixture.Context.Users.SingleAsync(candidate => candidate.Id == fixture.User.Id);
        var attempt = await fixture.Context.NativeDirectoryRecoveryAttempts.SingleAsync();
        var preparation = await fixture.Context.DirectorySettlementPreparations.SingleAsync();
        Assert.Equal(expectedRequiredChange, user.RequiresPasswordChange);
        Assert.Equal(fixture.OriginalPasswordHash, user.PasswordHash);
        Assert.NotEqual(originalStamp, user.SecurityStamp);
        Assert.Equal(NativeDirectoryRecoveryStatus.OperatorResolved, attempt.Status);
        Assert.Equal(DirectorySettlementPreparationStatus.Consumed, preparation.Status);
        Assert.NotNull((await fixture.Context.UserSessions.SingleAsync()).RevokedUtc);
        Assert.Equal(1, fixture.VerifyCalls);
        Assert.Equal(1, fixture.AuthorizationRevocations);
        Assert.Equal(1, fixture.TokenRevocations);
    }

    [Fact]
    public async Task PrepareAsync_RechecksAuthorizationPermissionEvidenceAndSingleActivePreparation()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Authorized = false;
        Assert.Equal(DirectoryCredentialOperatorResolutionOutcome.Unauthorized,
            (await fixture.Service.PrepareAsync(fixture.PreparationRequest())).Outcome);
        fixture.Authorized = true;
        Assert.Equal(DirectoryCredentialOperatorResolutionOutcome.Unavailable,
            (await fixture.Service.PrepareAsync(fixture.PreparationRequest() with { OriginalWritersDrained = false })).Outcome);
        Assert.Equal(DirectoryCredentialOperatorResolutionOutcome.Unavailable,
            (await fixture.Service.PrepareAsync(fixture.PreparationRequest() with { EvidenceReference = "free text evidence" })).Outcome);

        var first = await fixture.Service.PrepareAsync(fixture.PreparationRequest());
        var second = await fixture.Service.PrepareAsync(fixture.PreparationRequest());

        Assert.Equal(DirectoryCredentialOperatorResolutionOutcome.Available, first.Outcome);
        Assert.Equal(DirectoryCredentialOperatorResolutionOutcome.Unavailable, second.Outcome);
        Assert.Single(await fixture.Context.DirectorySettlementPreparations.ToListAsync());
        Assert.Equal(0, fixture.VerifyCalls);
        Assert.True(await fixture.HasBarrierAsync());
    }

    [Fact]
    public async Task Settlement_WhenCapabilityOrVerifiedDestinationUnavailable_FailsClosed()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.RecoveryOptions.PendingDirectorySettlementEnabled = false;
        Assert.Equal(DirectoryCredentialOperatorResolutionOutcome.Unavailable,
            (await fixture.Service.PrepareAsync(fixture.PreparationRequest())).Outcome);
        fixture.RecoveryOptions.PendingDirectorySettlementEnabled = true;
        fixture.PolicyEnabled = false;
        Assert.Equal(DirectoryCredentialOperatorResolutionOutcome.Unavailable,
            (await fixture.Service.PrepareAsync(fixture.PreparationRequest())).Outcome);
        Assert.Empty(fixture.EmailBodies);
        Assert.True(await fixture.HasBarrierAsync());
    }

    [Theory]
    [InlineData(DirectoryCredentialOutcome.PasswordChangeRequired)]
    [InlineData(DirectoryCredentialOutcome.InvalidCredentials)]
    [InlineData(DirectoryCredentialOutcome.Disabled)]
    [InlineData(DirectoryCredentialOutcome.Locked)]
    [InlineData(DirectoryCredentialOutcome.Ineligible)]
    [InlineData(DirectoryCredentialOutcome.Unavailable)]
    public async Task VerifyAndFinalizeAsync_NonAuthenticatedExactGuidResult_CancelsWithoutRetry(
        DirectoryCredentialOutcome outcome)
    {
        await using var fixture = await Fixture.CreateAsync();
        var prepared = await fixture.PrepareAndClaimAsync();
        fixture.VerificationOutcome = outcome;

        var result = await fixture.Service.VerifyAndFinalizeAsync(fixture.VerificationRequest(prepared));

        Assert.Equal(DirectorySettlementVerificationOutcome.Unavailable, result);
        Assert.Equal(1, fixture.VerifyCalls);
        Assert.True(await fixture.HasBarrierAsync());
        Assert.Equal(fixture.OriginalPasswordHash,
            (await fixture.Context.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == fixture.User.Id)).PasswordHash);
    }

    [Fact]
    public async Task VerifyAndFinalizeAsync_WrongOwnershipOrBrowserContext_DoesNotCallDirectory()
    {
        await using var fixture = await Fixture.CreateAsync();
        var prepared = await fixture.PrepareAndClaimAsync();

        var wrongCode = await fixture.Service.VerifyAndFinalizeAsync(
            fixture.VerificationRequest(prepared) with { OwnershipCode = "000000" });
        var wrongContext = await fixture.Service.VerifyAndFinalizeAsync(
            fixture.VerificationRequest(prepared) with { Context = new("other", "csrf") });

        Assert.Equal(DirectorySettlementVerificationOutcome.Unavailable, wrongCode);
        Assert.Equal(DirectorySettlementVerificationOutcome.Unavailable, wrongContext);
        Assert.Equal(0, fixture.VerifyCalls);
        Assert.True(await fixture.HasBarrierAsync());
    }

    [Fact]
    public async Task VerifyAndFinalizeAsync_OperatorPermissionLossDeniesBeforeDirectoryCall()
    {
        await using var fixture = await Fixture.CreateAsync();
        var prepared = await fixture.PrepareAndClaimAsync();
        await fixture.Context.UserRoles.ExecuteDeleteAsync();
        fixture.Context.ChangeTracker.Clear();

        var result = await fixture.Service.VerifyAndFinalizeAsync(fixture.VerificationRequest(prepared));

        Assert.Equal(DirectorySettlementVerificationOutcome.Unavailable, result);
        Assert.Equal(0, fixture.VerifyCalls);
        Assert.True(await fixture.HasBarrierAsync());
    }

    [Fact]
    public async Task VerifyAndFinalizeAsync_ReplayOnlyFirstConsumerCanFinalize()
    {
        await using var fixture = await Fixture.CreateAsync();
        var prepared = await fixture.PrepareAndClaimAsync();

        var first = await fixture.Service.VerifyAndFinalizeAsync(fixture.VerificationRequest(prepared));
        var replay = await fixture.Service.VerifyAndFinalizeAsync(fixture.VerificationRequest(prepared));

        Assert.Equal(DirectorySettlementVerificationOutcome.Resolved, first);
        Assert.Equal(DirectorySettlementVerificationOutcome.Unavailable, replay);
        Assert.Equal(1, fixture.VerifyCalls);
    }

    [Fact]
    public async Task VerifyAndFinalizeAsync_RevocationFailureRollsBackConsumptionAndTerminalization()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Tokens.Add(new object());
        fixture.FailTokenRevocation = true;
        var prepared = await fixture.PrepareAndClaimAsync();
        var originalStamp = fixture.User.SecurityStamp;

        var result = await fixture.Service.VerifyAndFinalizeAsync(fixture.VerificationRequest(prepared));

        Assert.Equal(DirectorySettlementVerificationOutcome.Unavailable, result);
        fixture.Context.ChangeTracker.Clear();
        var attempt = await fixture.Context.NativeDirectoryRecoveryAttempts.SingleAsync();
        var preparation = await fixture.Context.DirectorySettlementPreparations.SingleAsync();
        var user = await fixture.Context.Users.SingleAsync(candidate => candidate.Id == fixture.User.Id);
        Assert.NotEqual(NativeDirectoryRecoveryStatus.OperatorResolved, attempt.Status);
        Assert.NotEqual(DirectorySettlementPreparationStatus.Consumed, preparation.Status);
        Assert.Equal(originalStamp, user.SecurityStamp);
        Assert.Equal(fixture.OriginalPasswordHash, user.PasswordHash);
        Assert.True(await fixture.HasBarrierAsync());
    }

    [Fact]
    public async Task CancelAsync_LeavesAttemptBarrierAndInvalidatesContinuation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var prepared = await fixture.Service.PrepareAsync(fixture.PreparationRequest());
        var continuation = fixture.Continuation;

        var cancelled = await fixture.Service.CancelAsync(fixture.Actor.Id, prepared.PreparationId!.Value);
        var claim = await fixture.Service.ClaimAsync(new(continuation, fixture.ContextBinding));

        Assert.Equal(DirectoryCredentialOperatorResolutionOutcome.Resolved, cancelled);
        Assert.Equal(DirectorySettlementVerificationOutcome.Unavailable, claim.Outcome);
        Assert.True(await fixture.HasBarrierAsync());
    }

    [Fact]
    public async Task CancelUserAsync_RequiresBoundContextAndLeavesBarrier()
    {
        await using var fixture = await Fixture.CreateAsync();
        var prepared = await fixture.PrepareAndClaimAsync();

        var wrongContext = await fixture.Service.CancelUserAsync(prepared, new("other", "context"));
        var cancelled = await fixture.Service.CancelUserAsync(prepared, fixture.ContextBinding);

        Assert.Equal(DirectorySettlementVerificationOutcome.Unavailable, wrongContext);
        Assert.Equal(DirectorySettlementVerificationOutcome.Resolved, cancelled);
        Assert.Equal(DirectorySettlementPreparationStatus.Cancelled,
            (await fixture.Context.DirectorySettlementPreparations.AsNoTracking().SingleAsync()).Status);
        Assert.True(await fixture.HasBarrierAsync());
    }

    [Fact]
    public async Task CancelUserAsync_WhileDirectoryVerificationPending_PreventsFinalizationAndLeavesBarrier()
    {
        await using var fixture = await Fixture.CreateAsync();
        var prepared = await fixture.PrepareAndClaimAsync();
        fixture.VerificationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.VerificationRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var verificationTask = fixture.Service.VerifyAndFinalizeAsync(fixture.VerificationRequest(prepared));
        DirectorySettlementVerificationOutcome cancelled;
        try
        {
            await fixture.VerificationStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancelled = await fixture.Service.CancelUserAsync(prepared, fixture.ContextBinding);
        }
        finally
        {
            fixture.VerificationRelease.TrySetResult(true);
        }
        var returningVerifier = await verificationTask;

        Assert.Equal(DirectorySettlementVerificationOutcome.Resolved, cancelled);
        Assert.Equal(DirectorySettlementVerificationOutcome.Unavailable, returningVerifier);
        Assert.Equal(DirectorySettlementPreparationStatus.Cancelled,
            (await fixture.Context.DirectorySettlementPreparations.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(1, fixture.VerifyCalls);
        Assert.True(await fixture.HasBarrierAsync());
    }

    [Fact]
    public async Task VerifyAndFinalizeAsync_ExpiredOperatorWindowDeniesWithoutDirectoryCall()
    {
        await using var fixture = await Fixture.CreateAsync();
        var prepared = await fixture.PrepareAndClaimAsync();
        fixture.Time.Advance(TimeSpan.FromMinutes(6));

        var result = await fixture.Service.VerifyAndFinalizeAsync(fixture.VerificationRequest(prepared));

        Assert.Equal(DirectorySettlementVerificationOutcome.Unavailable, result);
        Assert.Equal(0, fixture.VerifyCalls);
        Assert.True(await fixture.HasBarrierAsync());
    }

    [Fact]
    public async Task PrepareAsync_AfterExpiryCreatesFreshPreparationWithoutClearingBarrier()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(DirectoryCredentialOperatorResolutionOutcome.Available,
            (await fixture.Service.PrepareAsync(fixture.PreparationRequest())).Outcome);
        fixture.Time.Advance(TimeSpan.FromMinutes(11));
        fixture.Context.ChangeTracker.Clear();
        var currentVersion = await fixture.Context.NativeDirectoryRecoveryAttempts.AsNoTracking()
            .Where(candidate => candidate.Id == fixture.Attempt.Id).Select(candidate => candidate.Version).SingleAsync();

        var fresh = await fixture.Service.PrepareAsync(
            fixture.PreparationRequest() with { ExpectedVersion = currentVersion });

        Assert.Equal(DirectoryCredentialOperatorResolutionOutcome.Available, fresh.Outcome);
        var preparations = (await fixture.Context.DirectorySettlementPreparations.AsNoTracking().ToListAsync())
            .OrderBy(candidate => candidate.CreatedAtUtc).ToList();
        Assert.Equal(2, preparations.Count);
        Assert.Equal(DirectorySettlementPreparationStatus.Cancelled, preparations[0].Status);
        Assert.Equal(DirectorySettlementPreparationStatus.Prepared, preparations[1].Status);
        Assert.True(await fixture.HasBarrierAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private Fixture(SqliteConnection connection, ApplicationDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public ApplicationDbContext Context { get; }
        public DirectoryCredentialOperatorResolutionService Service { get; private set; } = null!;
        public ApplicationUser User { get; private set; } = null!;
        public ApplicationUser Actor { get; private set; } = null!;
        public NativeDirectoryRecoveryAttempt Attempt { get; private set; } = null!;
        public Guid DirectoryObjectId { get; private set; }
        public string OriginalPasswordHash { get; private set; } = string.Empty;
        public ForgotPasswordRecoveryOptions RecoveryOptions { get; } = new()
        {
            PendingDirectorySettlementEnabled = true,
            PendingDirectorySettlementLifetimeMinutes = 10,
            PendingDirectorySettlementAuthorizationMinutes = 5,
            NativeOtpMaxAttempts = 5
        };
        public bool Authorized { get; set; } = true;
        public bool PolicyEnabled { get; set; } = true;
        public bool FailTokenRevocation { get; set; }
        public DirectoryCredentialOutcome VerificationOutcome { get; set; } = DirectoryCredentialOutcome.Authenticated;
        public TaskCompletionSource<bool>? VerificationStarted { get; set; }
        public TaskCompletionSource<bool>? VerificationRelease { get; set; }
        public int VerifyCalls { get; private set; }
        public int AuthorizationRevocations { get; private set; }
        public int TokenRevocations { get; private set; }
        public List<string> EmailBodies { get; } = [];
        public List<object> Authorizations { get; } = [];
        public List<object> Tokens { get; } = [];
        public MutableTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);
        public NativeRecoveryContext ContextBinding { get; } = new("server-browser-context", "server-csrf-context");
        public string Continuation => EmailBodies[0].Split(": ", 2)[1];
        public string OwnershipCode => Regex.Match(EmailBodies[1], "[0-9]{6}").Value;

        public static async Task<Fixture> CreateAsync(
            NativeDirectoryCredentialOperationKind operationKind = NativeDirectoryCredentialOperationKind.AdminTemporaryIssue)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            var fixture = new Fixture(connection, context);
            fixture.DirectoryObjectId = Guid.NewGuid();
            fixture.User = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = "settlement-user", NormalizedUserName = "SETTLEMENT-USER",
                IsActive = true, RequiresPasswordChange = false, SecurityStamp = "target-stamp",
                PasswordHash = "retained-local-password-hash"
            };
            fixture.Actor = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = "settlement-operator", NormalizedUserName = "SETTLEMENT-OPERATOR",
                IsActive = true, SecurityStamp = "operator-stamp"
            };
            fixture.OriginalPasswordHash = fixture.User.PasswordHash;
            var role = new ApplicationRole
            {
                Id = Guid.NewGuid(), Name = "RecoveryOperator", NormalizedName = "RECOVERYOPERATOR",
                Permissions = Permissions.Users.Update
            };
            var binding = new ProviderSubjectDirectoryBinding(fixture.User.Id, "provider", "subject",
                fixture.DirectoryObjectId, DateTime.UtcNow, fixture.User.UserName);
            var migration = new CredentialMigrationStateRecord(fixture.User.Id, binding.Id, DateTimeOffset.UtcNow);
            migration.Advance(CredentialMigrationState.ProofValidated, EffectiveEmailOtpRequirement.NotRequired,
                DateTimeOffset.UtcNow);
            migration.Advance(CredentialMigrationState.DirectoryCredentialCommitted, DateTimeOffset.UtcNow);
            migration.Advance(CredentialMigrationState.LocalFinalized, DateTimeOffset.UtcNow);
            var recoveryEmail = new RecoveryEmailRecord(fixture.User.Id, "user@example.test", "USER@EXAMPLE.TEST",
                DateTimeOffset.UtcNow);
            recoveryEmail.MarkVerified(DateTimeOffset.UtcNow);
            if (operationKind == NativeDirectoryCredentialOperationKind.NativeReset)
            {
                var nativeChallenge = new RecoveryProofChallenge(recoveryEmail.Id, fixture.User.Id,
                    RecoveryProofPurpose.NativePasswordRecovery, "native-code-hash", DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.AddMinutes(10));
                fixture.Attempt = new NativeDirectoryRecoveryAttempt(nativeChallenge.Id, fixture.User.Id,
                    fixture.DirectoryObjectId, DateTimeOffset.UtcNow);
                context.RecoveryProofChallenges.Add(nativeChallenge);
            }
            else
            {
                fixture.Attempt = new NativeDirectoryRecoveryAttempt(fixture.User.Id, fixture.DirectoryObjectId,
                    operationKind, DateTimeOffset.UtcNow);
            }
            context.Users.AddRange(fixture.User, fixture.Actor);
            context.Roles.Add(role);
            context.UserRoles.Add(new IdentityUserRole<Guid> { UserId = fixture.Actor.Id, RoleId = role.Id });
            context.ProviderSubjectDirectoryBindings.Add(binding);
            context.CredentialMigrationStateRecords.Add(migration);
            context.RecoveryEmails.Add(recoveryEmail);
            context.NativeDirectoryRecoveryAttempts.Add(fixture.Attempt);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var verifier = new Mock<IDirectoryCredentialVerifier>();
            verifier.Setup(service => service.VerifyCredentialAsync(fixture.DirectoryObjectId,
                    "known-current-credential", It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    fixture.VerifyCalls++;
                    fixture.VerificationStarted?.TrySetResult(true);
                    if (fixture.VerificationRelease is not null)
                    {
                        await fixture.VerificationRelease.Task;
                    }
                    return new DirectoryCredentialVerificationResult(fixture.VerificationOutcome,
                        new ManagedDirectoryIdentity(fixture.DirectoryObjectId, "settlement-user", true, true, false));
                });
            var userManager = CreateUserManager();
            userManager.Setup(manager => manager.UpdateSecurityStampAsync(It.IsAny<ApplicationUser>()))
                .ReturnsAsync((ApplicationUser user) =>
                {
                    user.SecurityStamp = Guid.NewGuid().ToString();
                    return IdentityResult.Success;
                });
            var authorizer = new Mock<IRecoveryProofAuthorizer>();
            authorizer.Setup(service => service.IsAdministratorAuthorizedAsync(fixture.Actor.Id,
                    It.IsAny<CancellationToken>())).ReturnsAsync(() => fixture.Authorized);
            var audit = new Mock<IRecoveryProofAudit>();
            audit.Setup(service => service.RecordAsync(It.IsAny<RecoveryProofAuditEvent>(),
                It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var policy = new Mock<IRecoveryVerificationPolicyEvaluator>();
            policy.Setup(service => service.EvaluateAsync(fixture.User.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new RecoveryVerificationPolicyDecision(fixture.PolicyEnabled, "period", true, true,
                    false, false, RecoveryPeriodDisposition.Satisfied,
                    new RecoveryEmailPolicyDecision("user@example.test", RecoveryEmailAddressSource.LocalRecord,
                        RecoveryEmailPolicyTrustOrigin.LocallyVerified, true, false, true)));
            var email = new Mock<IEmailService>();
            email.Setup(service => service.SendEmailAsync("user@example.test", It.IsAny<string>(), It.IsAny<string>(),
                    false, It.IsAny<CancellationToken>()))
                .Callback((string _, string _, string body, bool _, CancellationToken _) => fixture.EmailBodies.Add(body))
                .Returns(Task.CompletedTask);
            var authorizations = new Mock<IOpenIddictAuthorizationManager>();
            authorizations.Setup(manager => manager.FindBySubjectAsync(fixture.User.Id.ToString(),
                It.IsAny<CancellationToken>())).Returns(() => AsAsyncEnumerable(fixture.Authorizations));
            authorizations.Setup(manager => manager.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("authorization-id");
            authorizations.Setup(manager => manager.TryRevokeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => { fixture.AuthorizationRevocations++; return true; });
            var tokens = new Mock<IOpenIddictTokenManager>();
            tokens.Setup(manager => manager.FindBySubjectAsync(fixture.User.Id.ToString(), It.IsAny<CancellationToken>()))
                .Returns(() => AsAsyncEnumerable(fixture.Tokens));
            tokens.Setup(manager => manager.FindByAuthorizationIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(() => AsAsyncEnumerable(Array.Empty<object>()));
            tokens.Setup(manager => manager.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((object token, CancellationToken _) => fixture.Tokens.IndexOf(token).ToString());
            tokens.Setup(manager => manager.TryRevokeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => { fixture.TokenRevocations++; return !fixture.FailTokenRevocation; });

            fixture.Service = new DirectoryCredentialOperatorResolutionService(context, userManager.Object,
                verifier.Object, authorizations.Object, tokens.Object, authorizer.Object, audit.Object, policy.Object,
                email.Object, new PasswordHasher<ApplicationUser>(), Options.Create(fixture.RecoveryOptions),
                Options.Create(new DirectoryIntegrationOptions { Enabled = true }), fixture.Time);
            return fixture;
        }

        public DirectorySettlementPreparationRequest PreparationRequest(
            DirectorySettlementDisposition disposition = DirectorySettlementDisposition.OriginalOperationSettled) => new(
            Actor.Id, User.Id, Attempt.Id, Attempt.Version, Attempt.OperationKind, Attempt.Status, DirectoryObjectId, true,
            disposition,
            disposition == DirectorySettlementDisposition.OriginalOperationSettled
                ? DirectorySettlementEvidenceCategory.ApprovedDirectoryOperation
                : DirectorySettlementEvidenceCategory.ApprovedRecoveryOperation,
            "CASE:20260911-001");

        public async Task<Guid> PrepareAndClaimAsync(
            DirectorySettlementDisposition disposition = DirectorySettlementDisposition.OriginalOperationSettled)
        {
            var preparation = await Service.PrepareAsync(PreparationRequest(disposition));
            Assert.Equal(DirectoryCredentialOperatorResolutionOutcome.Available, preparation.Outcome);
            var claim = await Service.ClaimAsync(new(Continuation, ContextBinding));
            Assert.Equal(DirectorySettlementVerificationOutcome.ChallengeIssued, claim.Outcome);
            return claim.PreparationId!.Value;
        }

        public DirectorySettlementVerificationRequest VerificationRequest(Guid preparationId) =>
            new(preparationId, OwnershipCode, "known-current-credential", ContextBinding);
        public Task<bool> HasBarrierAsync() => Context.NativeDirectoryRecoveryAttempts.AsNoTracking().AnyAsync(attempt =>
            attempt.LocalAccountId == User.Id && (attempt.Status == NativeDirectoryRecoveryStatus.Reserved ||
                attempt.Status == NativeDirectoryRecoveryStatus.ReconciliationRequired));
        public async Task AddTargetSessionAsync()
        {
            Context.UserSessions.Add(new UserSession { UserId = User.Id, AuthorizationId = Guid.NewGuid().ToString() });
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
        }
        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
        private static Mock<UserManager<ApplicationUser>> CreateUserManager()
        {
            var store = new Mock<IUserStore<ApplicationUser>>();
            return new Mock<UserManager<ApplicationUser>>(store.Object, null, null, null, null, null, null, null, null);
        }
        private static async IAsyncEnumerable<object> AsAsyncEnumerable(IEnumerable<object> items)
        {
            foreach (var item in items) yield return item;
            await Task.CompletedTask;
        }

        public sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
        {
            private DateTimeOffset _utcNow = utcNow;
            public override DateTimeOffset GetUtcNow() => _utcNow;
            public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
        }
    }
}
