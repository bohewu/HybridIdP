using Core.Application;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Tests.Application.UnitTests;

public sealed class Stage2CredentialMigrationTests
{
    [Fact]
    public async Task BeginAsync_EligibleUnmigratedAccount_ProofsOnceAndPersistsBeforeContinuation()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "account" };
        var binding = new DirectoryObjectBinding("provider", "subject", Guid.NewGuid(), "account");
        var required = new CredentialMigrationRecord(user.Id, binding, CredentialMigrationState.Required);
        var validated = required with
        {
            State = CredentialMigrationState.ProofValidated,
            EffectiveEmailOtpRequirement = EffectiveEmailOtpRequirement.NotRequired
        };
        var proof = new Mock<IProofProvider>(MockBehavior.Strict);
        proof.Setup(service => service.ProveAsync(It.IsAny<ProofRequest>(), "current-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessfulProof("account"));
        var lookup = new Mock<IDirectoryIdentityLookup>(MockBehavior.Strict);
        lookup.Setup(service => service.FindManagedIdentityAsync("account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryLookupResult(DirectoryLookupOutcome.Found, Identity(binding.DirectoryObjectId)));
        var state = new Mock<ICredentialMigrationStateStore>(MockBehavior.Strict);
        state.Setup(service => service.FindAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CredentialMigrationRecord?)null);
        state.Setup(service => service.EnsureRequiredAsync(user.Id, It.Is<DirectoryObjectBinding>(candidate => candidate == binding), It.IsAny<CancellationToken>()))
            .ReturnsAsync(required);
        state.Setup(service => service.AdvanceToProofValidatedAsync(
                user.Id,
                EffectiveEmailOtpRequirement.NotRequired,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(validated);
        var continuations = new Mock<IMigrationContinuationStore>(MockBehavior.Strict);
        continuations.Setup(service => service.CreateAsync(
                It.Is<MigrationContinuationRequest>(candidate => candidate.LocalAccountId == user.Id && candidate.Binding == binding),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationContinuation("opaque", DateTimeOffset.UtcNow.AddMinutes(1)));
        var sut = CreateService(user, proof.Object, lookup.Object, state.Object, continuations.Object);

        var result = await sut.BeginAsync(new MigrationProofCeremonyRequest(
            "account",
            "current-password",
            Context()));

        Assert.Equal(MigrationCeremonyOutcome.ContinuationIssued, result.Outcome);
        Assert.Equal("opaque", result.Continuation);
        proof.Verify(service => service.ProveAsync(It.IsAny<ProofRequest>(), "current-password", It.IsAny<CancellationToken>()), Times.Once);
        state.Verify(service => service.AdvanceToProofValidatedAsync(
            user.Id,
            EffectiveEmailOtpRequirement.NotRequired,
            It.IsAny<CancellationToken>()), Times.Once);
        continuations.VerifyAll();
    }

    [Fact]
    public async Task BeginAsync_ProviderRequestedEmailOtp_PersistsRequiredBeforeContinuation()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "account" };
        var binding = new DirectoryObjectBinding("provider", "subject", Guid.NewGuid(), "account");
        var required = new CredentialMigrationRecord(user.Id, binding, CredentialMigrationState.Required);
        var validated = required with
        {
            State = CredentialMigrationState.ProofValidated,
            EffectiveEmailOtpRequirement = EffectiveEmailOtpRequirement.Required
        };
        var proof = new Mock<IProofProvider>(MockBehavior.Strict);
        proof.Setup(service => service.ProveAsync(It.IsAny<ProofRequest>(), "current-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessfulProof("account") with
            {
                RequiredActions = [ProofRequiredAction.EmailOtp],
                Profile = new AssuredProfile
                {
                    Email = "member@example.test",
                    AssuredFields = [AssuredProfileField.Email]
                }
            });
        var lookup = new Mock<IDirectoryIdentityLookup>(MockBehavior.Strict);
        lookup.Setup(service => service.FindManagedIdentityAsync("account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryLookupResult(DirectoryLookupOutcome.Found, Identity(binding.DirectoryObjectId)));
        var state = new Mock<ICredentialMigrationStateStore>(MockBehavior.Strict);
        state.Setup(service => service.FindAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CredentialMigrationRecord?)null);
        state.Setup(service => service.EnsureRequiredAsync(user.Id, It.IsAny<DirectoryObjectBinding>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(required);
        state.Setup(service => service.AdvanceToProofValidatedAsync(
                user.Id,
                EffectiveEmailOtpRequirement.Required,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(validated);
        var continuations = new Mock<IMigrationContinuationStore>(MockBehavior.Strict);
        continuations.Setup(service => service.CreateAsync(It.IsAny<MigrationContinuationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationContinuation("opaque", DateTimeOffset.UtcNow.AddMinutes(10)));
        var sut = CreateService(
            user,
            proof.Object,
            lookup.Object,
            state.Object,
            continuations.Object,
            policyDecision: new MigrationPolicyDecision(true, EmailOtpPolicy.ProviderRequested));

        var result = await sut.BeginAsync(new MigrationProofCeremonyRequest("account", "current-password", Context()));

        Assert.Equal(MigrationCeremonyOutcome.ContinuationIssued, result.Outcome);
        Assert.True(result.RequiresEmailOtp);
        state.Verify(service => service.AdvanceToProofValidatedAsync(
            user.Id,
            EffectiveEmailOtpRequirement.Required,
            It.IsAny<CancellationToken>()), Times.Once);
        continuations.VerifyAll();
    }

    [Fact]
    public async Task BeginAsync_CompletedCandidate_DeniesBeforeCallingProofProvider()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "completed" };
        var binding = new DirectoryObjectBinding("provider", "subject", Guid.NewGuid());
        var state = new Mock<ICredentialMigrationStateStore>(MockBehavior.Strict);
        state.Setup(service => service.FindAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialMigrationRecord(user.Id, binding, CredentialMigrationState.LocalFinalized));
        var proof = new Mock<IProofProvider>(MockBehavior.Strict);
        var sut = CreateService(
            user,
            proof.Object,
            Mock.Of<IDirectoryIdentityLookup>(),
            state.Object,
            Mock.Of<IMigrationContinuationStore>(),
            preProofCandidateAccount: "completed");

        var result = await sut.BeginAsync(new MigrationProofCeremonyRequest("completed", "current-password", Context()));

        Assert.Equal(MigrationCeremonyOutcome.Denied, result.Outcome);
        proof.Verify(service => service.ProveAsync(It.IsAny<ProofRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BeginAsync_CompletedCanonicalAlias_DeniesBeforeCallingProofProvider()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "provider-subject" };
        var directoryObjectId = Guid.NewGuid();
        var binding = new DirectoryObjectBinding("provider", "subject", directoryObjectId, "canonical-account");
        var state = new Mock<ICredentialMigrationStateStore>(MockBehavior.Strict);
        state.Setup(service => service.FindAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialMigrationRecord(user.Id, binding, CredentialMigrationState.LocalFinalized));
        var proof = new Mock<IProofProvider>(MockBehavior.Strict);
        var aliasBinding = new ProviderSubjectDirectoryBinding(
            user.Id,
            binding.ProviderNamespace,
            binding.StableSubject,
            directoryObjectId,
            DateTime.UtcNow,
            binding.CanonicalAccountAlias);
        var sut = CreateService(
            user,
            proof.Object,
            Mock.Of<IDirectoryIdentityLookup>(),
            state.Object,
            Mock.Of<IMigrationContinuationStore>(),
            aliasBindings: [aliasBinding]);

        var result = await sut.BeginAsync(new MigrationProofCeremonyRequest("canonical-account", "current-password", Context()));

        Assert.Equal(MigrationCeremonyOutcome.Denied, result.Outcome);
        proof.Verify(service => service.ProveAsync(It.IsAny<ProofRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CommitAsync_ResetSuccessButIndependentBindFails_DoesNotCommitOrFinalize()
    {
        var accountId = Guid.NewGuid();
        var binding = new DirectoryObjectBinding("provider", "subject", Guid.NewGuid());
        var continuationRecord = new CredentialMigrationRecord(
            accountId,
            binding,
            CredentialMigrationState.ProofValidated,
            EffectiveEmailOtpRequirement.NotRequired);
        var continuations = new Mock<IMigrationContinuationStore>(MockBehavior.Strict);
        continuations.Setup(service => service.InspectAsync("opaque", It.IsAny<MigrationContinuationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.Consumed, continuationRecord));
        continuations.Setup(service => service.ConsumeAsync("opaque", It.IsAny<MigrationContinuationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.Consumed, continuationRecord));
        var state = new Mock<ICredentialMigrationStateStore>(MockBehavior.Strict);
        var resetter = new Mock<IDirectoryCredentialResetter>(MockBehavior.Strict);
        resetter.Setup(service => service.ResetCredentialAsync(binding.DirectoryObjectId, "new-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryCredentialOperationResult(DirectoryCredentialOperationOutcome.Succeeded));
        var verifier = new Mock<IDirectoryCredentialVerifier>(MockBehavior.Strict);
        verifier.Setup(service => service.VerifyCredentialAsync(binding.DirectoryObjectId, "new-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryCredentialVerificationResult(DirectoryCredentialOutcome.InvalidCredentials));
        var sut = CreateService(
            new ApplicationUser { Id = accountId, UserName = "account" },
            Mock.Of<IProofProvider>(),
            Mock.Of<IDirectoryIdentityLookup>(),
            state.Object,
            continuations.Object,
            resetter.Object,
            verifier.Object);

        var result = await sut.CommitAsync(new MigrationCommitCeremonyRequest(
            "opaque",
            "new-password",
            Context(),
            null));

        Assert.Equal(MigrationCeremonyOutcome.Denied, result.Outcome);
        resetter.Verify(service => service.ResetCredentialAsync(binding.DirectoryObjectId, "new-password", It.IsAny<CancellationToken>()), Times.Once);
        verifier.Verify(service => service.VerifyCredentialAsync(binding.DirectoryObjectId, "new-password", It.IsAny<CancellationToken>()), Times.Once);
        state.Verify(service => service.AdvanceAsync(
            accountId,
            CredentialMigrationState.ProofValidated,
            CredentialMigrationState.DirectoryCredentialCommitted,
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AuthenticateCompletedAsync_FinalizedAccount_UsesDirectoryWithoutProof()
    {
        var accountId = Guid.NewGuid();
        var binding = new DirectoryObjectBinding("provider", "subject", Guid.NewGuid());
        var state = new Mock<ICredentialMigrationStateStore>(MockBehavior.Strict);
        state.Setup(service => service.FindAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialMigrationRecord(accountId, binding, CredentialMigrationState.LocalFinalized));
        var authenticator = new Mock<IDirectoryCredentialAuthenticator>(MockBehavior.Strict);
        authenticator.Setup(service => service.AuthenticateAsync(binding.DirectoryObjectId, "directory-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryCredentialResult(DirectoryCredentialOutcome.Authenticated, Identity(binding.DirectoryObjectId)));
        var proof = new Mock<IProofProvider>(MockBehavior.Strict);
        var sut = CreateService(
            new ApplicationUser { Id = accountId, UserName = "account" },
            proof.Object,
            Mock.Of<IDirectoryIdentityLookup>(),
            state.Object,
            Mock.Of<IMigrationContinuationStore>(),
            authenticator: authenticator.Object);

        var result = await sut.AuthenticateCompletedAsync(accountId, "directory-password");

        Assert.Equal(DirectoryCredentialOutcome.Authenticated, result.Outcome);
        proof.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BeginAsync_ProviderKeyWithCanonicalCollision_DeniesWithoutEstablishingBinding()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "account", NormalizedUserName = "account" };
        var conflicting = new ApplicationUser { Id = Guid.NewGuid(), UserName = "other", NormalizedUserName = "account" };
        var proof = new Mock<IProofProvider>(MockBehavior.Strict);
        proof.Setup(service => service.ProveAsync(It.IsAny<ProofRequest>(), "current-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessfulProof("account"));
        var lookup = new Mock<IDirectoryIdentityLookup>(MockBehavior.Strict);
        lookup.Setup(service => service.FindManagedIdentityAsync("account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryLookupResult(DirectoryLookupOutcome.Found, Identity(Guid.NewGuid())));
        var state = new Mock<ICredentialMigrationStateStore>(MockBehavior.Strict);
        var sut = CreateService(user, proof.Object, lookup.Object, state.Object, Mock.Of<IMigrationContinuationStore>(), extraUsers: [conflicting]);

        var result = await sut.BeginAsync(new MigrationProofCeremonyRequest("browser-input", "current-password", Context()));

        Assert.Equal(MigrationCeremonyOutcome.Denied, result.Outcome);
        state.Verify(service => service.EnsureRequiredAsync(It.IsAny<Guid>(), It.IsAny<DirectoryObjectBinding>(), It.IsAny<CancellationToken>()), Times.Never);
        proof.Verify(service => service.ProveAsync(It.Is<ProofRequest>(request => request.AccountName == "browser-input"), "current-password", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CommitAsync_LifecycleChangedBeforeConsume_DeniesWithoutDirectoryReset()
    {
        var accountId = Guid.NewGuid();
        var binding = new DirectoryObjectBinding("provider", "subject", Guid.NewGuid());
        var record = new CredentialMigrationRecord(
            accountId,
            binding,
            CredentialMigrationState.ProofValidated,
            EffectiveEmailOtpRequirement.NotRequired);
        var continuations = new Mock<IMigrationContinuationStore>(MockBehavior.Strict);
        continuations.Setup(service => service.InspectAsync("opaque", It.IsAny<MigrationContinuationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.Consumed, record));
        var resetter = new Mock<IDirectoryCredentialResetter>(MockBehavior.Strict);
        var sut = CreateService(
            new ApplicationUser { Id = accountId, UserName = "account", IsActive = false },
            Mock.Of<IProofProvider>(),
            Mock.Of<IDirectoryIdentityLookup>(),
            Mock.Of<ICredentialMigrationStateStore>(),
            continuations.Object,
            resetter.Object);

        var result = await sut.CommitAsync(new MigrationCommitCeremonyRequest("opaque", "new-password", Context()));

        Assert.Equal(MigrationCeremonyOutcome.Denied, result.Outcome);
        continuations.Verify(service => service.ConsumeAsync(It.IsAny<string>(), It.IsAny<MigrationContinuationContext>(), It.IsAny<CancellationToken>()), Times.Never);
        resetter.Verify(service => service.ResetCredentialAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CommitAsync_StoredRequiredEmailOtpWithoutProof_DeniesBeforeConsumeOrReset()
    {
        var accountId = Guid.NewGuid();
        var binding = new DirectoryObjectBinding("provider", "subject", Guid.NewGuid());
        var record = new CredentialMigrationRecord(
            accountId,
            binding,
            CredentialMigrationState.ProofValidated,
            EffectiveEmailOtpRequirement.Required);
        var continuations = new Mock<IMigrationContinuationStore>(MockBehavior.Strict);
        continuations.Setup(service => service.InspectAsync("opaque", It.IsAny<MigrationContinuationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.Consumed, record));
        var resetter = new Mock<IDirectoryCredentialResetter>(MockBehavior.Strict);
        var assistance = new Mock<IRecoveryAssistanceService>(MockBehavior.Strict);
        assistance.Setup(service => service.ConsumeResetApprovalAsync(
                It.IsAny<ResetApprovalConsumptionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecoveryProofOutcome.Missing);
        var sut = CreateService(
            new ApplicationUser { Id = accountId, UserName = "account" },
            Mock.Of<IProofProvider>(),
            Mock.Of<IDirectoryIdentityLookup>(),
            Mock.Of<ICredentialMigrationStateStore>(),
            continuations.Object,
            resetter.Object,
            recoveryAssistance: assistance.Object);

        var result = await sut.CommitAsync(new MigrationCommitCeremonyRequest("opaque", "new-password", Context()));

        Assert.Equal(MigrationCeremonyOutcome.Denied, result.Outcome);
        continuations.Verify(service => service.ConsumeAsync(It.IsAny<string>(), It.IsAny<MigrationContinuationContext>(), It.IsAny<CancellationToken>()), Times.Never);
        resetter.Verify(service => service.ResetCredentialAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(RecoveryProofOutcome.Missing)]
    [InlineData(RecoveryProofOutcome.Invalid)]
    [InlineData(RecoveryProofOutcome.Expired)]
    [InlineData(RecoveryProofOutcome.Exhausted)]
    [InlineData(RecoveryProofOutcome.Replayed)]
    [InlineData(RecoveryProofOutcome.Unauthorized)]
    [InlineData(RecoveryProofOutcome.Unavailable)]
    public async Task CommitAsync_RequiredOtpProofFailure_DoesNotConsumeContinuationOrReset(
        RecoveryProofOutcome proofOutcome)
    {
        var accountId = Guid.NewGuid();
        var binding = new DirectoryObjectBinding("provider", "subject", Guid.NewGuid());
        var record = new CredentialMigrationRecord(
            accountId,
            binding,
            CredentialMigrationState.ProofValidated,
            EffectiveEmailOtpRequirement.Required);
        var continuations = new Mock<IMigrationContinuationStore>(MockBehavior.Strict);
        continuations.Setup(service => service.InspectAsync(
                "opaque",
                It.IsAny<MigrationContinuationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.Consumed, record));
        var otp = new Mock<IMigrationOtpProofService>(MockBehavior.Strict);
        otp.Setup(service => service.ConsumeAsync(
                It.Is<MigrationOtpConsumptionRequest>(request =>
                    request.Continuation == "opaque" && request.Proof == "otp-proof"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(proofOutcome);
        var resetter = new Mock<IDirectoryCredentialResetter>(MockBehavior.Strict);
        var sut = CreateService(
            new ApplicationUser { Id = accountId, UserName = "account" },
            Mock.Of<IProofProvider>(),
            Mock.Of<IDirectoryIdentityLookup>(),
            Mock.Of<ICredentialMigrationStateStore>(),
            continuations.Object,
            resetter.Object,
            migrationOtp: otp.Object);

        var result = await sut.CommitAsync(new MigrationCommitCeremonyRequest(
            "opaque",
            "new-password",
            Context(),
            "otp-proof"));

        Assert.Equal(MigrationCeremonyOutcome.Denied, result.Outcome);
        continuations.Verify(service => service.ConsumeAsync(
            It.IsAny<string>(),
            It.IsAny<MigrationContinuationContext>(),
            It.IsAny<CancellationToken>()), Times.Never);
        resetter.Verify(service => service.ResetCredentialAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CommitAsync_RequiredOtpProofSuccess_ConsumesProofBeforeContinuationAndReset()
    {
        var calls = new List<string>();
        var accountId = Guid.NewGuid();
        var binding = new DirectoryObjectBinding("provider", "subject", Guid.NewGuid());
        var record = new CredentialMigrationRecord(
            accountId,
            binding,
            CredentialMigrationState.ProofValidated,
            EffectiveEmailOtpRequirement.Required);
        var continuations = new Mock<IMigrationContinuationStore>(MockBehavior.Strict);
        continuations.Setup(service => service.InspectAsync(
                "opaque",
                It.IsAny<MigrationContinuationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.Consumed, record));
        continuations.Setup(service => service.ConsumeAsync(
                "opaque",
                It.IsAny<MigrationContinuationContext>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("continuation"))
            .ReturnsAsync(new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.Consumed, record));
        var otp = new Mock<IMigrationOtpProofService>(MockBehavior.Strict);
        otp.Setup(service => service.ConsumeAsync(
                It.IsAny<MigrationOtpConsumptionRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("proof"))
            .ReturnsAsync(RecoveryProofOutcome.Success);
        var resetter = new Mock<IDirectoryCredentialResetter>(MockBehavior.Strict);
        resetter.Setup(service => service.ResetCredentialAsync(
                binding.DirectoryObjectId,
                "new-password",
                It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("reset"))
            .ReturnsAsync(new DirectoryCredentialOperationResult(DirectoryCredentialOperationOutcome.Succeeded));
        var verifier = new Mock<IDirectoryCredentialVerifier>(MockBehavior.Strict);
        verifier.Setup(service => service.VerifyCredentialAsync(
                binding.DirectoryObjectId,
                "new-password",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryCredentialVerificationResult(DirectoryCredentialOutcome.InvalidCredentials));
        var sut = CreateService(
            new ApplicationUser { Id = accountId, UserName = "account" },
            Mock.Of<IProofProvider>(),
            Mock.Of<IDirectoryIdentityLookup>(),
            Mock.Of<ICredentialMigrationStateStore>(),
            continuations.Object,
            resetter.Object,
            verifier.Object,
            migrationOtp: otp.Object);

        var result = await sut.CommitAsync(new MigrationCommitCeremonyRequest(
            "opaque",
            "new-password",
            Context(),
            "otp-proof"));

        Assert.Equal(MigrationCeremonyOutcome.Denied, result.Outcome);
        Assert.Equal(["proof", "continuation", "reset"], calls);
    }

    [Fact]
    public async Task CommitAsync_RequiredOtpSuccess_DoesNotEnablePermanentEmailMfa()
    {
        var accountId = Guid.NewGuid();
        var user = new ApplicationUser { Id = accountId, UserName = "account", EmailMfaEnabled = false };
        var binding = new DirectoryObjectBinding("provider", "subject", Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;
        var persistedBinding = new ProviderSubjectDirectoryBinding(
            accountId,
            binding.ProviderNamespace,
            binding.StableSubject,
            binding.DirectoryObjectId,
            now.UtcDateTime);
        var persistedState = new CredentialMigrationStateRecord(accountId, persistedBinding.Id, now);
        persistedState.Advance(
            CredentialMigrationState.ProofValidated,
            EffectiveEmailOtpRequirement.Required,
            now);
        persistedState.Advance(CredentialMigrationState.DirectoryCredentialCommitted, now);
        var proofValidated = new CredentialMigrationRecord(
            accountId,
            binding,
            CredentialMigrationState.ProofValidated,
            EffectiveEmailOtpRequirement.Required);
        var directoryCommitted = proofValidated with { State = CredentialMigrationState.DirectoryCredentialCommitted };
        var localFinalized = proofValidated with { State = CredentialMigrationState.LocalFinalized };
        var continuations = new Mock<IMigrationContinuationStore>(MockBehavior.Strict);
        continuations.Setup(service => service.InspectAsync(
                "opaque",
                It.IsAny<MigrationContinuationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.Consumed, proofValidated));
        continuations.Setup(service => service.ConsumeAsync(
                "opaque",
                It.IsAny<MigrationContinuationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.Consumed, proofValidated));
        var state = new Mock<ICredentialMigrationStateStore>(MockBehavior.Strict);
        state.Setup(service => service.AdvanceAsync(
                accountId,
                CredentialMigrationState.ProofValidated,
                CredentialMigrationState.DirectoryCredentialCommitted,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(directoryCommitted);
        state.Setup(service => service.AdvanceAsync(
                accountId,
                CredentialMigrationState.DirectoryCredentialCommitted,
                CredentialMigrationState.LocalFinalized,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(localFinalized);
        var resetter = new Mock<IDirectoryCredentialResetter>(MockBehavior.Strict);
        resetter.Setup(service => service.ResetCredentialAsync(
                binding.DirectoryObjectId,
                "new-password",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryCredentialOperationResult(DirectoryCredentialOperationOutcome.Succeeded));
        var verifier = new Mock<IDirectoryCredentialVerifier>(MockBehavior.Strict);
        verifier.Setup(service => service.VerifyCredentialAsync(
                binding.DirectoryObjectId,
                "new-password",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryCredentialVerificationResult(
                DirectoryCredentialOutcome.Authenticated,
                Identity(binding.DirectoryObjectId)));
        var refresh = new Mock<IStage1BindingRefreshService>(MockBehavior.Strict);
        refresh.Setup(service => service.BindAndRefreshAsync(
                It.IsAny<Stage1BindingRefreshRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stage1BindingRefreshOutcome.BoundAndRefreshed);
        var sut = CreateService(
            user,
            Mock.Of<IProofProvider>(),
            Mock.Of<IDirectoryIdentityLookup>(),
            state.Object,
            continuations.Object,
            resetter.Object,
            verifier.Object,
            bindingRefresh: refresh.Object,
            migrationOtp: MigrationOtpReturning(RecoveryProofOutcome.Success),
            aliasBindings: [persistedBinding],
            migrationStateRecords: [persistedState]);

        var result = await sut.CommitAsync(new MigrationCommitCeremonyRequest(
            "opaque",
            "new-password",
            Context(),
            "otp-proof"));

        Assert.Equal(MigrationCeremonyOutcome.Completed, result.Outcome);
        Assert.False(user.EmailMfaEnabled);
    }

    [Fact]
    public async Task BeginAsync_RequiredOtpWithoutAssuredProfile_IssuesPendingContinuation()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "account" };
        var directoryObjectId = Guid.NewGuid();
        var binding = new DirectoryObjectBinding("provider", "subject", directoryObjectId, "account");
        var proof = new Mock<IProofProvider>(MockBehavior.Strict);
        proof.Setup(service => service.ProveAsync(
                It.IsAny<ProofRequest>(),
                "current-password",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessfulProof("account"));
        var lookup = new Mock<IDirectoryIdentityLookup>(MockBehavior.Strict);
        lookup.Setup(service => service.FindManagedIdentityAsync("account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryLookupResult(DirectoryLookupOutcome.Found, Identity(directoryObjectId)));
        var state = new Mock<ICredentialMigrationStateStore>(MockBehavior.Strict);
        state.Setup(service => service.FindAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CredentialMigrationRecord?)null);
        state.Setup(service => service.EnsureRequiredAsync(
                user.Id,
                It.IsAny<DirectoryObjectBinding>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialMigrationRecord(user.Id, binding, CredentialMigrationState.Required));
        state.Setup(service => service.AdvanceToProofValidatedAsync(
                user.Id,
                EffectiveEmailOtpRequirement.Required,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialMigrationRecord(
                user.Id,
                binding,
                CredentialMigrationState.ProofValidated,
                EffectiveEmailOtpRequirement.Required));
        var continuations = new Mock<IMigrationContinuationStore>(MockBehavior.Strict);
        continuations.Setup(service => service.CreateAsync(
                It.IsAny<MigrationContinuationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationContinuation("opaque", DateTimeOffset.UtcNow.AddMinutes(10)));
        var sut = CreateService(
            user,
            proof.Object,
            lookup.Object,
            state.Object,
            continuations.Object,
            policyDecision: new MigrationPolicyDecision(true, EmailOtpPolicy.Required));

        var result = await sut.BeginAsync(new MigrationProofCeremonyRequest(
            "account",
            "current-password",
            Context(),
            EmailOtpPolicy.Required));

        Assert.Equal(MigrationCeremonyOutcome.ContinuationIssued, result.Outcome);
        Assert.True(result.RequiresEmailOtp);
    }

    [Fact]
    public async Task BeginAsync_ProofValidatedAfterExpiredContinuation_AllowsFreshProofRestart()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "account" };
        var directoryObjectId = Guid.NewGuid();
        var binding = new DirectoryObjectBinding("provider", "subject", directoryObjectId, "account");
        var now = DateTimeOffset.UtcNow;
        var persistedBinding = new ProviderSubjectDirectoryBinding(
            user.Id,
            "provider",
            "subject",
            directoryObjectId,
            now.UtcDateTime);
        var persistedState = new CredentialMigrationStateRecord(user.Id, persistedBinding.Id, now.AddMinutes(-20));
        persistedState.Advance(
            CredentialMigrationState.ProofValidated,
            EffectiveEmailOtpRequirement.Required,
            now.AddMinutes(-19));
        var expiredContinuation = new CredentialMigrationContinuationRecord(
            persistedState.Id,
            "expired-token-hash",
            "context-hash",
            "csrf-hash",
            now.AddMinutes(-10),
            now.AddMinutes(-5));
        var record = new CredentialMigrationRecord(
            user.Id,
            binding,
            CredentialMigrationState.ProofValidated,
            EffectiveEmailOtpRequirement.Required);
        var proof = new Mock<IProofProvider>(MockBehavior.Strict);
        proof.Setup(service => service.ProveAsync(
                It.IsAny<ProofRequest>(),
                "current-password",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessfulProof("account"));
        var lookup = new Mock<IDirectoryIdentityLookup>(MockBehavior.Strict);
        lookup.Setup(service => service.FindManagedIdentityAsync("account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryLookupResult(DirectoryLookupOutcome.Found, Identity(directoryObjectId)));
        var state = new Mock<ICredentialMigrationStateStore>(MockBehavior.Strict);
        state.Setup(service => service.FindAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        var continuations = new Mock<IMigrationContinuationStore>(MockBehavior.Strict);
        continuations.Setup(service => service.CreateAsync(
                It.IsAny<MigrationContinuationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationContinuation("fresh", DateTimeOffset.UtcNow.AddMinutes(10)));
        var sut = CreateService(
            user,
            proof.Object,
            lookup.Object,
            state.Object,
            continuations.Object,
            aliasBindings: [persistedBinding],
            migrationStateRecords: [persistedState],
            continuationRecords: [expiredContinuation],
            policyDecision: new MigrationPolicyDecision(true, EmailOtpPolicy.Required));

        var result = await sut.BeginAsync(new MigrationProofCeremonyRequest(
            "account",
            "current-password",
            Context(),
            EmailOtpPolicy.Required));

        Assert.Equal(MigrationCeremonyOutcome.ContinuationIssued, result.Outcome);
        Assert.Equal("fresh", result.Continuation);
        state.Verify(service => service.EnsureRequiredAsync(
            It.IsAny<Guid>(),
            It.IsAny<DirectoryObjectBinding>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BeginAsync_ProofValidatedWithConsumedContinuation_DeniesFreshProofRestart()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "account" };
        var directoryObjectId = Guid.NewGuid();
        var binding = new DirectoryObjectBinding("provider", "subject", directoryObjectId, "account");
        var now = DateTimeOffset.UtcNow;
        var persistedBinding = new ProviderSubjectDirectoryBinding(
            user.Id,
            "provider",
            "subject",
            directoryObjectId,
            now.UtcDateTime);
        var persistedState = new CredentialMigrationStateRecord(user.Id, persistedBinding.Id, now.AddMinutes(-20));
        persistedState.Advance(
            CredentialMigrationState.ProofValidated,
            EffectiveEmailOtpRequirement.Required,
            now.AddMinutes(-19));
        var consumedContinuation = new CredentialMigrationContinuationRecord(
            persistedState.Id,
            "consumed-token-hash",
            "context-hash",
            "csrf-hash",
            now.AddMinutes(-10),
            now.AddMinutes(10));
        Assert.True(consumedContinuation.TryConsume(now.AddMinutes(-5)));
        var record = new CredentialMigrationRecord(
            user.Id,
            binding,
            CredentialMigrationState.ProofValidated,
            EffectiveEmailOtpRequirement.Required);
        var proof = new Mock<IProofProvider>(MockBehavior.Strict);
        proof.Setup(service => service.ProveAsync(
                It.IsAny<ProofRequest>(),
                "current-password",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessfulProof("account"));
        var lookup = new Mock<IDirectoryIdentityLookup>(MockBehavior.Strict);
        lookup.Setup(service => service.FindManagedIdentityAsync("account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryLookupResult(DirectoryLookupOutcome.Found, Identity(directoryObjectId)));
        var state = new Mock<ICredentialMigrationStateStore>(MockBehavior.Strict);
        state.Setup(service => service.FindAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        var continuations = new Mock<IMigrationContinuationStore>(MockBehavior.Strict);
        var sut = CreateService(
            user,
            proof.Object,
            lookup.Object,
            state.Object,
            continuations.Object,
            aliasBindings: [persistedBinding],
            migrationStateRecords: [persistedState],
            continuationRecords: [consumedContinuation],
            policyDecision: new MigrationPolicyDecision(true, EmailOtpPolicy.Required));

        var result = await sut.BeginAsync(new MigrationProofCeremonyRequest(
            "account",
            "current-password",
            Context(),
            EmailOtpPolicy.Required));

        Assert.Equal(MigrationCeremonyOutcome.Denied, result.Outcome);
        continuations.Verify(service => service.CreateAsync(
            It.IsAny<MigrationContinuationRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AuthenticateCompletedAsync_ProfileRefreshFailure_PreservesDirectoryAuthenticationAndExactBinding()
    {
        var accountId = Guid.NewGuid();
        var binding = new DirectoryObjectBinding("provider", "subject", Guid.NewGuid());
        var state = new Mock<ICredentialMigrationStateStore>(MockBehavior.Strict);
        state.Setup(service => service.FindAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialMigrationRecord(accountId, binding, CredentialMigrationState.LocalFinalized));
        var authenticator = new Mock<IDirectoryCredentialAuthenticator>(MockBehavior.Strict);
        authenticator.Setup(service => service.AuthenticateAsync(binding.DirectoryObjectId, "directory-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryCredentialResult(DirectoryCredentialOutcome.Authenticated, Identity(binding.DirectoryObjectId)));
        var refresh = new Mock<IStage1BindingRefreshService>(MockBehavior.Strict);
        refresh.Setup(service => service.BindAndRefreshAsync(
                It.Is<Stage1BindingRefreshRequest>(request =>
                    request.LocalAccountId == accountId &&
                    request.ProviderNamespace == binding.ProviderNamespace &&
                    request.StableSubject == binding.StableSubject &&
                    request.DirectoryIdentity.ObjectId == binding.DirectoryObjectId),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        var sut = CreateService(
            new ApplicationUser { Id = accountId, UserName = "account" },
            Mock.Of<IProofProvider>(),
            Mock.Of<IDirectoryIdentityLookup>(),
            state.Object,
            Mock.Of<IMigrationContinuationStore>(),
            authenticator: authenticator.Object,
            bindingRefresh: refresh.Object);

        var result = await sut.AuthenticateCompletedAsync(accountId, "directory-password");

        Assert.Equal(DirectoryCredentialOutcome.Authenticated, result.Outcome);
        refresh.VerifyAll();
    }

    [Fact]
    public async Task SanitizedMigrationAudit_RecordAsync_EmitsOnlyTypedCategoryStateAndCorrelation()
    {
        var logger = new CapturingLogger<SanitizedMigrationAudit>();
        var audit = new SanitizedMigrationAudit(logger);

        await audit.RecordAsync(new SanitizedMigrationAuditEvent(
            Guid.Parse("3c1f8b42-0b0d-48d7-9d7e-7d9c40de9eb4"),
            SanitizedMigrationAuditCategory.ProofValidated,
            CredentialMigrationState.ProofValidated));

        Assert.Equal(LogLevel.Information, logger.Level);
        Assert.Equal(
            ["Category", "State", "CorrelationId", "{OriginalFormat}"],
            logger.Fields.Select(field => field.Key));
        Assert.DoesNotContain("password", logger.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("continuation", logger.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", logger.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("distinguished", logger.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider", logger.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Stage2CredentialMigrationService CreateService(
        ApplicationUser user,
        IProofProvider proof,
        IDirectoryIdentityLookup lookup,
        ICredentialMigrationStateStore state,
        IMigrationContinuationStore continuations,
        IDirectoryCredentialResetter? resetter = null,
        IDirectoryCredentialVerifier? verifier = null,
        IDirectoryCredentialAuthenticator? authenticator = null,
        IStage1BindingRefreshService? bindingRefresh = null,
        IMigrationOtpProofService? migrationOtp = null,
        IRecoveryAssistanceService? recoveryAssistance = null,
        IReadOnlyCollection<ApplicationUser>? extraUsers = null,
        IReadOnlyCollection<ProviderSubjectDirectoryBinding>? aliasBindings = null,
        IReadOnlyCollection<CredentialMigrationStateRecord>? migrationStateRecords = null,
        IReadOnlyCollection<CredentialMigrationContinuationRecord>? continuationRecords = null,
        string? preProofCandidateAccount = null,
        MigrationPolicyDecision? policyDecision = null)
    {
        var userManager = UserManager();
        userManager.Setup(manager => manager.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        userManager.Setup(manager => manager.FindByNameAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        if (preProofCandidateAccount is not null)
        {
            userManager.Setup(manager => manager.FindByEmailAsync(preProofCandidateAccount)).ReturnsAsync(user);
        }
        userManager.Setup(manager => manager.FindByLoginAsync("provider", "subject")).ReturnsAsync(user);
        userManager.Setup(manager => manager.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        userManager.Setup(manager => manager.IsLockedOutAsync(user)).ReturnsAsync(false);
        var dbContext = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"stage2-{Guid.NewGuid():N}")
            .Options);
        dbContext.Users.Add(user);
        if (extraUsers is not null)
        {
            dbContext.Users.AddRange(extraUsers);
        }
        if (aliasBindings is not null)
        {
            dbContext.ProviderSubjectDirectoryBindings.AddRange(aliasBindings);
        }
        if (migrationStateRecords is not null)
        {
            dbContext.CredentialMigrationStateRecords.AddRange(migrationStateRecords);
        }
        if (continuationRecords is not null)
        {
            dbContext.CredentialMigrationContinuations.AddRange(continuationRecords);
        }
        dbContext.SaveChanges();
        var policy = new Mock<ICredentialMigrationPolicy>();
        policy.Setup(service => service.Evaluate(It.IsAny<MigrationPolicyRequest>(), It.IsAny<DateTimeOffset>()))
            .Returns(policyDecision ?? new MigrationPolicyDecision(true, EmailOtpPolicy.Disabled));
        migrationOtp ??= MigrationOtpReturning(RecoveryProofOutcome.Missing);
        recoveryAssistance ??= RecoveryAssistanceReturning(RecoveryProofOutcome.Missing);
        return new Stage2CredentialMigrationService(
            userManager.Object,
            proof,
            lookup,
            authenticator ?? Mock.Of<IDirectoryCredentialAuthenticator>(),
            resetter ?? Mock.Of<IDirectoryCredentialResetter>(),
            verifier ?? Mock.Of<IDirectoryCredentialVerifier>(),
            policy.Object,
            state,
            continuations,
            migrationOtp,
            recoveryAssistance,
            bindingRefresh ?? Mock.Of<IStage1BindingRefreshService>(),
            dbContext,
            Mock.Of<ISanitizedMigrationAudit>(),
            Mock.Of<ILegacyPasswordSyncCoordinator>(),
            Options.Create(new DirectoryIntegrationOptions
            {
                Enabled = true,
                AuthenticationEnabled = true
            }),
            Options.Create(new LegacyPasswordSyncOptions()));
    }

    private static IMigrationOtpProofService MigrationOtpReturning(RecoveryProofOutcome outcome)
    {
        var service = new Mock<IMigrationOtpProofService>();
        service.Setup(candidate => candidate.ConsumeAsync(
                It.IsAny<MigrationOtpConsumptionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);
        return service.Object;
    }

    private static IRecoveryAssistanceService RecoveryAssistanceReturning(RecoveryProofOutcome outcome)
    {
        var service = new Mock<IRecoveryAssistanceService>();
        service.Setup(candidate => candidate.GetResetApprovalStatusAsync(
                It.IsAny<ResetApprovalConsumptionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);
        service.Setup(candidate => candidate.ConsumeResetApprovalAsync(
                It.IsAny<ResetApprovalConsumptionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);
        return service.Object;
    }

    private static Mock<UserManager<ApplicationUser>> UserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object,
            Options.Create(new IdentityOptions()),
            Mock.Of<IPasswordHasher<ApplicationUser>>(),
            new List<IUserValidator<ApplicationUser>>(),
            new List<IPasswordValidator<ApplicationUser>>(),
            Mock.Of<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            Mock.Of<IServiceProvider>(),
            Mock.Of<ILogger<UserManager<ApplicationUser>>>());
    }

    private static ProofResult SuccessfulProof(string account) => new()
    {
        Outcome = ProofOutcome.Authenticated,
        ProviderNamespace = "provider",
        StableSubject = "subject",
        CanonicalAccount = account,
        Assurance = new ProofAssurance { StableSubjectAssured = true, CanonicalAccountAssured = true }
    };

    private static ManagedDirectoryIdentity Identity(Guid id) =>
        new(id, "account", true, true, false);

    private static MigrationContinuationContext Context() =>
        new(new string('A', 64), new string('B', 64));

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public LogLevel Level { get; private set; }
        public IReadOnlyList<KeyValuePair<string, object?>> Fields { get; private set; } = [];
        public string Message { get; private set; } = string.Empty;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Level = logLevel;
            Fields = state is IEnumerable<KeyValuePair<string, object?>> fields
                ? fields.ToArray()
                : [];
            Message = formatter(state, exception);
        }
    }
}
