using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using Core.Application.Ports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Web.IdP.Pages.Account;

namespace Tests.Web.IdP.UnitTests.Pages.Account;

public sealed class CredentialMigrationCeremonyTests
{
    [Fact]
    public async Task OnPostBeginAsync_ContinuationIssued_StoresOpaqueValueOnlyInSession()
    {
        var ceremony = new FakeCeremony
        {
            BeginResult = new MigrationProofCeremonyResult(
                MigrationCeremonyOutcome.ContinuationIssued,
                true,
                "opaque-continuation")
        };
        var session = new DictionarySession();
        var model = CreateModel(ceremony, session);
        model.Input = new CredentialMigrationModel.BeginInput
        {
            AccountName = "account",
            CurrentPassword = "current-password"
        };

        var result = await model.OnPostBeginAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(model.AwaitingNewPassword);
        Assert.True(model.AwaitingProof);
        Assert.Equal("opaque-continuation", session.GetString("credential-migration.continuation"));
        Assert.Equal(1, ceremony.BeginCalls);
        Assert.Equal(64, ceremony.LastBeginRequest?.Context.ContextHash.Length);
    }

    [Fact]
    public async Task OnPostBeginAsync_UnrelatedModelErrors_DoNotBlockSelectedInput()
    {
        var ceremony = new FakeCeremony
        {
            BeginResult = new MigrationProofCeremonyResult(
                MigrationCeremonyOutcome.ContinuationIssued,
                false,
                "opaque-continuation")
        };
        var model = CreateModel(ceremony, new DictionarySession());
        model.Input = new CredentialMigrationModel.BeginInput
        {
            AccountName = "account",
            CurrentPassword = "current-password"
        };
        model.ModelState.AddModelError("Commit.NewPassword", "required");
        model.ModelState.AddModelError("Recovery.LocalAccountId", "invalid-binding");
        model.ModelState.AddModelError("Proof.Code", "required");

        var result = await model.OnPostBeginAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(1, ceremony.BeginCalls);
        Assert.True(model.ModelState.IsValid);
    }

    [Fact]
    public async Task OnPostCommitAsync_UnrelatedModelErrors_DoNotBlockSelectedInput()
    {
        var ceremony = new FakeCeremony
        {
            BeginResult = new MigrationProofCeremonyResult(
                MigrationCeremonyOutcome.ContinuationIssued,
                false,
                "opaque-continuation")
        };
        var model = CreateModel(ceremony, new DictionarySession());
        model.Input = new CredentialMigrationModel.BeginInput
        {
            AccountName = "account",
            CurrentPassword = "current-password"
        };
        await model.OnPostBeginAsync(CancellationToken.None);
        model.ModelState.AddModelError("Input.AccountName", "required");
        model.ModelState.AddModelError("Recovery.LocalAccountId", "invalid-binding");
        model.ModelState.AddModelError("Proof.Code", "required");
        model.Commit = new CredentialMigrationModel.CommitInput
        {
            NewPassword = "new-password",
            ConfirmPassword = "new-password"
        };

        var result = await model.OnPostCommitAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(1, ceremony.CommitCalls);
        Assert.True(model.ModelState.IsValid);
    }

    [Fact]
    public async Task OnPostCommitAsync_MissingContinuation_ReturnsUniformDeniedPageWithoutCallingService()
    {
        var ceremony = new FakeCeremony();
        var model = CreateModel(ceremony, new DictionarySession());
        model.Commit = new CredentialMigrationModel.CommitInput { NewPassword = "new-password" };

        var result = await model.OnPostCommitAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(0, ceremony.CommitCalls);
        Assert.True(model.RequestDenied);
    }

    [Fact]
    public void CommitInput_MismatchedConfirmation_IsInvalid()
    {
        var input = new CredentialMigrationModel.CommitInput
        {
            NewPassword = "new-password",
            ConfirmPassword = "different-password"
        };
        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            input,
            new ValidationContext(input),
            validationResults,
            validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(validationResults, result =>
            result.ErrorMessage == "CredentialMigration.PasswordMismatch");
    }

    [Fact]
    public async Task Commit_InvalidInputWithContinuation_PreservesSecondStep()
    {
        var ceremony = new FakeCeremony();
        var session = new DictionarySession();
        var model = CreateModel(ceremony, session);
        session.SetString("credential-migration.continuation", "continuation");
        model.ModelState.AddModelError("Commit.ConfirmPassword", "CredentialMigration.PasswordMismatch");

        var result = await model.OnPostCommitAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(model.AwaitingNewPassword);
        Assert.False(model.RequestDenied);
        Assert.Equal("continuation", session.GetString("credential-migration.continuation"));
        Assert.Equal(0, ceremony.CommitCalls);
        Assert.False(model.ModelState.IsValid);
        Assert.True(model.ModelState.ContainsKey("Commit.ConfirmPassword"));
    }

    [Fact]
    public async Task RecoveryHandlers_StoreOnlyOpaqueContinuationAndPassPasswordOnce()
    {
        var ceremony = new FakeCeremony();
        var recovery = new FakeRecovery
        {
            BeginResult = new MigrationRecoveryContinuationResult(MigrationRecoveryOutcome.Reconciled, "opaque-recovery")
        };
        var session = new DictionarySession();
        var model = CreateModel(ceremony, session, recovery);
        model.Recovery = new CredentialMigrationModel.RecoveryInput { LocalAccountId = Guid.NewGuid() };

        var begin = await model.OnPostRecoveryBeginAsync(CancellationToken.None);

        Assert.IsType<PageResult>(begin);
        Assert.Equal("opaque-recovery", session.GetString("credential-migration.recovery-continuation"));
        Assert.DoesNotContain(session.Keys, key => key.Contains("password", StringComparison.OrdinalIgnoreCase));

        model.Recovery.NewPassword = "ephemeral-password";
        var commit = await model.OnPostRecoveryCommitAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(commit);
        Assert.Equal("ephemeral-password", recovery.LastRequest?.NewPassword);
        Assert.DoesNotContain(session.Keys, key => key.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RequiredOtp_MissingRecoveryEmail_PreservesPendingCeremonyForAdminAssistance()
    {
        var ceremony = new FakeCeremony
        {
            BeginResult = new MigrationProofCeremonyResult(
                MigrationCeremonyOutcome.ContinuationIssued,
                true,
                "opaque-continuation")
        };
        var otp = new FakeMigrationOtp
        {
            SendResult = new MigrationOtpSendResult(RecoveryProofOutcome.Missing)
        };
        var session = new DictionarySession();
        var model = CreateModel(ceremony, session, migrationOtp: otp);
        model.Input = new CredentialMigrationModel.BeginInput
        {
            AccountName = "account",
            CurrentPassword = "current-password"
        };

        var result = await model.OnPostBeginAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(model.AwaitingProof);
        Assert.False(model.AwaitingNewPassword);
        Assert.Equal(CredentialMigrationOutcomeCodes.RecoveryEmailOrAdminRequired, model.ProofOutcomeCode);
        Assert.Equal("opaque-continuation", session.GetString("credential-migration.continuation"));
    }

    [Fact]
    public async Task VerifyOtp_SuccessStoresOpaqueProofAndCommitPassesItOnce()
    {
        var ceremony = new FakeCeremony
        {
            BeginResult = new MigrationProofCeremonyResult(
                MigrationCeremonyOutcome.ContinuationIssued,
                true,
                "opaque-continuation")
        };
        var otp = new FakeMigrationOtp
        {
            SendResult = new MigrationOtpSendResult(RecoveryProofOutcome.Success, 60),
            VerifyResult = new MigrationOtpVerificationResult(RecoveryProofOutcome.Success, "opaque-proof")
        };
        var session = new DictionarySession();
        var model = CreateModel(ceremony, session, migrationOtp: otp);
        model.Input = new CredentialMigrationModel.BeginInput
        {
            AccountName = "account",
            CurrentPassword = "current-password"
        };
        await model.OnPostBeginAsync(CancellationToken.None);
        model.Proof = new CredentialMigrationModel.ProofInput { Code = "123456" };

        var verified = await model.OnPostVerifyOtpAsync(CancellationToken.None);

        Assert.IsType<PageResult>(verified);
        Assert.True(model.AwaitingNewPassword);
        Assert.Equal("opaque-proof", session.GetString("credential-migration.otp-proof"));

        model.Commit = new CredentialMigrationModel.CommitInput
        {
            NewPassword = "new-password",
            ConfirmPassword = "new-password"
        };
        var committed = await model.OnPostCommitAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(committed);
        Assert.Equal("opaque-proof", ceremony.LastCommitRequest?.MigrationOtpProof);
        Assert.DoesNotContain(session.Keys, key => key.Contains("proof", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CheckApproval_UsesReadOnlyStatusAndCommitCarriesNoApprovalAuthority()
    {
        var ceremony = new FakeCeremony
        {
            BeginResult = new MigrationProofCeremonyResult(
                MigrationCeremonyOutcome.ContinuationIssued,
                true,
                "opaque-continuation")
        };
        var otp = new FakeMigrationOtp
        {
            SendResult = new MigrationOtpSendResult(RecoveryProofOutcome.Missing)
        };
        var assistance = new FakeAssistance { StatusOutcome = RecoveryProofOutcome.Success };
        var session = new DictionarySession();
        var model = CreateModel(ceremony, session, migrationOtp: otp, recoveryAssistance: assistance);
        model.Input = new CredentialMigrationModel.BeginInput
        {
            AccountName = "account",
            CurrentPassword = "current-password"
        };
        await model.OnPostBeginAsync(CancellationToken.None);

        var checkedResult = await model.OnPostCheckApprovalAsync(CancellationToken.None);

        Assert.IsType<PageResult>(checkedResult);
        Assert.True(model.AwaitingNewPassword);
        Assert.Equal(1, assistance.StatusCalls);
        Assert.Equal(0, assistance.ConsumeCalls);
        model.Commit = new CredentialMigrationModel.CommitInput
        {
            NewPassword = "new-password",
            ConfirmPassword = "new-password"
        };

        await model.OnPostCommitAsync(CancellationToken.None);

        Assert.Null(ceremony.LastCommitRequest?.MigrationOtpProof);
    }

    [Fact]
    public void MigrationProofInput_HasCodeOnlyAndNoAddressField()
    {
        Assert.Equal(["Code"], typeof(CredentialMigrationModel.ProofInput)
            .GetProperties()
            .Select(property => property.Name));
    }

    private static CredentialMigrationModel CreateModel(
        FakeCeremony ceremony,
        ISession session,
        ICredentialMigrationRecoveryService? recovery = null,
        IMigrationOtpProofService? migrationOtp = null,
        IRecoveryEmailService? recoveryEmail = null,
        IRecoveryAssistanceService? recoveryAssistance = null)
    {
        var httpContext = new DefaultHttpContext { Session = session };
        return new CredentialMigrationModel(ceremony, recovery, migrationOtp, recoveryEmail, recoveryAssistance)
        {
            PageContext = new PageContext { HttpContext = httpContext }
        };
    }

    private sealed class FakeCeremony : IStage2CredentialMigrationService
    {
        public int BeginCalls { get; private set; }
        public int CommitCalls { get; private set; }
        public MigrationProofCeremonyRequest? LastBeginRequest { get; private set; }
        public MigrationCommitCeremonyRequest? LastCommitRequest { get; private set; }
        public MigrationProofCeremonyResult BeginResult { get; set; } = new(MigrationCeremonyOutcome.Denied);

        public Task<MigrationProofCeremonyResult> BeginAsync(
            MigrationProofCeremonyRequest request,
            CancellationToken cancellationToken = default)
        {
            BeginCalls++;
            LastBeginRequest = request;
            return Task.FromResult(BeginResult);
        }

        public Task<MigrationCommitCeremonyResult> CommitAsync(
            MigrationCommitCeremonyRequest request,
            CancellationToken cancellationToken = default)
        {
            CommitCalls++;
            LastCommitRequest = request;
            return Task.FromResult(new MigrationCommitCeremonyResult(MigrationCeremonyOutcome.Completed));
        }

        public Task<DirectoryCredentialResult> AuthenticateCompletedAsync(
            Guid localAccountId,
            string password,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectoryCredentialResult(DirectoryCredentialOutcome.InvalidCredentials));
    }

    private sealed class FakeMigrationOtp : IMigrationOtpProofService
    {
        public MigrationOtpSendResult SendResult { get; set; } = new(RecoveryProofOutcome.Missing);
        public MigrationOtpVerificationResult VerifyResult { get; set; } = new(RecoveryProofOutcome.Invalid);

        public Task<MigrationOtpSendResult> SendAsync(
            MigrationOtpSendRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(SendResult);

        public Task<MigrationOtpVerificationResult> VerifyAsync(
            MigrationOtpVerificationRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(VerifyResult);

        public Task<RecoveryProofOutcome> ConsumeAsync(
            MigrationOtpConsumptionRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(RecoveryProofOutcome.Success);
    }

    private sealed class FakeAssistance : IRecoveryAssistanceService
    {
        public RecoveryProofOutcome StatusOutcome { get; set; } = RecoveryProofOutcome.Missing;
        public int StatusCalls { get; private set; }
        public int ConsumeCalls { get; private set; }

        public Task<MigrationOtpSendResult> ResendMigrationOtpAsync(
            AdminMigrationOtpResendRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MigrationOtpSendResult(RecoveryProofOutcome.Unavailable));

        public Task<RecoveryEmailChangeResult> ReplaceRecoveryEmailAsync(
            AdminRecoveryEmailReplacementRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new RecoveryEmailChangeResult(RecoveryProofOutcome.Unavailable));

        public Task<ResetApprovalIssueResult> IssueResetApprovalAsync(
            AdminResetApprovalRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResetApprovalIssueResult(RecoveryProofOutcome.Unavailable));

        public Task<RecoveryProofOutcome> GetResetApprovalStatusAsync(
            ResetApprovalConsumptionRequest request,
            CancellationToken cancellationToken = default)
        {
            StatusCalls++;
            return Task.FromResult(StatusOutcome);
        }

        public Task<RecoveryProofOutcome> ConsumeResetApprovalAsync(
            ResetApprovalConsumptionRequest request,
            CancellationToken cancellationToken = default)
        {
            ConsumeCalls++;
            return Task.FromResult(RecoveryProofOutcome.Success);
        }
    }

    private sealed class FakeRecovery : ICredentialMigrationRecoveryService
    {
        public MigrationRecoveryContinuationResult BeginResult { get; set; } =
            new(MigrationRecoveryOutcome.Unresolved);
        public MigrationRecoveryRequest? LastRequest { get; private set; }

        public Task<MigrationRecoveryContinuationResult> BeginDirectoryRecoveryAsync(
            MigrationRecoveryBeginRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(BeginResult);

        public Task<MigrationRecoveryResult> RecoverAsync(
            MigrationRecoveryRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new MigrationRecoveryResult(MigrationRecoveryOutcome.Reconciled));
        }
    }

    private sealed class DictionarySession : ISession
    {
        private readonly ConcurrentDictionary<string, byte[]> _values = new();

        public IEnumerable<string> Keys => _values.Keys;
        public string Id => "test-session";
        public bool IsAvailable => true;

        public void Clear() => _values.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _values.TryRemove(key, out _);
        public void Set(string key, byte[] value) => _values[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _values.TryGetValue(key, out value!);
    }
}
