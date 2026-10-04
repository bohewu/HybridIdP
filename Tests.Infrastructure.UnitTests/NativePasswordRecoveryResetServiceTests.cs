using Core.Application;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using HybridIdP.Infrastructure.Identity;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class NativePasswordRecoveryResetServiceTests
{
    private static readonly NativeRecoveryContext BrowserContext = new("browser-hash", "csrf-hash");

    [Fact]
    public async Task ResetAsync_SucceedsAndRevokesEverySession_WhenProofAndAccountRemainValid()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.User.LastPasswordChangeDate = fixture.Time.GetUtcNow().AddDays(-2).UtcDateTime;
        fixture.Policy.PasswordExpirationDays = 1;
        fixture.Policy.MinPasswordAgeDays = 30;
        await fixture.Context.SaveChangesAsync();
        await fixture.AddSessionAsync();
        fixture.Authorizations.Add(new object());
        fixture.Tokens.Add(new object());
        var proof = await fixture.CreateProofAsync();

        var result = await fixture.Service.ResetAsync(new NativeRecoveryResetRequest(
            proof.RequestId,
            proof.Proof!,
            "NewValid!Password2",
            BrowserContext));

        Assert.Equal(NativeRecoveryResetOutcome.Succeeded, result.Outcome);
        fixture.Context.ChangeTracker.Clear();
        var user = await fixture.Context.Users.SingleAsync(candidate => candidate.Id == fixture.User.Id);
        var challenge = await fixture.Context.RecoveryProofChallenges.SingleAsync(candidate => candidate.Id == proof.RequestId);
        var session = await fixture.Context.UserSessions.SingleAsync(candidate => candidate.UserId == fixture.User.Id);
        Assert.Equal(PasswordVerificationResult.Success,
            fixture.Hasher.VerifyHashedPassword(user, user.PasswordHash!, "NewValid!Password2"));
        Assert.NotEqual("old-security-stamp", user.SecurityStamp);
        Assert.Equal(fixture.Time.GetUtcNow().UtcDateTime, user.LastPasswordChangeDate);
        Assert.Contains(
            fixture.OriginalPasswordHash,
            System.Text.Json.JsonSerializer.Deserialize<List<string>>(user.PasswordHistory)!);
        Assert.NotNull(challenge.ConsumedAtUtc);
        var recoveryEmail = await fixture.Context.RecoveryEmails.SingleAsync(
            candidate => candidate.Id == fixture.RecoveryEmail.Id);
        Assert.Equal(challenge.VerifiedAtUtc, recoveryEmail.VerifiedAtUtc);
        var periodDecision = await fixture.EvaluatePeriodAsync(fixture.Time.GetUtcNow().AddMinutes(-1));
        Assert.True(periodDecision.HasCurrentPeriodVerification);
        Assert.Equal(RecoveryPeriodDisposition.Satisfied, periodDecision.PeriodDisposition);
        Assert.Equal("native-password-recovery", session.RevocationReason);
        Assert.Equal(1, fixture.AuthorizationRevocations);
        Assert.Equal(1, fixture.TokenRevocations);
        Assert.Equal(0, fixture.SyncCalls);
    }

    [Fact]
    public async Task ResetAsync_SourceBootstrapOtp_PromotesEmailAndAcceptsResultingProof()
    {
        await using var fixture = await Fixture.CreateAsync();
        var proof = await fixture.CreateSourceBootstrapProofAsync();

        var result = await fixture.Service.ResetAsync(new NativeRecoveryResetRequest(
            proof.RequestId,
            proof.Proof,
            "NewValid!Password2",
            BrowserContext));

        Assert.Equal(NativeRecoveryResetOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, fixture.DirectoryResetCalls);
        fixture.Context.ChangeTracker.Clear();
        Assert.NotNull((await fixture.Context.RecoveryEmails.SingleAsync()).VerifiedAtUtc);
        Assert.NotNull((await fixture.Context.RecoveryProofChallenges
            .SingleAsync(candidate => candidate.Id == proof.RequestId)).ConsumedAtUtc);
    }

    [Fact]
    public async Task ResetAsync_DoesNotSatisfyCurrentPeriod_WhenOtpWasVerifiedBeforeEffectiveTime()
    {
        await using var fixture = await Fixture.CreateAsync();
        var otpVerifiedAt = fixture.Time.GetUtcNow();
        var proof = await fixture.CreateProofAsync();
        var effectiveAt = otpVerifiedAt.AddMinutes(1);
        fixture.Time.SetUtcNow(effectiveAt.AddMinutes(1));

        var result = await fixture.Service.ResetAsync(new NativeRecoveryResetRequest(
            proof.RequestId,
            proof.Proof!,
            "NewValid!Password2",
            BrowserContext));

        Assert.Equal(NativeRecoveryResetOutcome.Succeeded, result.Outcome);
        fixture.Context.ChangeTracker.Clear();
        var challenge = await fixture.Context.RecoveryProofChallenges.SingleAsync(
            candidate => candidate.Id == proof.RequestId);
        var recoveryEmail = await fixture.Context.RecoveryEmails.SingleAsync(
            candidate => candidate.Id == fixture.RecoveryEmail.Id);
        Assert.Equal(otpVerifiedAt, challenge.VerifiedAtUtc);
        Assert.Equal(challenge.VerifiedAtUtc, recoveryEmail.VerifiedAtUtc);
        var periodDecision = await fixture.EvaluatePeriodAsync(effectiveAt);
        Assert.False(periodDecision.HasCurrentPeriodVerification);
        Assert.Equal(RecoveryPeriodDisposition.Required, periodDecision.PeriodDisposition);
    }

    [Fact]
    public async Task ResetAsync_PreservesLaterLocalVerification()
    {
        await using var fixture = await Fixture.CreateAsync();
        var laterVerification = fixture.Time.GetUtcNow().AddMinutes(2);
        fixture.RecoveryEmail.MarkVerified(laterVerification);
        await fixture.Context.SaveChangesAsync();
        var proof = await fixture.CreateProofAsync();

        var result = await fixture.Service.ResetAsync(new NativeRecoveryResetRequest(
            proof.RequestId,
            proof.Proof!,
            "NewValid!Password2",
            BrowserContext));

        Assert.Equal(NativeRecoveryResetOutcome.Succeeded, result.Outcome);
        fixture.Context.ChangeTracker.Clear();
        var recoveryEmail = await fixture.Context.RecoveryEmails.SingleAsync(
            candidate => candidate.Id == fixture.RecoveryEmail.Id);
        Assert.Equal(laterVerification, recoveryEmail.VerifiedAtUtc);
    }

    [Fact]
    public async Task ResetAsync_DeniesReplay()
    {
        await using var fixture = await Fixture.CreateAsync();
        var proof = await fixture.CreateProofAsync();
        var request = new NativeRecoveryResetRequest(
            proof.RequestId,
            proof.Proof!,
            "NewValid!Password2",
            BrowserContext);

        Assert.Equal(NativeRecoveryResetOutcome.Succeeded, (await fixture.Service.ResetAsync(request)).Outcome);
        Assert.Equal(NativeRecoveryResetOutcome.Denied, (await fixture.Service.ResetAsync(request)).Outcome);
        Assert.Equal(1, fixture.PasswordResetCalls);
    }

    [Fact]
    public async Task ResetAsync_ConsumesAdministrativeApprovalOnceThroughExistingLocalReset()
    {
        await using var fixture = await Fixture.CreateAsync(ordinaryRecoveryAssistanceEnabled: true);
        var requestId = await fixture.CreateUnverifiedChallengeAsync();
        await fixture.AddNativeApprovalAsync(requestId);
        var request = new NativeRecoveryResetRequest(
            requestId,
            string.Empty,
            "NewValid!Password2",
            BrowserContext,
            UseAdministrativeApproval: true);

        Assert.Equal(NativeRecoveryResetOutcome.Succeeded, (await fixture.Service.ResetAsync(request)).Outcome);
        Assert.Equal(NativeRecoveryResetOutcome.Denied, (await fixture.Service.ResetAsync(request)).Outcome);
        Assert.Equal(1, fixture.PasswordResetCalls);
        fixture.Context.ChangeTracker.Clear();
        Assert.NotNull((await fixture.Context.NativeRecoveryResetApprovals.SingleAsync()).ConsumedAtUtc);
        Assert.NotNull((await fixture.Context.RecoveryProofChallenges.SingleAsync(
            candidate => candidate.Id == requestId)).ConsumedAtUtc);
    }

    [Fact]
    public async Task ResetAsync_DeniesAdministrativeApprovalAfterSecurityStampChanges()
    {
        await using var fixture = await Fixture.CreateAsync(ordinaryRecoveryAssistanceEnabled: true);
        var requestId = await fixture.CreateUnverifiedChallengeAsync();
        await fixture.AddNativeApprovalAsync(requestId);
        fixture.User.SecurityStamp = "changed-security-stamp";
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Service.ResetAsync(new NativeRecoveryResetRequest(
            requestId,
            string.Empty,
            "NewValid!Password2",
            BrowserContext,
            UseAdministrativeApproval: true));

        Assert.Equal(NativeRecoveryResetOutcome.Denied, result.Outcome);
        Assert.Equal(0, fixture.PasswordResetCalls);
    }

    [Fact]
    public async Task ResetAsync_AdministrativeApprovalUsesDirectoryResetWithoutMutatingLocalHash()
    {
        await using var fixture = await Fixture.CreateAsync(ordinaryRecoveryAssistanceEnabled: true);
        var directoryObjectId = await fixture.ConfigureCompletedDirectoryAsync();
        var originalHash = fixture.User.PasswordHash;
        var requestId = await fixture.CreateUnverifiedChallengeAsync();
        await fixture.AddNativeApprovalAsync(requestId);

        var result = await fixture.Service.ResetAsync(new NativeRecoveryResetRequest(
            requestId,
            string.Empty,
            "NewValid!Password2",
            BrowserContext,
            UseAdministrativeApproval: true));

        Assert.Equal(NativeRecoveryResetOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, fixture.DirectoryResetCalls);
        Assert.Equal(directoryObjectId, fixture.LastDirectoryObjectId);
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(originalHash, (await fixture.Context.Users.SingleAsync()).PasswordHash);
    }

    [Fact]
    public async Task ResetAsync_DeniesAdministrativeApprovalWhenOrdinaryAssistanceIsDisabledWithoutConsumingState()
    {
        await using var fixture = await Fixture.CreateAsync();
        var originalHash = fixture.User.PasswordHash;
        var requestId = await fixture.CreateUnverifiedChallengeAsync();
        await fixture.AddNativeApprovalAsync(requestId);

        var result = await fixture.Service.ResetAsync(new NativeRecoveryResetRequest(
            requestId,
            string.Empty,
            "NewValid!Password2",
            BrowserContext,
            UseAdministrativeApproval: true));

        Assert.Equal(NativeRecoveryResetOutcome.Denied, result.Outcome);
        Assert.Equal(0, fixture.PasswordResetCalls);
        Assert.Equal(0, fixture.DirectoryResetCalls);
        fixture.Context.ChangeTracker.Clear();
        Assert.Null((await fixture.Context.NativeRecoveryResetApprovals.SingleAsync()).ConsumedAtUtc);
        var challenge = await fixture.Context.RecoveryProofChallenges.SingleAsync(
            candidate => candidate.Id == requestId);
        Assert.Null(challenge.VerifiedAtUtc);
        Assert.Null(challenge.ConsumedAtUtc);
        Assert.Equal(originalHash, (await fixture.Context.Users.SingleAsync()).PasswordHash);
    }

    [Fact]
    public async Task ResetAsync_DeniesChangedContextEmailOrAccountState()
    {
        await using var contextFixture = await Fixture.CreateAsync();
        var contextProof = await contextFixture.CreateProofAsync();
        var changedContext = await contextFixture.Service.ResetAsync(new NativeRecoveryResetRequest(
            contextProof.RequestId,
            contextProof.Proof!,
            "NewValid!Password2",
            BrowserContext with { CsrfHash = "changed" }));
        Assert.Equal(NativeRecoveryResetOutcome.Denied, changedContext.Outcome);

        await using var emailFixture = await Fixture.CreateAsync();
        var emailProof = await emailFixture.CreateProofAsync();
        emailFixture.RecoveryEmail.ReplaceAddress(
            "changed@example.test",
            "CHANGED@EXAMPLE.TEST",
            emailFixture.Time.GetUtcNow(),
            emailFixture.Time.GetUtcNow());
        await emailFixture.Context.SaveChangesAsync();
        var changedEmail = await emailFixture.Service.ResetAsync(new NativeRecoveryResetRequest(
            emailProof.RequestId,
            emailProof.Proof!,
            "NewValid!Password2",
            BrowserContext));
        Assert.Equal(NativeRecoveryResetOutcome.Denied, changedEmail.Outcome);

        await using var stateFixture = await Fixture.CreateAsync();
        var stateProof = await stateFixture.CreateProofAsync();
        stateFixture.User.IsActive = false;
        await stateFixture.Context.SaveChangesAsync();
        var changedState = await stateFixture.Service.ResetAsync(new NativeRecoveryResetRequest(
            stateProof.RequestId,
            stateProof.Proof!,
            "NewValid!Password2",
            BrowserContext));
        Assert.Equal(NativeRecoveryResetOutcome.Denied, changedState.Outcome);
    }

    [Fact]
    public async Task ResetAsync_DeniesWhenNativeModeIsDisabled()
    {
        await using var fixture = await Fixture.CreateAsync();
        var proof = await fixture.CreateProofAsync();
        fixture.Policy.ForgotPasswordMode = ForgotPasswordMode.Disabled;
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Service.ResetAsync(new NativeRecoveryResetRequest(
            proof.RequestId,
            proof.Proof!,
            "NewValid!Password2",
            BrowserContext));

        Assert.Equal(NativeRecoveryResetOutcome.Denied, result.Outcome);
        Assert.Equal(0, fixture.PasswordResetCalls);
    }

    [Fact]
    public async Task ResetAsync_PreservesProofAndPassword_WhenRealValidatorRejectsPassword()
    {
        await using var fixture = await Fixture.CreateAsync();
        var proof = await fixture.CreateProofAsync();

        var result = await fixture.Service.ResetAsync(new NativeRecoveryResetRequest(
            proof.RequestId,
            proof.Proof!,
            "weak",
            BrowserContext));

        Assert.Equal(NativeRecoveryResetOutcome.PasswordRejected, result.Outcome);
        Assert.Contains(nameof(IdentityErrorDescriber.PasswordTooShort), result.ErrorCodes!);
        fixture.Context.ChangeTracker.Clear();
        var user = await fixture.Context.Users.SingleAsync(candidate => candidate.Id == fixture.User.Id);
        var challenge = await fixture.Context.RecoveryProofChallenges.SingleAsync(candidate => candidate.Id == proof.RequestId);
        var recoveryEmail = await fixture.Context.RecoveryEmails.SingleAsync(
            candidate => candidate.Id == fixture.RecoveryEmail.Id);
        Assert.Equal(fixture.OriginalPasswordHash, user.PasswordHash);
        Assert.Equal(fixture.OriginalPasswordChangeDate, user.LastPasswordChangeDate);
        Assert.Null(challenge.ConsumedAtUtc);
        Assert.Equal(fixture.OriginalRecoveryVerification, recoveryEmail.VerifiedAtUtc);
    }

    [Fact]
    public async Task ResetAsync_RollsBackProofPasswordAndSession_WhenTokenRevocationFails()
    {
        await using var fixture = await Fixture.CreateAsync();
        var proof = await fixture.CreateProofAsync();
        await fixture.AddSessionAsync();
        fixture.Authorizations.Add(new object());
        fixture.Tokens.Add(new object());
        fixture.FailTokenRevocation = true;

        var result = await fixture.Service.ResetAsync(new NativeRecoveryResetRequest(
            proof.RequestId,
            proof.Proof!,
            "NewValid!Password2",
            BrowserContext));

        Assert.Equal(NativeRecoveryResetOutcome.Denied, result.Outcome);
        fixture.Context.ChangeTracker.Clear();
        var user = await fixture.Context.Users.SingleAsync(candidate => candidate.Id == fixture.User.Id);
        var challenge = await fixture.Context.RecoveryProofChallenges.SingleAsync(candidate => candidate.Id == proof.RequestId);
        var session = await fixture.Context.UserSessions.SingleAsync(candidate => candidate.UserId == fixture.User.Id);
        var recoveryEmail = await fixture.Context.RecoveryEmails.SingleAsync(
            candidate => candidate.Id == fixture.RecoveryEmail.Id);
        Assert.Equal(fixture.OriginalPasswordHash, user.PasswordHash);
        Assert.Equal("old-security-stamp", user.SecurityStamp);
        Assert.Equal(fixture.OriginalPasswordChangeDate, user.LastPasswordChangeDate);
        Assert.Null(challenge.ConsumedAtUtc);
        Assert.Null(session.RevokedUtc);
        Assert.Equal(fixture.OriginalRecoveryVerification, recoveryEmail.VerifiedAtUtc);
    }

    [Fact]
    public async Task ResetAsync_CompletedDirectoryBinding_SynchronizesAfterCommitAndPreservesPrimarySuccess()
    {
        await using var fixture = await Fixture.CreateAsync();
        var directoryObjectId = await fixture.ConfigureCompletedDirectoryAsync();
        await fixture.AddSessionAsync();
        var proof = await fixture.CreateProofAsync();
        fixture.SyncOperation = async (invocation, cancellationToken) =>
        {
            Assert.Null(fixture.Context.Database.CurrentTransaction);
            var attempt = await fixture.Context.NativeDirectoryRecoveryAttempts
                .AsNoTracking()
                .SingleAsync(cancellationToken);
            var user = await fixture.Context.Users.AsNoTracking().SingleAsync(cancellationToken);
            var session = await fixture.Context.UserSessions.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(NativeDirectoryRecoveryStatus.Succeeded, attempt.Status);
            Assert.Equal(attempt.Id, invocation.Source.AttemptId);
            Assert.Equal(attempt.Version, invocation.Source.Version);
            Assert.Equal(user.ConcurrencyStamp, invocation.ConcurrencyStamp);
            Assert.Equal(user.SecurityStamp, invocation.SecurityStamp);
            Assert.NotNull(session.RevokedUtc);
            throw new InvalidOperationException("secondary failure");
        };

        var result = await fixture.Service.ResetAsync(new NativeRecoveryResetRequest(
            proof.RequestId,
            proof.Proof,
            "NewValid!Password2",
            BrowserContext));

        Assert.Equal(NativeRecoveryResetOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, fixture.DirectoryResetCalls);
        Assert.Equal(directoryObjectId, fixture.LastDirectoryObjectId);
        fixture.Context.ChangeTracker.Clear();
        var user = await fixture.Context.Users.SingleAsync(candidate => candidate.Id == fixture.User.Id);
        var attempt = await fixture.Context.NativeDirectoryRecoveryAttempts.SingleAsync();
        var session = await fixture.Context.UserSessions.SingleAsync(candidate => candidate.UserId == fixture.User.Id);
        Assert.Equal(fixture.OriginalPasswordHash, user.PasswordHash);
        Assert.NotEqual("old-security-stamp", user.SecurityStamp);
        Assert.Equal(NativeDirectoryRecoveryStatus.Succeeded, attempt.Status);
        Assert.Equal("native-password-recovery", session.RevocationReason);
        Assert.Equal(1, fixture.SyncCalls);
        Assert.Equal(LegacyPasswordSyncSourceKind.NativeRecovery, fixture.LastSync!.Source.Kind);
        Assert.Equal(LegacyPasswordSyncCohort.CompletedDirectoryRecovery, fixture.LastSync.Cohort);
        Assert.Equal("NewValid!Password2", fixture.LastSync.Password);
    }

    [Theory]
    [InlineData(true, false, 1, 0)]
    [InlineData(false, true, 0, 1)]
    [InlineData(true, true, 1, 1)]
    [InlineData(false, false, 0, 0)]
    public async Task ResetAsync_DestinationMatrix_PreservesDirectoryAuthorityAndExactDispatchCounts(
        bool directoryEnabled,
        bool legacyEnabled,
        int expectedDirectoryWrites,
        int expectedLegacyDispatches)
    {
        await using var fixture = await Fixture.CreateAsync(
            directoryDestinationEnabled: directoryEnabled,
            legacyDestinationEnabled: legacyEnabled);
        await fixture.ConfigureCompletedDirectoryAsync();
        var proof = await fixture.CreateProofAsync();

        var result = await fixture.Service.ResetAsync(new NativeRecoveryResetRequest(
            proof.RequestId,
            proof.Proof,
            "NewValid!Password2",
            BrowserContext));

        Assert.Equal(
            directoryEnabled || legacyEnabled ? NativeRecoveryResetOutcome.Succeeded : NativeRecoveryResetOutcome.Denied,
            result.Outcome);
        Assert.Equal(expectedDirectoryWrites, fixture.DirectoryResetCalls);
        Assert.Equal(expectedLegacyDispatches, fixture.LegacyDispatchCalls);
        Assert.Equal(legacyEnabled ? 1 : 0, fixture.SyncCalls);
        Assert.Equal(
            (directoryEnabled, legacyEnabled) switch
            {
                (true, true) => ["directory", "legacy"],
                (true, false) => ["directory"],
                (false, true) => ["legacy"],
                _ => []
            },
            fixture.DestinationEvents);
    }

    [Fact]
    public async Task ResetAsync_UncertainDirectoryWrite_BlocksIssuanceAndDoesNotRetryOrFallBack()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ConfigureCompletedDirectoryAsync();
        fixture.DirectoryResetOutcome = DirectoryCredentialOperationOutcome.Timeout;
        var proof = await fixture.CreateProofAsync();
        var request = new NativeRecoveryResetRequest(
            proof.RequestId,
            proof.Proof,
            "NewValid!Password2",
            BrowserContext);

        Assert.Equal(NativeRecoveryResetOutcome.Denied, (await fixture.Service.ResetAsync(request)).Outcome);
        Assert.Equal(NativeRecoveryResetOutcome.Denied, (await fixture.Service.ResetAsync(request)).Outcome);

        Assert.Equal(1, fixture.DirectoryResetCalls);
        Assert.Equal(0, fixture.PasswordResetCalls);
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(
            NativeDirectoryRecoveryStatus.ReconciliationRequired,
            (await fixture.Context.NativeDirectoryRecoveryAttempts.SingleAsync()).Status);
        Assert.True(await new NativeDirectoryRecoveryBarrier(fixture.Context)
            .HasIssuanceBarrierAsync(fixture.User.Id));
        Assert.Equal(
            fixture.OriginalPasswordHash,
            (await fixture.Context.Users.SingleAsync(candidate => candidate.Id == fixture.User.Id)).PasswordHash);
    }

    [Fact]
    public async Task ReconcileAsync_AdministratorSuppliedSecretIsDeniedWithoutVerification()
    {
        await using var fixture = await Fixture.CreateAsync();
        var directoryObjectId = await fixture.ConfigureCompletedDirectoryAsync();
        fixture.DirectoryResetOutcome = DirectoryCredentialOperationOutcome.Unavailable;
        var proof = await fixture.CreateProofAsync();
        await fixture.Service.ResetAsync(new NativeRecoveryResetRequest(
            proof.RequestId,
            proof.Proof,
            "NewValid!Password2",
            BrowserContext));
        fixture.ReconciliationAuthorized = true;
        fixture.DirectoryVerification = new DirectoryCredentialVerificationResult(
            DirectoryCredentialOutcome.Authenticated,
            new ManagedDirectoryIdentity(directoryObjectId, "bound", true, true, false));

        var result = await fixture.Service.ReconcileAsync(
            Guid.NewGuid(),
            fixture.User.Id,
            "NewValid!Password2");

        Assert.Equal(NativeDirectoryRecoveryReconciliationOutcome.Unresolved, result);
        Assert.Equal(0, fixture.DirectoryVerificationCalls);
        Assert.Equal(1, fixture.DirectoryResetCalls);
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(
            NativeDirectoryRecoveryStatus.ReconciliationRequired,
            (await fixture.Context.NativeDirectoryRecoveryAttempts.SingleAsync()).Status);
    }

    [Fact]
    public async Task ReconcileAsync_Unauthorized_DoesNotVerifyOrFinalize()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ConfigureCompletedDirectoryAsync();
        fixture.DirectoryResetOutcome = DirectoryCredentialOperationOutcome.Timeout;
        var proof = await fixture.CreateProofAsync();
        await fixture.Service.ResetAsync(new NativeRecoveryResetRequest(
            proof.RequestId,
            proof.Proof,
            "NewValid!Password2",
            BrowserContext));

        var result = await fixture.Service.ReconcileAsync(Guid.NewGuid(), fixture.User.Id, "NewValid!Password2");

        Assert.Equal(NativeDirectoryRecoveryReconciliationOutcome.Unresolved, result);
        Assert.Equal(0, fixture.DirectoryVerificationCalls);
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(
            NativeDirectoryRecoveryStatus.ReconciliationRequired,
            (await fixture.Context.NativeDirectoryRecoveryAttempts.SingleAsync()).Status);
    }

    [Fact]
    public async Task ReconcileAsync_VerifiedWrongDirectoryObject_LeavesBarrier()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ConfigureCompletedDirectoryAsync();
        fixture.DirectoryResetOutcome = DirectoryCredentialOperationOutcome.Timeout;
        var proof = await fixture.CreateProofAsync();
        await fixture.Service.ResetAsync(new NativeRecoveryResetRequest(
            proof.RequestId,
            proof.Proof,
            "NewValid!Password2",
            BrowserContext));
        fixture.ReconciliationAuthorized = true;
        fixture.DirectoryVerification = new DirectoryCredentialVerificationResult(
            DirectoryCredentialOutcome.Authenticated,
            new ManagedDirectoryIdentity(Guid.NewGuid(), "wrong", true, true, false));

        var result = await fixture.Service.ReconcileAsync(Guid.NewGuid(), fixture.User.Id, "NewValid!Password2");

        Assert.Equal(NativeDirectoryRecoveryReconciliationOutcome.Unresolved, result);
        Assert.Equal(0, fixture.DirectoryVerificationCalls);
        Assert.Equal(1, fixture.DirectoryResetCalls);
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(
            NativeDirectoryRecoveryStatus.ReconciliationRequired,
            (await fixture.Context.NativeDirectoryRecoveryAttempts.SingleAsync()).Status);
    }

    [Fact]
    public async Task ResetAsync_DirectoryWriteSucceededButRevocationFailed_LeavesIssuanceBarrier()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ConfigureCompletedDirectoryAsync();
        fixture.Authorizations.Add(new object());
        fixture.Tokens.Add(new object());
        fixture.FailTokenRevocation = true;
        var proof = await fixture.CreateProofAsync();

        var result = await fixture.Service.ResetAsync(new NativeRecoveryResetRequest(
            proof.RequestId,
            proof.Proof,
            "NewValid!Password2",
            BrowserContext));

        Assert.Equal(NativeRecoveryResetOutcome.Denied, result.Outcome);
        Assert.Equal(1, fixture.DirectoryResetCalls);
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(
            NativeDirectoryRecoveryStatus.Reserved,
            (await fixture.Context.NativeDirectoryRecoveryAttempts.SingleAsync()).Status);
        Assert.True(await new NativeDirectoryRecoveryBarrier(fixture.Context)
            .HasIssuanceBarrierAsync(fixture.User.Id));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly Mock<UserManager<ApplicationUser>> _userManager;
        private readonly Mock<IOpenIddictAuthorizationManager> _authorizationManager;
        private readonly Mock<IOpenIddictTokenManager> _tokenManager;

        private Fixture(
            SqliteConnection connection,
            ApplicationDbContext context,
            ApplicationUser user,
            RecoveryEmailRecord recoveryEmail,
            SecurityPolicy policy,
            FixedTimeProvider time,
            PasswordHasher<ApplicationUser> hasher,
            Mock<UserManager<ApplicationUser>> userManager,
            Mock<IOpenIddictAuthorizationManager> authorizationManager,
            Mock<IOpenIddictTokenManager> tokenManager,
            NativePasswordRecoveryResetService service)
        {
            _connection = connection;
            Context = context;
            User = user;
            RecoveryEmail = recoveryEmail;
            Policy = policy;
            Time = time;
            Hasher = hasher;
            _userManager = userManager;
            _authorizationManager = authorizationManager;
            _tokenManager = tokenManager;
            Service = service;
            OriginalPasswordHash = user.PasswordHash!;
            OriginalPasswordChangeDate = user.LastPasswordChangeDate;
            OriginalRecoveryVerification = recoveryEmail.VerifiedAtUtc;
        }

        public ApplicationDbContext Context { get; }
        public ApplicationUser User { get; }
        public RecoveryEmailRecord RecoveryEmail { get; }
        public SecurityPolicy Policy { get; }
        public FixedTimeProvider Time { get; }
        public PasswordHasher<ApplicationUser> Hasher { get; }
        public NativePasswordRecoveryResetService Service { get; }
        public string OriginalPasswordHash { get; }
        public DateTime? OriginalPasswordChangeDate { get; }
        public DateTimeOffset? OriginalRecoveryVerification { get; }
        public List<object> Authorizations { get; } = [];
        public List<object> Tokens { get; } = [];
        public int PasswordResetCalls { get; private set; }
        public int AuthorizationRevocations { get; private set; }
        public int TokenRevocations { get; private set; }
        public bool FailTokenRevocation { get; set; }
        public DirectoryCredentialOperationOutcome DirectoryResetOutcome { get; set; } =
            DirectoryCredentialOperationOutcome.Succeeded;
        public DirectoryCredentialVerificationResult DirectoryVerification { get; set; } =
            new(DirectoryCredentialOutcome.InvalidCredentials);
        public bool ReconciliationAuthorized { get; set; }
        public int DirectoryResetCalls { get; private set; }
        public int DirectoryVerificationCalls { get; private set; }
        public Guid? LastDirectoryObjectId { get; private set; }
        public int SyncCalls { get; private set; }
        public int LegacyDispatchCalls { get; private set; }
        public List<string> DestinationEvents { get; } = [];
        public SyncInvocation? LastSync { get; private set; }
        public Func<SyncInvocation, CancellationToken, Task<LegacyPasswordSyncResult>>? SyncOperation { get; set; }

        public static async Task<Fixture> CreateAsync(
            bool ordinaryRecoveryAssistanceEnabled = false,
            bool directoryDestinationEnabled = true,
            bool legacyDestinationEnabled = true)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();

            var time = new FixedTimeProvider();
            var hasher = new PasswordHasher<ApplicationUser>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "native-reset-user",
                NormalizedUserName = "NATIVE-RESET-USER",
                Email = "user@example.test",
                NormalizedEmail = "USER@EXAMPLE.TEST",
                IsActive = true,
                SecurityStamp = "old-security-stamp",
                LastPasswordChangeDate = time.GetUtcNow().AddDays(-10).UtcDateTime
            };
            user.PasswordHash = hasher.HashPassword(user, "Current!Password1");
            var recoveryEmail = new RecoveryEmailRecord(
                user.Id,
                "recovery@example.test",
                "RECOVERY@EXAMPLE.TEST",
                time.GetUtcNow().AddDays(-30));
            recoveryEmail.MarkVerified(time.GetUtcNow().AddDays(-30));
            var policy = new SecurityPolicy
            {
                ForgotPasswordMode = ForgotPasswordMode.Native,
                MinPasswordLength = 12,
                PasswordExpirationDays = 0,
                MinPasswordAgeDays = 0
            };
            context.Users.Add(user);
            context.RecoveryEmails.Add(recoveryEmail);
            context.SecurityPolicies.Add(policy);
            await context.SaveChangesAsync();

            var policyService = new Mock<ISecurityPolicyService>();
            policyService.Setup(service => service.GetCurrentPolicyAsync()).ReturnsAsync(policy);
            var validator = new DynamicPasswordValidator(policyService.Object, NullLogger<DynamicPasswordValidator>.Instance);
            Fixture? fixture = null;
            var userManager = CreateUserManager();
            userManager.Setup(manager => manager.GeneratePasswordResetTokenAsync(user)).ReturnsAsync("reset-token");
            userManager.Setup(manager => manager.ResetPasswordAsync(user, "reset-token", It.IsAny<string>()))
                .Returns(async (ApplicationUser target, string _, string password) =>
                {
                    fixture!.PasswordResetCalls++;
                    var fixtureResult = await validator.ValidateAsync(userManager.Object, target, password);
                    if (!fixtureResult.Succeeded)
                    {
                        return fixtureResult;
                    }

                    target.PasswordHash = hasher.HashPassword(target, password);
                    target.SecurityStamp = Guid.NewGuid().ToString();
                    await context.SaveChangesAsync();
                    return IdentityResult.Success;
                });
            userManager.Setup(manager => manager.UpdateSecurityStampAsync(It.IsAny<ApplicationUser>()))
                .Returns(async (ApplicationUser target) =>
                {
                    target.SecurityStamp = Guid.NewGuid().ToString();
                    await context.SaveChangesAsync();
                    return IdentityResult.Success;
                });

            var authorizations = new Mock<IOpenIddictAuthorizationManager>();
            var tokens = new Mock<IOpenIddictTokenManager>();
            authorizations.Setup(manager => manager.FindBySubjectAsync(user.Id.ToString(), It.IsAny<CancellationToken>()))
                .Returns(() => AsAsyncEnumerable(fixture!.Authorizations));
            authorizations.Setup(manager => manager.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("authorization-id");
            authorizations.Setup(manager => manager.TryRevokeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    fixture!.AuthorizationRevocations++;
                    return true;
                });
            tokens.Setup(manager => manager.FindBySubjectAsync(user.Id.ToString(), It.IsAny<CancellationToken>()))
                .Returns(() => AsAsyncEnumerable(fixture!.Tokens));
            tokens.Setup(manager => manager.FindByAuthorizationIdAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() => AsAsyncEnumerable([]));
            tokens.Setup(manager => manager.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((object token, CancellationToken _) =>
                    fixture!.Tokens.IndexOf(token).ToString(System.Globalization.CultureInfo.InvariantCulture));
            tokens.Setup(manager => manager.TryRevokeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    fixture!.TokenRevocations++;
                    if (fixture.FailTokenRevocation)
                    {
                        throw new InvalidOperationException("forced token revocation failure");
                    }

                    return true;
                });

            var directoryOptions = new DirectoryIntegrationOptions
            {
                Enabled = true,
                AuthenticationEnabled = directoryDestinationEnabled
            };
            var recoveryOptions = new ForgotPasswordRecoveryOptions
            {
                DeploymentCeiling = ForgotPasswordMode.Native,
                NativeRecoveryEnabled = true,
                NativeDirectoryRecoveryEnabled = true,
                OrdinaryRecoveryAssistanceEnabled = ordinaryRecoveryAssistanceEnabled
            };
            var options = Options.Create(recoveryOptions);
            var legacyOptions = new LegacyPasswordSyncOptions
            {
                Enabled = legacyDestinationEnabled,
                CompletedDirectoryRecoveryEnabled = legacyDestinationEnabled
            };
            var policyEvaluator = new FixedPolicyEvaluator(recoveryEmail.Address);
            var routingEvaluator = new ForgotPasswordRoutingEvaluator(options);
            var directoryResetter = new Mock<IDirectoryCredentialResetter>();
            var directoryVerifier = new Mock<IDirectoryCredentialVerifier>();
            var recoveryAuthorizer = new Mock<IRecoveryProofAuthorizer>();
            var passwordSyncCoordinator = new Mock<ILegacyPasswordSyncCoordinator>();
            directoryResetter.Setup(service => service.ResetCredentialAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid objectId, string _, CancellationToken _) =>
                {
                    fixture!.DirectoryResetCalls++;
                    fixture.DestinationEvents.Add("directory");
                    fixture.LastDirectoryObjectId = objectId;
                    return new DirectoryCredentialOperationResult(fixture.DirectoryResetOutcome);
                });
            directoryVerifier.Setup(service => service.VerifyCredentialAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    fixture!.DirectoryVerificationCalls++;
                    return fixture.DirectoryVerification;
                });
            recoveryAuthorizer.Setup(service => service.IsAdministratorAuthorizedAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => fixture!.ReconciliationAuthorized);
            passwordSyncCoordinator.Setup(service => service.SynchronizeAsync(
                    It.IsAny<LegacyPasswordSyncSourceReference>(),
                    It.IsAny<LegacyPasswordSyncCohort>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns((LegacyPasswordSyncSourceReference source, LegacyPasswordSyncCohort cohort,
                    Guid accountId, Guid bindingId, string concurrencyStamp, string securityStamp,
                    string password, CancellationToken cancellationToken) =>
                {
                    fixture!.SyncCalls++;
                    fixture.LastSync = new SyncInvocation(
                        source, cohort, accountId, bindingId, concurrencyStamp, securityStamp, password);
                    if (!legacyOptions.Enabled || !legacyOptions.IsCohortEnabled(cohort))
                    {
                        return Task.FromResult(
                            new LegacyPasswordSyncResult(LegacyPasswordSyncResultOutcome.NotEligible));
                    }

                    fixture.LegacyDispatchCalls++;
                    fixture.DestinationEvents.Add("legacy");
                    return fixture.SyncOperation?.Invoke(fixture.LastSync, cancellationToken) ??
                        Task.FromResult(new LegacyPasswordSyncResult(LegacyPasswordSyncResultOutcome.Succeeded));
                });
            var service = new NativePasswordRecoveryResetService(
                context,
                userManager.Object,
                authorizations.Object,
                tokens.Object,
                policyEvaluator,
                routingEvaluator,
                directoryResetter.Object,
                directoryVerifier.Object,
                recoveryAuthorizer.Object,
                passwordSyncCoordinator.Object,
                options,
                Options.Create(directoryOptions),
                Options.Create(legacyOptions),
                time);
            fixture = new Fixture(
                connection,
                context,
                user,
                recoveryEmail,
                policy,
                time,
                hasher,
                userManager,
                authorizations,
                tokens,
                service);
            return fixture;
        }

        public Task<RecoveryVerificationPolicyDecision> EvaluatePeriodAsync(DateTimeOffset effectiveAt)
        {
            var options = Options.Create(new RecoveryVerificationPolicyOptions
            {
                Enabled = true,
                CurrentPeriodId = "period",
                EffectiveAtUtc = effectiveAt,
                GraceEndsAtUtc = effectiveAt
            });
            var evaluator = new RecoveryVerificationPolicyEvaluator(Context, options, Time);
            return evaluator.EvaluateAsync(User.Id);
        }

        public async Task<ProofFixture> CreateProofAsync()
        {
            var email = new CapturingEmailService();
            var options = Options.Create(new ForgotPasswordRecoveryOptions
            {
                DeploymentCeiling = ForgotPasswordMode.Native,
                NativeRecoveryEnabled = true,
                NativeDirectoryRecoveryEnabled = true
            });
            var proofService = new NativePasswordRecoveryProofService(
                Context,
                email,
                Hasher,
                new TestLookupNormalizer(),
                new FixedPolicyEvaluator(RecoveryEmail.Address),
                new ForgotPasswordRoutingEvaluator(options),
                options,
                Time);
            var started = await proofService.StartAsync(new NativeRecoveryStartRequest(User.UserName!, BrowserContext));
            var verified = await proofService.VerifyAsync(new NativeRecoveryVerificationRequest(
                started.RequestId,
                email.LastCode!,
                BrowserContext));
            return new ProofFixture(started.RequestId, verified.Proof!);
        }

        public async Task<Guid> CreateUnverifiedChallengeAsync()
        {
            var email = new CapturingEmailService();
            var options = Options.Create(new ForgotPasswordRecoveryOptions
            {
                DeploymentCeiling = ForgotPasswordMode.Native,
                NativeRecoveryEnabled = true,
                NativeDirectoryRecoveryEnabled = true
            });
            var proofService = new NativePasswordRecoveryProofService(
                Context,
                email,
                Hasher,
                new TestLookupNormalizer(),
                new FixedPolicyEvaluator(RecoveryEmail.Address),
                new ForgotPasswordRoutingEvaluator(options),
                options,
                Time);
            var started = await proofService.StartAsync(new NativeRecoveryStartRequest(User.UserName!, BrowserContext));
            return started.RequestId;
        }

        public async Task AddNativeApprovalAsync(Guid requestId)
        {
            var challenge = await Context.RecoveryProofChallenges.SingleAsync(candidate => candidate.Id == requestId);
            var email = await Context.RecoveryEmails.SingleAsync(candidate => candidate.Id == challenge.RecoveryEmailId);
            var approval = new NativeRecoveryResetApproval(
                challenge.Id,
                User.Id,
                email.Id,
                email.Version,
                Guid.NewGuid(),
                BrowserContext.ContextHash,
                BrowserContext.CsrfHash,
                challenge.NativeDirectoryAuthority == true,
                challenge.NativeDirectoryObjectId,
                challenge.NativeSecurityStamp!,
                "manual reset",
                "identity evidence",
                Time.GetUtcNow(),
                Time.GetUtcNow().AddMinutes(5));
            Context.NativeRecoveryResetApprovals.Add(approval);
            await Context.SaveChangesAsync();
        }

        public async Task<ProofFixture> CreateSourceBootstrapProofAsync()
        {
            Context.RecoveryEmails.Remove(RecoveryEmail);
            await Context.SaveChangesAsync();
            await ConfigureCompletedDirectoryAsync();
            var binding = await Context.ProviderSubjectDirectoryBindings.SingleAsync();
            var snapshot = new ProviderMetadataSnapshot(binding.Id, Time.GetUtcNow());
            snapshot.Refresh(
                RecoveryEmail.Address,
                ProviderEmailTrustOrigin.SourceVerified,
                Time.GetUtcNow(),
                Time.GetUtcNow());
            Context.ProviderMetadataSnapshots.Add(snapshot);
            await Context.SaveChangesAsync();

            var email = new CapturingEmailService();
            var recoveryOptions = Options.Create(new ForgotPasswordRecoveryOptions
            {
                DeploymentCeiling = ForgotPasswordMode.Native,
                NativeRecoveryEnabled = true,
                NativeDirectoryRecoveryEnabled = true
            });
            var verificationOptions = Options.Create(new RecoveryVerificationPolicyOptions
            {
                Enabled = true,
                CurrentPeriodId = "period",
                EffectiveAtUtc = Time.GetUtcNow().AddHours(-1),
                GraceEndsAtUtc = Time.GetUtcNow().AddHours(-1),
                BootstrapEnabled = true,
                BootstrapUntilUtc = Time.GetUtcNow().AddHours(1),
                AcceptSourceVerifiedEmails = true
            });
            var proofService = new NativePasswordRecoveryProofService(
                Context,
                email,
                Hasher,
                new TestLookupNormalizer(),
                new RecoveryVerificationPolicyEvaluator(Context, verificationOptions, Time),
                new ForgotPasswordRoutingEvaluator(recoveryOptions),
                recoveryOptions,
                Time);
            var started = await proofService.StartAsync(new NativeRecoveryStartRequest(User.UserName!, BrowserContext));
            var verified = await proofService.VerifyAsync(new NativeRecoveryVerificationRequest(
                started.RequestId,
                email.LastCode!,
                BrowserContext));
            return new ProofFixture(started.RequestId, verified.Proof!);
        }

        public async Task AddSessionAsync()
        {
            var role = new ApplicationRole { Id = Guid.NewGuid(), Name = $"role-{Guid.NewGuid():N}" };
            Context.Roles.Add(role);
            Context.UserSessions.Add(new UserSession
            {
                UserId = User.Id,
                AuthorizationId = Guid.NewGuid().ToString(),
                ActiveRoleId = role.Id
            });
            await Context.SaveChangesAsync();
        }

        public async Task<Guid> ConfigureCompletedDirectoryAsync()
        {
            var directoryObjectId = Guid.NewGuid();
            var binding = new ProviderSubjectDirectoryBinding(
                User.Id,
                "test",
                $"subject-{Guid.NewGuid():N}",
                directoryObjectId,
                Time.GetUtcNow().UtcDateTime,
                User.UserName);
            var state = new CredentialMigrationStateRecord(User.Id, binding.Id, Time.GetUtcNow());
            state.Advance(
                CredentialMigrationState.ProofValidated,
                EffectiveEmailOtpRequirement.NotRequired,
                Time.GetUtcNow());
            state.Advance(CredentialMigrationState.DirectoryCredentialCommitted, Time.GetUtcNow());
            state.Advance(CredentialMigrationState.LocalFinalized, Time.GetUtcNow());
            Context.ProviderSubjectDirectoryBindings.Add(binding);
            Context.CredentialMigrationStateRecords.Add(state);
            await Context.SaveChangesAsync();
            return directoryObjectId;
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
            foreach (var item in items)
            {
                yield return item;
            }

            await Task.CompletedTask;
        }
    }

    private sealed record ProofFixture(Guid RequestId, string Proof);

    private sealed record SyncInvocation(
        LegacyPasswordSyncSourceReference Source,
        LegacyPasswordSyncCohort Cohort,
        Guid AccountId,
        Guid BindingId,
        string ConcurrencyStamp,
        string SecurityStamp,
        string Password);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 9, 8, 4, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
    }

    private sealed class FixedPolicyEvaluator(string address) : IRecoveryVerificationPolicyEvaluator
    {
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
                    address,
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
        public string? LastCode { get; private set; }

        public Task SendEmailAsync(
            string to,
            string subject,
            string body,
            bool isHtml = false,
            CancellationToken ct = default)
        {
            LastCode = body.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.TrimEnd('.'))
                .Single(part => part.Length == 6 && part.All(char.IsDigit));
            return Task.CompletedTask;
        }

        public Task SendTestEmailAsync(MailSettingsDto settings, string to, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
