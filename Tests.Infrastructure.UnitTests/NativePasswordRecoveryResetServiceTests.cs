using Core.Application;
using Core.Application.DTOs;
using Core.Application.Interfaces;
using Core.Application.Options;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Core.Domain.Models;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectionOtp_ResetUsesCurrentDestinationWithoutPromotingDefault(bool useDefault)
    {
        await using var f = await Fixture.CreateAsync(selectionEnabled: true);
        if (useDefault)
        {
            f.Context.RecoveryEmails.Remove(f.RecoveryEmail);
            await f.Context.SaveChangesAsync();
            f.DefaultDestination = new("default@example.test", new string('D', 64), 1, false);
        }
        await f.AddSessionAsync();
        f.Authorizations.Add(new object()); f.Tokens.Add(new object());
        var id = await f.SendSelectionAsync();
        Assert.Equal(NativeRecoveryResetOutcome.Denied,
            (await f.Service.ResetAsync(new(id, "provider-verified", "Changed!Password123", BrowserContext))).Outcome);
        f.Time.SetUtcNow(f.Time.GetUtcNow().AddMinutes(6));
        var proof = await f.VerifySelectionAsync(id);
        Assert.Equal(NativeRecoveryVerificationOutcome.Verified, proof.Outcome);
        Assert.Equal(NativeRecoveryResetOutcome.Succeeded,
            (await f.Service.ResetAsync(new(id, proof.Proof!, "Changed!Password123", BrowserContext))).Outcome);
        Assert.Equal(1, f.PasswordResetCalls);
        Assert.Equal(1, f.AuthorizationRevocations); Assert.Equal(1, f.TokenRevocations);
        Assert.Empty(await f.Context.RecoveryEmailPreferences.ToListAsync());
        if (useDefault) Assert.Empty(await f.Context.RecoveryEmails.ToListAsync());
        else { Assert.Equal(0, f.DefaultLookups); Assert.Equal(f.OriginalRecoveryVerification, f.RecoveryEmail.VerifiedAtUtc); }
    }

    [Theory]
    [InlineData("destination", false)]
    [InlineData("destination", true)]
    [InlineData("epoch", false)]
    [InlineData("epoch", true)]
    [InlineData("stamp", false)]
    [InlineData("stamp", true)]
    [InlineData("policy", false)]
    [InlineData("policy", true)]
    [InlineData("inactive", false)]
    [InlineData("inactive", true)]
    [InlineData("pending", false)]
    [InlineData("pending", true)]
    [InlineData("grant", false)]
    [InlineData("grant", true)]
    public async Task SelectionOtp_RejectsChangedStateAtVerifyAndReset(string change, bool afterOtp)
    {
        await using var f = await Fixture.CreateAsync(selectionEnabled: true);
        f.Context.RecoveryEmails.Remove(f.RecoveryEmail);
        await f.Context.SaveChangesAsync();
        f.DefaultDestination = new("default@example.test", new string('D', 64), 1, false);
        var id = await f.SendSelectionAsync();
        var proof = afterOtp ? (await f.VerifySelectionAsync(id)).Proof : null;
        if (afterOtp) Assert.NotNull(proof);
        switch (change)
        {
            case "destination": f.DefaultDestination = f.DefaultDestination with { Address = "changed@example.test", Version = 2 }; break;
            case "epoch":
                var preference = new RecoveryEmailPreference(f.User.Id, f.Time.GetUtcNow());
                preference.TrySelect(RecoveryEmailSelectionMode.UseDefault, 1, f.Time.GetUtcNow());
                f.Context.RecoveryEmailPreferences.Add(preference); break;
            case "stamp": f.User.SecurityStamp = "changed"; break;
            case "policy": f.VerificationOptions.CurrentPeriodId = "changed"; break;
            case "inactive": f.User.IsActive = false; break;
            case "pending": f.Context.NativeDirectoryRecoveryAttempts.Add(new(id, f.User.Id, Guid.NewGuid(), f.Time.GetUtcNow())); break;
            case "grant":
                var grant = await f.Context.RecoveryPrecheckGrants.SingleAsync();
                f.Context.Entry(grant).Property(g => g.RevokedAtUtc).CurrentValue = f.Time.GetUtcNow(); break;
        }
        await f.Context.SaveChangesAsync();
        if (afterOtp) Assert.Equal(NativeRecoveryResetOutcome.Denied,
            (await f.Service.ResetAsync(new(id, proof!, "Changed!Password123", BrowserContext))).Outcome);
        else Assert.Equal(NativeRecoveryVerificationOutcome.Denied, (await f.VerifySelectionAsync(id)).Outcome);
        Assert.Equal(0, f.PasswordResetCalls); Assert.Equal(0, f.DirectoryResetCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectionApproval_UsesOwnAdminAuthorizationAndRejectsChangedEpoch(bool changed)
    {
        await using var f = await Fixture.CreateAsync(ordinaryRecoveryAssistanceEnabled: true, selectionEnabled: true);
        f.Context.RecoveryEmails.Remove(f.RecoveryEmail);
        await f.Context.SaveChangesAsync();
        f.DefaultDestination = new("default@example.test", new string('D', 64), 1, false);
        var id = await f.SendSelectionAsync();
        f.ReconciliationAuthorized = true;
        Assert.Equal(RecoveryProofOutcome.Success, (await f.Assistance.ApproveResetAsync(new(Guid.NewGuid(), f.User.Id, "checked", "support"))).Outcome);
        if (changed) f.DefaultDestination = f.DefaultDestination with { Version = 2 };
        var result = await f.Service.ResetAsync(new(id, string.Empty, "Changed!Password123", BrowserContext, true));
        Assert.Equal(changed ? NativeRecoveryResetOutcome.Denied : NativeRecoveryResetOutcome.Succeeded, result.Outcome);
    }

    [Fact]
    public async Task SelectionRollback_CustomStillWorksAndOldNullBindingCannotBypassPersistedIntent()
    {
        await using var f = await Fixture.CreateAsync();
        var old = await f.CreateProofAsync();
        var preference = new RecoveryEmailPreference(f.User.Id, f.Time.GetUtcNow());
        preference.TrySelect(RecoveryEmailSelectionMode.UseCustom, 1, f.Time.GetUtcNow());
        f.Context.RecoveryEmailPreferences.Add(preference);
        await f.Context.SaveChangesAsync();
        Assert.Equal(NativeRecoveryResetOutcome.Denied,
            (await f.Service.ResetAsync(new(old.RequestId, old.Proof, "Changed!Password123", BrowserContext))).Outcome);
        f.Time.SetUtcNow(f.Time.GetUtcNow().AddSeconds(61));
        var started = await f.SelectionProof.StartAsync(new(f.User.UserName!, BrowserContext));
        var challenge = await f.Context.RecoveryProofChallenges.AsNoTracking().SingleAsync(c => c.Id == started.RequestId);
        Assert.Equal(preference.SelectionEpoch, challenge.SelectionEpoch);
        Assert.Equal(f.RecoveryEmail.Id, challenge.RecoveryEmailId);
        Assert.Equal(0, f.DefaultLookups);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectionDirectory_RetainsWriterAndUnknownBarrier(bool timeout)
    {
        await using var f = await Fixture.CreateAsync(selectionEnabled: true);
        await f.ConfigureCompletedDirectoryAsync();
        if (timeout) f.DirectoryResetOutcome = DirectoryCredentialOperationOutcome.Timeout;
        var id = await f.SendSelectionAsync();
        var proof = await f.VerifySelectionAsync(id);
        Assert.NotNull(proof.Proof);
        var request = new NativeRecoveryResetRequest(id, proof.Proof!, "Changed!Password123", BrowserContext);
        Assert.Equal(timeout ? NativeRecoveryResetOutcome.Denied : NativeRecoveryResetOutcome.Succeeded,
            (await f.Service.ResetAsync(request)).Outcome);
        Assert.Equal(1, f.DirectoryResetCalls); Assert.Equal(0, f.PasswordResetCalls);
        Assert.Equal(f.OriginalPasswordHash, (await f.Context.Users.AsNoTracking().SingleAsync()).PasswordHash);
        if (timeout)
        {
            Assert.Equal(NativeRecoveryResetOutcome.Denied, (await f.Service.ResetAsync(request)).Outcome);
            Assert.Equal(1, f.DirectoryResetCalls);
            Assert.True(await new NativeDirectoryRecoveryBarrier(f.Context).HasIssuanceBarrierAsync(f.User.Id));
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task SelectionReplacement_KeepsAddressProofSeparateFromReset(
        bool useDefault, bool failDelivery, bool retainCustomRecord)
    {
        await using var f = await Fixture.CreateAsync(ordinaryRecoveryAssistanceEnabled: true, selectionEnabled: true);
        if (useDefault)
        {
            if (retainCustomRecord)
            {
                var preference = new RecoveryEmailPreference(f.User.Id, f.Time.GetUtcNow());
                Assert.True(preference.TrySelect(RecoveryEmailSelectionMode.UseDefault,
                    preference.SelectionEpoch, f.Time.GetUtcNow()));
                f.Context.RecoveryEmailPreferences.Add(preference);
            }
            else
            {
                f.Context.RecoveryEmails.Remove(f.RecoveryEmail);
            }
            await f.Context.SaveChangesAsync();
            f.DefaultDestination = new("default@example.test", new string('D', 64), 1, false);
        }
        var id = await f.SendSelectionAsync();
        f.ReconciliationAuthorized = true;
        f.FailSelectedDelivery = failDelivery;
        if (retainCustomRecord) f.Context.ChangeTracker.Clear();
        Assert.Equal(failDelivery ? RecoveryProofOutcome.Unavailable : RecoveryProofOutcome.Success, (await f.Assistance.ReplaceEmailAsync(
            new(Guid.NewGuid(), f.User.Id, "replacement@example.test", "checked", "support"))).Outcome);
        if (retainCustomRecord)
        {
            var reusedEmail = await f.Context.RecoveryEmails.AsNoTracking().SingleAsync();
            Assert.Equal(f.RecoveryEmail.Id, reusedEmail.Id);
            Assert.Equal("replacement@example.test", reusedEmail.Address);
            Assert.Null(reusedEmail.VerifiedAtUtc);
        }
        Assert.Equal("Verify recovery email", f.LastDeliveryMessage!.Subject);
        Assert.Equal("replacement@example.test", f.LastDeliveryMessage.To);
        Assert.False(f.LastDeliveryMessage.IsHtml);
        f.GeneralQueue.Verify(q => q.QueueEmailAsync(It.IsAny<EmailMessage>()), Times.Never());
        var auditText = System.Text.Json.JsonSerializer.Serialize(f.AuditEvents);
        Assert.DoesNotContain("replacement@example.test", auditText);
        Assert.DoesNotContain(f.AssistanceCode!, auditText);
        Assert.Equal(NativeRecoveryVerificationOutcome.Denied, (await f.VerifySelectionAsync(id)).Outcome);
        if (failDelivery)
        {
            Assert.NotEqual(RecoveryProofOutcome.Success,
                await f.Assistance.VerifyReplacementAsync(new(id, f.AssistanceCode!, BrowserContext)));
            Assert.Null((await f.Context.RecoveryEmails.AsNoTracking().SingleAsync()).VerifiedAtUtc);
            Assert.NotNull((await f.Context.RecoveryProofChallenges.AsNoTracking().SingleAsync(c =>
                c.Purpose == RecoveryProofPurpose.RecoveryAddressVerification)).RevokedAtUtc);
            Assert.Equal(0, f.PasswordResetCalls);
            return;
        }
        Assert.Equal(RecoveryProofOutcome.Success,
            await f.Assistance.VerifyReplacementAsync(new(id, f.AssistanceCode!, BrowserContext)));
        var change = await f.Context.RecoveryProofChallenges.SingleAsync(c => c.Purpose == RecoveryProofPurpose.RecoveryAddressVerification);
        Assert.Equal(NativeRecoveryResetOutcome.Denied,
            (await f.Service.ResetAsync(new(change.Id, f.AssistanceCode!, "Changed!Password123", BrowserContext))).Outcome);
        var active = await f.Context.RecoveryEmails.AsNoTracking().SingleAsync();
        Assert.Equal(RecoveryEmailProvenance.AdminAssistedVerified, active.Provenance);
        Assert.Equal(RecoveryEmailSelectionMode.UseCustom, (await f.Context.RecoveryEmailPreferences.SingleAsync()).Mode);
        Assert.Equal(0, f.PasswordResetCalls);
    }

    [Fact]
    public async Task SelectionOtp_RejectsPersonSuspensionBeforeVerify()
    {
        await using var f = await Fixture.CreateAsync(selectionEnabled: true);
        var person = new Person { Id = Guid.NewGuid(), Status = PersonStatus.Active };
        f.Context.Persons.Add(person); f.User.PersonId = person.Id; await f.Context.SaveChangesAsync();
        var id = await f.SendSelectionAsync();
        person.Status = PersonStatus.Suspended; await f.Context.SaveChangesAsync();
        Assert.Equal(NativeRecoveryVerificationOutcome.Denied, (await f.VerifySelectionAsync(id)).Outcome);
    }

    [Fact]
    public async Task SelectionOtp_DoesNotShareProofsOrSelectionBetweenSamePersonAccounts()
    {
        await using var f = await Fixture.CreateAsync(selectionEnabled: true);
        var person = new Person { Id = Guid.NewGuid(), Status = PersonStatus.Active };
        var other = new ApplicationUser { Id = Guid.NewGuid(), UserName = "other", NormalizedUserName = "OTHER",
            IsActive = true, SecurityStamp = "other-stamp", PasswordHash = "other-hash", PersonId = person.Id };
        var otherEmail = new RecoveryEmailRecord(other.Id, "other@example.test", "OTHER@EXAMPLE.TEST", f.Time.GetUtcNow());
        otherEmail.MarkVerified(f.Time.GetUtcNow());
        f.Context.Persons.Add(person); f.Context.Users.Add(other); f.Context.RecoveryEmails.Add(otherEmail);
        f.User.PersonId = person.Id; await f.Context.SaveChangesAsync();
        var first = await f.SendSelectionAsync(); var proof = await f.VerifySelectionAsync(first);
        var prepared = await f.Precheck.PrepareAsync(new(other.UserName, string.Empty, BrowserContext, "127.0.0.1"));
        Assert.NotNull(prepared.GrantId);
        var second = await f.Precheck.SendOtpAsync(new(prepared.GrantId!.Value, BrowserContext, "127.0.0.1"));
        var preference = new RecoveryEmailPreference(f.User.Id, f.Time.GetUtcNow());
        preference.TrySelect(RecoveryEmailSelectionMode.Disabled, 1, f.Time.GetUtcNow());
        f.Context.RecoveryEmailPreferences.Add(preference); await f.Context.SaveChangesAsync();
        Assert.Equal(NativeRecoveryVerificationOutcome.Verified, (await f.VerifySelectionAsync(second.RequestId)).Outcome);
        Assert.Equal(NativeRecoveryResetOutcome.Denied,
            (await f.Service.ResetAsync(new(second.RequestId, proof.Proof!, "Changed!Password123", BrowserContext))).Outcome);
        Assert.Equal("other@example.test", (await f.Context.RecoveryEmails.AsNoTracking().SingleAsync(e => e.LocalAccountId == other.Id)).Address);
        Assert.Equal(0, f.PasswordResetCalls);
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
        public RecoveryPrecheckService Precheck { get; private set; } = null!;
        public NativePasswordRecoveryProofService SelectionProof { get; private set; } = null!;
        public NativeRecoveryAssistanceService Assistance { get; private set; } = null!;
        public RecoveryEmailSelectionOptions SelectionOptions { get; private set; } = null!;
        public RecoveryVerificationPolicyOptions VerificationOptions { get; private set; } = null!;
        public RecoveryDefaultDestination? DefaultDestination { get; set; }
        public string? SelectionCode { get; set; }
        public int DefaultLookups { get; set; }
        public string? AssistanceCode => SelectionCode;
        public bool FailSelectedDelivery { get; set; }
        public EmailMessage? LastDeliveryMessage { get; set; }
        public Mock<IEmailQueue> GeneralQueue { get; } = new(MockBehavior.Strict);
        public List<RecoveryProofAuditEvent> AuditEvents { get; } = [];
        public async Task<Guid> SendSelectionAsync()
        {
            var prepared = await Precheck.PrepareAsync(new(User.UserName!, string.Empty, BrowserContext, "127.0.0.1"));
            Assert.NotNull(prepared.GrantId);
            var result = await Precheck.SendOtpAsync(new(prepared.GrantId!.Value, BrowserContext, "127.0.0.1"));
            Assert.NotNull(SelectionCode);
            return result.RequestId;
        }
        public Task<NativeRecoveryVerificationResult> VerifySelectionAsync(Guid requestId) =>
            SelectionProof.VerifyAsync(new(requestId, SelectionCode!, BrowserContext));
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
            bool legacyDestinationEnabled = true, bool selectionEnabled = false)
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
            userManager.Setup(manager => manager.GeneratePasswordResetTokenAsync(It.Is<ApplicationUser>(u => u.Id == user.Id))).ReturnsAsync("reset-token");
            userManager.Setup(manager => manager.ResetPasswordAsync(It.Is<ApplicationUser>(u => u.Id == user.Id), "reset-token", It.IsAny<string>()))
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
            var selection = Options.Create(new RecoveryEmailSelectionOptions { Enabled = selectionEnabled, TrustedDefaultFallbackEnabled = true });
            var verification = Options.Create(new RecoveryVerificationPolicyOptions { Enabled = true, CurrentPeriodId = "period",
                EffectiveAtUtc = time.GetUtcNow().AddHours(-1), GraceEndsAtUtc = time.GetUtcNow().AddHours(-1), AcceptSourceVerifiedEmails = true });
            var defaults = new Mock<IRecoveryDefaultDestinationEvaluator>();
            defaults.Setup(d => d.EvaluateDefaultAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => { fixture!.DefaultLookups++; return fixture.DefaultDestination; });
            var resolver = new RecoveryDestinationResolver(context, defaults.Object, selection, verification, time);
            var dispatcher = new Mock<IEmailDispatcher>();
            dispatcher.Setup(d => d.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
                .Returns<EmailMessage, CancellationToken>((message, _) =>
                {
                    fixture!.SelectionCode = System.Text.RegularExpressions.Regex.Match(message.Body, @"\b\d{6}\b").Value;
                    fixture.LastDeliveryMessage = message;
                    return fixture.FailSelectedDelivery ?
                        Task.FromException(new InvalidOperationException(message.To + message.Body)) : Task.CompletedTask;
                });
            var mailSettings = new Mock<IOptionsSnapshot<EmailOptions>>();
            mailSettings.SetupGet(m => m.Value).Returns(new EmailOptions { SmtpHost = "synthetic.test" });
            var delivery = new RecoveryOtpDeliveryService(dispatcher.Object, mailSettings.Object);
            var precheck = new RecoveryPrecheckService(context, resolver, new Mock<IRecoveryIdentityVerificationClient>().Object,
                new RecoveryThrottleService(context, Options.Create(new RecoveryThrottleOptions { HashKey = Guid.NewGuid().ToString("N") }), time),
                delivery, hasher, new TestLookupNormalizer(), routingEvaluator,
                Options.Create(new RecoveryIdentityVerificationOptions()), selection, options, time);
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
                time, precheck, selection);
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
            fixture.Precheck = precheck;
            fixture.SelectionOptions = selection.Value;
            fixture.VerificationOptions = verification.Value;
            fixture.SelectionProof = new NativePasswordRecoveryProofService(context, new CapturingEmailService(), hasher,
                new TestLookupNormalizer(), policyEvaluator, routingEvaluator, options, time, selectionOptions: selection, precheck: precheck, delivery: delivery);
            var audit = new Mock<IRecoveryProofAudit>();
            audit.Setup(a => a.RecordAsync(It.IsAny<RecoveryProofAuditEvent>(), It.IsAny<CancellationToken>()))
                .Callback<RecoveryProofAuditEvent, CancellationToken>((item, _) => fixture.AuditEvents.Add(item))
                .Returns(Task.CompletedTask);
            fixture.Assistance = new NativeRecoveryAssistanceService(context, recoveryAuthorizer.Object, policyEvaluator,
                routingEvaluator, new EmailService(fixture.GeneralQueue.Object, dispatcher.Object), hasher, audit.Object,
                options, time, precheck, selection, delivery: delivery);
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
