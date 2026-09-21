using Core.Application.Ports;
using Core.Domain.Entities;

namespace Infrastructure.Services;

/// <summary>
/// Reconciles only independently established committed evidence without replaying proof or continuations.
/// </summary>
public sealed class CredentialMigrationRecoveryService : ICredentialMigrationRecoveryService
{
    private readonly ICredentialMigrationStateStore _stateStore;
    private readonly IMigrationContinuationStore _continuations;
    private readonly IMigrationRecoveryAuthorizer _authorizer;
    private readonly IMigrationRecoveryEvidenceReader _evidenceReader;

    public CredentialMigrationRecoveryService(
        ICredentialMigrationStateStore stateStore,
        IMigrationContinuationStore continuations,
        IMigrationRecoveryAuthorizer authorizer,
        IMigrationRecoveryEvidenceReader evidenceReader)
    {
        _stateStore = stateStore;
        _continuations = continuations;
        _authorizer = authorizer;
        _evidenceReader = evidenceReader;
    }

    public async Task<MigrationRecoveryContinuationResult> BeginDirectoryRecoveryAsync(
        MigrationRecoveryBeginRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorizer.IsAuthorizedAsync(cancellationToken))
        {
            return new MigrationRecoveryContinuationResult(MigrationRecoveryOutcome.Unauthorized);
        }

        if (request.LocalAccountId == Guid.Empty)
        {
            return new MigrationRecoveryContinuationResult(MigrationRecoveryOutcome.Unresolved);
        }

        var record = await _stateStore.FindAsync(request.LocalAccountId, cancellationToken);
        if (record?.State != CredentialMigrationState.ProofValidated ||
            !CredentialMigrationStateRecord.IsPersistableRequirement(record.EffectiveEmailOtpRequirement))
        {
            return new MigrationRecoveryContinuationResult(MigrationRecoveryOutcome.Unresolved);
        }

        try
        {
            var continuation = await _continuations.CreateAsync(
                new MigrationContinuationRequest(record.LocalAccountId, record.Binding, request.Context),
                cancellationToken);
            return new MigrationRecoveryContinuationResult(MigrationRecoveryOutcome.Reconciled, continuation.ProtectedValue);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new MigrationRecoveryContinuationResult(MigrationRecoveryOutcome.Unavailable);
        }
    }

    public async Task<MigrationRecoveryResult> RecoverAsync(
        MigrationRecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorizer.IsAuthorizedAsync(cancellationToken))
        {
            return new MigrationRecoveryResult(MigrationRecoveryOutcome.Unauthorized);
        }

        return !string.IsNullOrWhiteSpace(request.Continuation)
            ? await RecoverDirectoryCommitmentAsync(request, cancellationToken)
            : await RecoverLocalFinalizationAsync(request, cancellationToken);
    }

    private async Task<MigrationRecoveryResult> RecoverDirectoryCommitmentAsync(
        MigrationRecoveryRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.NewPassword) || request.Context is null)
        {
            return new MigrationRecoveryResult(MigrationRecoveryOutcome.Unresolved);
        }

        var consumed = await _continuations.ConsumeAsync(request.Continuation!, request.Context, cancellationToken);
        if (consumed.Outcome != MigrationContinuationConsumptionOutcome.Consumed ||
            consumed.Record?.State != CredentialMigrationState.ProofValidated ||
            !CredentialMigrationStateRecord.IsPersistableRequirement(consumed.Record.EffectiveEmailOtpRequirement))
        {
            return new MigrationRecoveryResult(MigrationRecoveryOutcome.Unresolved);
        }

        var evidence = await _evidenceReader.ReadDirectoryCommitmentAsync(
            consumed.Record,
            request.NewPassword,
            cancellationToken);
        if (evidence != MigrationCommittedEvidence.DirectoryCredentialCommitted)
        {
            return new MigrationRecoveryResult(MigrationRecoveryOutcome.Unresolved);
        }

        try
        {
            await _stateStore.AdvanceAsync(
                consumed.Record.LocalAccountId,
                CredentialMigrationState.ProofValidated,
                CredentialMigrationState.DirectoryCredentialCommitted,
                cancellationToken);
            return new MigrationRecoveryResult(MigrationRecoveryOutcome.Reconciled);
        }
        catch (InvalidOperationException)
        {
            var latest = await _stateStore.FindAsync(consumed.Record.LocalAccountId, cancellationToken);
            return latest is not null && latest.State is (CredentialMigrationState.DirectoryCredentialCommitted or CredentialMigrationState.LocalFinalized)
                ? new MigrationRecoveryResult(MigrationRecoveryOutcome.Reconciled)
                : new MigrationRecoveryResult(MigrationRecoveryOutcome.Unresolved);
        }
    }

    private async Task<MigrationRecoveryResult> RecoverLocalFinalizationAsync(
        MigrationRecoveryRequest request,
        CancellationToken cancellationToken)
    {
        if (request.LocalAccountId == Guid.Empty)
        {
            return new MigrationRecoveryResult(MigrationRecoveryOutcome.Unresolved);
        }

        var record = await _stateStore.FindAsync(request.LocalAccountId, cancellationToken);
        if (record is null || record.State is not (CredentialMigrationState.DirectoryCredentialCommitted or CredentialMigrationState.LocalFinalized))
        {
            return new MigrationRecoveryResult(MigrationRecoveryOutcome.Unresolved);
        }

        var evidence = await _evidenceReader.ReadLocalFinalizationAsync(record, cancellationToken);
        if (evidence != MigrationCommittedEvidence.LocalFinalized)
        {
            return new MigrationRecoveryResult(MigrationRecoveryOutcome.Unresolved);
        }

        if (record.State == CredentialMigrationState.LocalFinalized)
        {
            return new MigrationRecoveryResult(MigrationRecoveryOutcome.Reconciled);
        }

        try
        {
            await _stateStore.AdvanceAsync(
                record.LocalAccountId,
                CredentialMigrationState.DirectoryCredentialCommitted,
                CredentialMigrationState.LocalFinalized,
                cancellationToken);
            return new MigrationRecoveryResult(MigrationRecoveryOutcome.Reconciled);
        }
        catch (InvalidOperationException)
        {
            var latest = await _stateStore.FindAsync(record.LocalAccountId, cancellationToken);
            return latest?.State == CredentialMigrationState.LocalFinalized
                ? new MigrationRecoveryResult(MigrationRecoveryOutcome.Reconciled)
                : new MigrationRecoveryResult(MigrationRecoveryOutcome.Unresolved);
        }
    }
}
