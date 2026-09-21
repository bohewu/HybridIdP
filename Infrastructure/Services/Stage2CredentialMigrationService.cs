using Core.Application;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

/// <summary>
/// Performs the ordered Stage 2 proof, reset, independent verification, and local-finalization ceremony.
/// Passwords are passed only to the proof or directory port and are never retained.
/// </summary>
public sealed class Stage2CredentialMigrationService : IStage2CredentialMigrationService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IProofProvider _proofProvider;
    private readonly IDirectoryIdentityLookup _directoryLookup;
    private readonly IDirectoryCredentialAuthenticator _directoryAuthenticator;
    private readonly IDirectoryCredentialResetter _directoryResetter;
    private readonly IDirectoryCredentialVerifier _directoryVerifier;
    private readonly ICredentialMigrationPolicy _policy;
    private readonly ICredentialMigrationStateStore _stateStore;
    private readonly IMigrationContinuationStore _continuations;
    private readonly IMigrationOtpProofService _migrationOtpProofService;
    private readonly IRecoveryAssistanceService _recoveryAssistanceService;
    private readonly IStage1BindingRefreshService _bindingRefresh;
    private readonly IApplicationDbContext _dbContext;
    private readonly ISanitizedMigrationAudit _audit;
    private readonly ILegacyPasswordSyncCoordinator _passwordSyncCoordinator;
    private readonly DirectoryIntegrationOptions _directoryOptions;
    private readonly LegacyPasswordSyncOptions _legacyOptions;
    private readonly TimeProvider _timeProvider;

    public Stage2CredentialMigrationService(
        UserManager<ApplicationUser> userManager,
        IProofProvider proofProvider,
        IDirectoryIdentityLookup directoryLookup,
        IDirectoryCredentialAuthenticator directoryAuthenticator,
        IDirectoryCredentialResetter directoryResetter,
        IDirectoryCredentialVerifier directoryVerifier,
        ICredentialMigrationPolicy policy,
        ICredentialMigrationStateStore stateStore,
        IMigrationContinuationStore continuations,
        IMigrationOtpProofService migrationOtpProofService,
        IRecoveryAssistanceService recoveryAssistanceService,
        IStage1BindingRefreshService bindingRefresh,
        IApplicationDbContext dbContext,
        ISanitizedMigrationAudit audit,
        ILegacyPasswordSyncCoordinator passwordSyncCoordinator,
        IOptions<DirectoryIntegrationOptions> directoryOptions,
        IOptions<LegacyPasswordSyncOptions> legacyOptions,
        TimeProvider? timeProvider = null)
    {
        _userManager = userManager;
        _proofProvider = proofProvider;
        _directoryLookup = directoryLookup;
        _directoryAuthenticator = directoryAuthenticator;
        _directoryResetter = directoryResetter;
        _directoryVerifier = directoryVerifier;
        _policy = policy;
        _stateStore = stateStore;
        _continuations = continuations;
        _migrationOtpProofService = migrationOtpProofService;
        _recoveryAssistanceService = recoveryAssistanceService;
        _bindingRefresh = bindingRefresh;
        _dbContext = dbContext;
        _audit = audit;
        _passwordSyncCoordinator = passwordSyncCoordinator;
        _directoryOptions = directoryOptions.Value;
        _legacyOptions = legacyOptions.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<MigrationProofCeremonyResult> BeginAsync(
        MigrationProofCeremonyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.AccountName) || string.IsNullOrEmpty(request.Password))
        {
            return await DenyProofAsync(cancellationToken);
        }

        try
        {
            // This is a denial-only routing check. The browser account value never selects a binding target.
            if (await IsCompletedCandidateAsync(request.AccountName, cancellationToken))
            {
                return await DenyProofAsync(cancellationToken, CredentialMigrationState.LocalFinalized);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return await UnavailableAsync(SanitizedMigrationAuditCategory.DirectoryUnavailable, cancellationToken);
        }

        // AccountName is proof input only. It is not used to choose a local account or directory target.
        var policy = _policy.Evaluate(
            new MigrationPolicyRequest(Guid.Empty, false, request.RequestedEmailOtpPolicy),
            _timeProvider.GetUtcNow());
        if (!policy.AllowLegacyProof)
        {
            return await DenyProofAsync(cancellationToken);
        }

        ProofResult proof;
        try
        {
            proof = await _proofProvider.ProveAsync(
                new ProofRequest
                {
                    AccountName = request.AccountName,
                    RequestedEmailOtpPolicy = request.RequestedEmailOtpPolicy
                },
                request.Password,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return await UnavailableAsync(SanitizedMigrationAuditCategory.ProofDenied, cancellationToken);
        }

        if (!proof.TryValidate(out _) || proof.Outcome != ProofOutcome.Authenticated)
        {
            return await DenyProofAsync(cancellationToken);
        }

        DirectoryLookupResult lookup;
        try
        {
            lookup = await _directoryLookup.FindManagedIdentityAsync(proof.CanonicalAccount!, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return await UnavailableAsync(SanitizedMigrationAuditCategory.DirectoryUnavailable, cancellationToken);
        }

        if (lookup.Outcome != DirectoryLookupOutcome.Found || lookup.Identity is null ||
            !IsManagedIdentityValid(lookup.Identity, lookup.Identity.ObjectId))
        {
            return await DenyDirectoryAsync(cancellationToken);
        }

        var user = await ResolveLocalAccountAsync(proof, cancellationToken);
        if (user is null)
        {
            return await DenyProofAsync(cancellationToken);
        }

        var record = await _stateStore.FindAsync(user.Id, cancellationToken);
        if (record?.State is CredentialMigrationState.DirectoryCredentialCommitted or CredentialMigrationState.LocalFinalized)
        {
            return await DenyProofAsync(cancellationToken, record.State);
        }

        policy = _policy.Evaluate(
            new MigrationPolicyRequest(user.Id, false, request.RequestedEmailOtpPolicy),
            _timeProvider.GetUtcNow());
        if (!policy.AllowLegacyProof || !await CanMigrateAsync(user.Id, cancellationToken))
        {
            return await DenyProofAsync(cancellationToken, record?.State);
        }

        var effectiveEmailOtpRequirement = ToEffectiveEmailOtpRequirement(policy, proof.RequiredActions);
        if (!CredentialMigrationStateRecord.IsPersistableRequirement(effectiveEmailOtpRequirement))
        {
            return await DenyProofAsync(cancellationToken, record?.State);
        }

        var requiresEmailOtp = effectiveEmailOtpRequirement == EffectiveEmailOtpRequirement.Required;

        var binding = new DirectoryObjectBinding(
            proof.ProviderNamespace!,
            proof.StableSubject!,
            lookup.Identity.ObjectId,
            proof.CanonicalAccount);

        try
        {
            if (record?.State == CredentialMigrationState.ProofValidated)
            {
                if (!Matches(record.Binding, binding) ||
                    !CredentialMigrationStateRecord.IsPersistableRequirement(record.EffectiveEmailOtpRequirement) ||
                    await HasConsumedOrActiveContinuationAsync(user.Id, cancellationToken))
                {
                    return await DenyProofAsync(cancellationToken, record.State);
                }

                var restartedContinuation = await _continuations.CreateAsync(
                    new MigrationContinuationRequest(user.Id, binding, request.Context),
                    cancellationToken);
                await RecordAsync(SanitizedMigrationAuditCategory.ProofValidated, record.State, cancellationToken);
                return new MigrationProofCeremonyResult(
                    MigrationCeremonyOutcome.ContinuationIssued,
                    RequiresEmailOtp(record.EffectiveEmailOtpRequirement),
                    restartedContinuation.ProtectedValue);
            }

            record = await _stateStore.EnsureRequiredAsync(user.Id, binding, cancellationToken);
            if (record.State != CredentialMigrationState.Required || !Matches(record.Binding, binding))
            {
                return await DenyProofAsync(cancellationToken, record.State);
            }

            record = await _stateStore.AdvanceToProofValidatedAsync(
                user.Id,
                effectiveEmailOtpRequirement,
                cancellationToken);
            if (!await CanMigrateAsync(user.Id, cancellationToken))
            {
                return await DenyProofAsync(cancellationToken, record.State);
            }

            var continuation = await _continuations.CreateAsync(
                new MigrationContinuationRequest(user.Id, binding, request.Context),
                cancellationToken);
            await RecordAsync(SanitizedMigrationAuditCategory.ProofValidated, record.State, cancellationToken);
            return new MigrationProofCeremonyResult(
                MigrationCeremonyOutcome.ContinuationIssued,
                requiresEmailOtp,
                continuation.ProtectedValue);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return await DenyProofAsync(cancellationToken, record?.State);
        }
    }

    public async Task<MigrationCommitCeremonyResult> CommitAsync(
        MigrationCommitCeremonyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Continuation) || string.IsNullOrEmpty(request.NewPassword))
        {
            return await DenyCommitAsync(SanitizedMigrationAuditCategory.ContinuationDenied, cancellationToken);
        }

        var directoryDestinationEnabled =
            _directoryOptions.Enabled && _directoryOptions.AuthenticationEnabled;
        var legacyDestinationEnabled = _legacyOptions.Enabled &&
            _legacyOptions.IsCohortEnabled(LegacyPasswordSyncCohort.Stage2Migration);
        if (!directoryDestinationEnabled && !legacyDestinationEnabled)
        {
            return await DenyCommitAsync(SanitizedMigrationAuditCategory.PolicyDenied, cancellationToken);
        }

        var pending = await _continuations.InspectAsync(request.Continuation, request.Context, cancellationToken);
        if (pending.Outcome != MigrationContinuationConsumptionOutcome.Consumed || pending.Record is null ||
            pending.Record.State != CredentialMigrationState.ProofValidated ||
            !CredentialMigrationStateRecord.IsPersistableRequirement(pending.Record.EffectiveEmailOtpRequirement) ||
            !await CanMigrateAsync(pending.Record.LocalAccountId, cancellationToken))
        {
            return await DenyCommitAsync(SanitizedMigrationAuditCategory.ContinuationDenied, cancellationToken, pending.Record?.State);
        }

        if (RequiresEmailOtp(pending.Record.EffectiveEmailOtpRequirement))
        {
            RecoveryProofOutcome proofOutcome;
            if (!string.IsNullOrWhiteSpace(request.MigrationOtpProof))
            {
                proofOutcome = await _migrationOtpProofService.ConsumeAsync(
                    new MigrationOtpConsumptionRequest(
                        request.Continuation,
                        request.Context,
                        request.MigrationOtpProof),
                    cancellationToken);
            }
            else
            {
                proofOutcome = await _recoveryAssistanceService.ConsumeResetApprovalAsync(
                    new ResetApprovalConsumptionRequest(request.Continuation, request.Context),
                    cancellationToken);
            }

            if (proofOutcome != RecoveryProofOutcome.Success)
            {
                return await DenyCommitAsync(
                    SanitizedMigrationAuditCategory.ContinuationDenied,
                    cancellationToken,
                    pending.Record.State);
            }
        }

        var consumed = await _continuations.ConsumeAsync(request.Continuation, request.Context, cancellationToken);
        if (consumed.Outcome != MigrationContinuationConsumptionOutcome.Consumed || consumed.Record is null ||
            consumed.Record.State != CredentialMigrationState.ProofValidated ||
            !CredentialMigrationStateRecord.IsPersistableRequirement(consumed.Record.EffectiveEmailOtpRequirement) ||
            consumed.Record.EffectiveEmailOtpRequirement != pending.Record.EffectiveEmailOtpRequirement)
        {
            return await DenyCommitAsync(SanitizedMigrationAuditCategory.ContinuationDenied, cancellationToken, consumed.Record?.State);
        }

        var floor = _policy.Evaluate(
            new MigrationPolicyRequest(consumed.Record.LocalAccountId, false, EmailOtpPolicy.Disabled),
            _timeProvider.GetUtcNow());
        if (!floor.AllowLegacyProof ||
            (floor.EffectiveEmailOtpPolicy == EmailOtpPolicy.Required &&
             !RequiresEmailOtp(consumed.Record.EffectiveEmailOtpRequirement)))
        {
            return await DenyCommitAsync(SanitizedMigrationAuditCategory.PolicyDenied, cancellationToken, consumed.Record.State);
        }

        // Recheck after the atomic consume so lifecycle changes cannot reach the writable directory operation.
        if (!await CanMigrateAsync(consumed.Record.LocalAccountId, cancellationToken))
        {
            return await DenyCommitAsync(SanitizedMigrationAuditCategory.PolicyDenied, cancellationToken, consumed.Record.State);
        }

        if (!directoryDestinationEnabled)
        {
            if (consumed.SourceAttemptId is not { } sourceAttemptId ||
                consumed.SourceAttemptVersion is not { } sourceAttemptVersion)
            {
                return await UnavailableCommitAsync(cancellationToken, consumed.Record.State);
            }

            try
            {
                var sourceIdentity = await (
                    from state in _dbContext.CredentialMigrationStateRecords.AsNoTracking()
                    join user in _dbContext.Users.AsNoTracking() on state.LocalAccountId equals user.Id
                    where state.LocalAccountId == consumed.Record.LocalAccountId &&
                          state.State == CredentialMigrationState.ProofValidated
                    select new
                    {
                        state.ProviderSubjectDirectoryBindingId,
                        user.ConcurrencyStamp,
                        user.SecurityStamp
                    })
                    .SingleAsync(cancellationToken);
                var syncResult = await _passwordSyncCoordinator.SynchronizeAsync(
                    new LegacyPasswordSyncSourceReference(
                        LegacyPasswordSyncSourceKind.Stage2Migration,
                        sourceAttemptId,
                        sourceAttemptVersion),
                    LegacyPasswordSyncCohort.Stage2Migration,
                    consumed.Record.LocalAccountId,
                    sourceIdentity.ProviderSubjectDirectoryBindingId,
                    sourceIdentity.ConcurrencyStamp!,
                    sourceIdentity.SecurityStamp!,
                    request.NewPassword,
                    cancellationToken);
                return syncResult.Outcome == LegacyPasswordSyncResultOutcome.Succeeded
                    ? new MigrationCommitCeremonyResult(MigrationCeremonyOutcome.Completed)
                    : await UnavailableCommitAsync(cancellationToken, consumed.Record.State);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return await UnavailableCommitAsync(cancellationToken, consumed.Record.State);
            }
        }

        DirectoryCredentialOperationResult reset;
        try
        {
            // Deliberately one write attempt. A success response is not treated as commitment.
            reset = await _directoryResetter.ResetCredentialAsync(
                consumed.Record.Binding.DirectoryObjectId,
                request.NewPassword,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return await UnavailableCommitAsync(cancellationToken, consumed.Record.State);
        }

        if (reset.Outcome != DirectoryCredentialOperationOutcome.Succeeded)
        {
            return reset.Outcome is DirectoryCredentialOperationOutcome.Unavailable or DirectoryCredentialOperationOutcome.Timeout
                ? await UnavailableCommitAsync(cancellationToken, consumed.Record.State)
                : await DenyCommitAsync(SanitizedMigrationAuditCategory.DirectoryDenied, cancellationToken, consumed.Record.State);
        }

        DirectoryCredentialVerificationResult verification;
        try
        {
            // The verifier opens an independent new-password bind and then reads the object state.
            verification = await _directoryVerifier.VerifyCredentialAsync(
                consumed.Record.Binding.DirectoryObjectId,
                request.NewPassword,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return await UnavailableCommitAsync(cancellationToken, consumed.Record.State);
        }

        if (!IsIndependentVerificationValid(verification, consumed.Record.Binding.DirectoryObjectId))
        {
            return verification.Outcome is DirectoryCredentialOutcome.Unavailable or DirectoryCredentialOutcome.Timeout
                ? await UnavailableCommitAsync(cancellationToken, consumed.Record.State)
                : await DenyCommitAsync(SanitizedMigrationAuditCategory.DirectoryDenied, cancellationToken, consumed.Record.State);
        }

        LegacyPasswordSyncSourceReference? syncSource = null;
        var syncBindingId = Guid.Empty;
        string? syncConcurrencyStamp = null;
        string? syncSecurityStamp = null;
        try
        {
            var committed = await _stateStore.AdvanceAsync(
                consumed.Record.LocalAccountId,
                CredentialMigrationState.ProofValidated,
                CredentialMigrationState.DirectoryCredentialCommitted,
                cancellationToken);
            await RecordAsync(SanitizedMigrationAuditCategory.DirectoryCredentialCommitted, committed.State, cancellationToken);
            var refreshed = await _bindingRefresh.BindAndRefreshAsync(
                new Stage1BindingRefreshRequest(
                    committed.LocalAccountId,
                    committed.Binding.ProviderNamespace,
                    committed.Binding.StableSubject,
                    verification.Identity!),
                cancellationToken);
            if (refreshed is Stage1BindingRefreshOutcome.Conflict or Stage1BindingRefreshOutcome.Invalid)
            {
                return await DenyCommitAsync(SanitizedMigrationAuditCategory.DirectoryDenied, cancellationToken, committed.State);
            }

            var finalizationSource = await _dbContext.CredentialMigrationStateRecords
                .AsNoTracking()
                .Where(record => record.LocalAccountId == committed.LocalAccountId &&
                    record.State == CredentialMigrationState.DirectoryCredentialCommitted)
                .Select(record => new
                {
                    record.Id,
                    record.ProviderSubjectDirectoryBindingId,
                    record.Version
                })
                .SingleAsync(cancellationToken);
            var accountStamps = await _dbContext.Users
                .AsNoTracking()
                .Where(user => user.Id == committed.LocalAccountId)
                .Select(user => new { user.ConcurrencyStamp, user.SecurityStamp })
                .SingleAsync(cancellationToken);
            var finalized = await _stateStore.AdvanceAsync(
                committed.LocalAccountId,
                CredentialMigrationState.DirectoryCredentialCommitted,
                CredentialMigrationState.LocalFinalized,
                cancellationToken);
            await RecordAsync(SanitizedMigrationAuditCategory.Completed, finalized.State, cancellationToken);
            syncSource = new LegacyPasswordSyncSourceReference(
                LegacyPasswordSyncSourceKind.Stage2Migration,
                finalizationSource.Id,
                finalizationSource.Version + 1);
            syncBindingId = finalizationSource.ProviderSubjectDirectoryBindingId;
            syncConcurrencyStamp = accountStamps.ConcurrencyStamp;
            syncSecurityStamp = accountStamps.SecurityStamp;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return await UnavailableCommitAsync(cancellationToken, CredentialMigrationState.DirectoryCredentialCommitted);
        }

        if (legacyDestinationEnabled)
        {
            try
            {
                await _passwordSyncCoordinator.SynchronizeAsync(
                    syncSource!,
                    LegacyPasswordSyncCohort.Stage2Migration,
                    consumed.Record.LocalAccountId,
                    syncBindingId,
                    syncConcurrencyStamp!,
                    syncSecurityStamp!,
                    request.NewPassword,
                    cancellationToken);
            }
            catch
            {
                // LocalFinalized and its audit remain authoritative.
            }
        }

        return new MigrationCommitCeremonyResult(MigrationCeremonyOutcome.Completed);
    }

    public async Task<DirectoryCredentialResult> AuthenticateCompletedAsync(
        Guid localAccountId,
        string password,
        CancellationToken cancellationToken = default)
    {
        var record = await _stateStore.FindAsync(localAccountId, cancellationToken);
        if (record?.State != CredentialMigrationState.LocalFinalized || string.IsNullOrEmpty(password))
        {
            return new DirectoryCredentialResult(DirectoryCredentialOutcome.InvalidCredentials);
        }

        try
        {
            var result = await _directoryAuthenticator.AuthenticateAsync(
                record.Binding.DirectoryObjectId,
                password,
                cancellationToken);
            if (result.Outcome != DirectoryCredentialOutcome.Authenticated)
            {
                await RecordAsync(SanitizedMigrationAuditCategory.DirectoryDenied, record.State, cancellationToken);
                return result;
            }

            if (result.Identity is null || !IsManagedIdentityValid(result.Identity, record.Binding.DirectoryObjectId))
            {
                await RecordAsync(SanitizedMigrationAuditCategory.DirectoryDenied, record.State, cancellationToken);
                return new DirectoryCredentialResult(DirectoryCredentialOutcome.Malformed);
            }

            try
            {
                // The binding is already durable and exact. Refresh failures remain best effort.
                await _bindingRefresh.BindAndRefreshAsync(
                    new Stage1BindingRefreshRequest(
                        record.LocalAccountId,
                        record.Binding.ProviderNamespace,
                        record.Binding.StableSubject,
                        result.Identity),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Directory authentication remains authoritative; no profile failure changes its result.
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await RecordAsync(SanitizedMigrationAuditCategory.DirectoryUnavailable, record.State, cancellationToken);
            return new DirectoryCredentialResult(DirectoryCredentialOutcome.Unavailable);
        }
    }

    private async Task<MigrationProofCeremonyResult> DenyProofAsync(
        CancellationToken cancellationToken,
        CredentialMigrationState? state = null)
    {
        await RecordAsync(SanitizedMigrationAuditCategory.ProofDenied, state, cancellationToken);
        return new MigrationProofCeremonyResult(MigrationCeremonyOutcome.Denied);
    }

    private async Task<MigrationProofCeremonyResult> DenyDirectoryAsync(
        CancellationToken cancellationToken,
        CredentialMigrationState? state = null)
    {
        await RecordAsync(SanitizedMigrationAuditCategory.DirectoryDenied, state, cancellationToken);
        return new MigrationProofCeremonyResult(MigrationCeremonyOutcome.Denied);
    }

    private async Task<MigrationProofCeremonyResult> UnavailableAsync(
        SanitizedMigrationAuditCategory category,
        CancellationToken cancellationToken,
        CredentialMigrationState? state = null)
    {
        await RecordAsync(category, state, cancellationToken);
        return new MigrationProofCeremonyResult(MigrationCeremonyOutcome.Unavailable);
    }

    private async Task<MigrationCommitCeremonyResult> DenyCommitAsync(
        SanitizedMigrationAuditCategory category,
        CancellationToken cancellationToken,
        CredentialMigrationState? state = null)
    {
        await RecordAsync(category, state, cancellationToken);
        return new MigrationCommitCeremonyResult(MigrationCeremonyOutcome.Denied);
    }

    private async Task<MigrationCommitCeremonyResult> UnavailableCommitAsync(
        CancellationToken cancellationToken,
        CredentialMigrationState? state = null)
    {
        await RecordAsync(SanitizedMigrationAuditCategory.DirectoryUnavailable, state, cancellationToken);
        return new MigrationCommitCeremonyResult(MigrationCeremonyOutcome.Unavailable);
    }

    private Task RecordAsync(
        SanitizedMigrationAuditCategory category,
        CredentialMigrationState? state,
        CancellationToken cancellationToken) =>
        _audit.RecordAsync(new SanitizedMigrationAuditEvent(Guid.NewGuid(), category, state), cancellationToken);

    private static bool Matches(DirectoryObjectBinding left, DirectoryObjectBinding right) =>
        left.ProviderNamespace == right.ProviderNamespace &&
        left.StableSubject == right.StableSubject &&
        left.DirectoryObjectId == right.DirectoryObjectId;

    private static bool IsIndependentVerificationValid(
        DirectoryCredentialVerificationResult verification,
        Guid expectedObjectId) =>
        verification.Outcome == DirectoryCredentialOutcome.Authenticated &&
        verification.Identity is
        {
            ObjectId: var objectId,
            IsEligible: true,
            IsEnabled: true,
            IsLocked: false
        } && objectId == expectedObjectId;

    private static bool IsManagedIdentityValid(ManagedDirectoryIdentity identity, Guid expectedObjectId) =>
        identity.ObjectId == expectedObjectId &&
        identity.IsEligible &&
        identity.IsEnabled &&
        !identity.IsLocked;

    private async Task<ApplicationUser?> ResolveLocalAccountAsync(
        ProofResult proof,
        CancellationToken cancellationToken)
    {
        var providerUser = await _userManager.FindByLoginAsync(proof.ProviderNamespace!, proof.StableSubject!);
        var normalizedName = _userManager.NormalizeName(proof.CanonicalAccount!) ?? proof.CanonicalAccount!;
        var normalizedEmail = _userManager.NormalizeEmail(proof.CanonicalAccount!) ?? proof.CanonicalAccount!;
        var canonicalMatches = await _dbContext.Users
            .Where(candidate =>
                candidate.NormalizedUserName == normalizedName ||
                candidate.NormalizedEmail == normalizedEmail)
            .ToListAsync(cancellationToken);
        var distinctMatches = canonicalMatches
            .GroupBy(candidate => candidate.Id)
            .Select(group => group.First())
            .ToArray();

        if (providerUser is not null)
        {
            return distinctMatches.All(candidate => candidate.Id == providerUser.Id)
                ? providerUser
                : null;
        }

        return distinctMatches.Length == 1 ? distinctMatches[0] : null;
    }

    private async Task<bool> IsCompletedCandidateAsync(string accountName, CancellationToken cancellationToken)
    {
        var candidate = await _userManager.FindByEmailAsync(accountName) ??
            await _userManager.FindByNameAsync(accountName);
        if (candidate is null)
        {
            var normalizedAlias = ProviderSubjectDirectoryBinding.NormalizeCanonicalAccountAlias(accountName);
            if (normalizedAlias is null)
            {
                return false;
            }

            var bindings = await _dbContext.ProviderSubjectDirectoryBindings
                .AsNoTracking()
                .Where(binding => binding.NormalizedCanonicalAccountAlias == normalizedAlias)
                .ToListAsync(cancellationToken);
            if (bindings.Count == 0)
            {
                return false;
            }

            if (bindings.Count != 1)
            {
                return true;
            }

            candidate = await _dbContext.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(user => user.Id == bindings[0].LocalAccountId, cancellationToken);
            if (candidate is null)
            {
                return true;
            }
        }

        return (await _stateStore.FindAsync(candidate.Id, cancellationToken))?.State == CredentialMigrationState.LocalFinalized;
    }

    private async Task<bool> CanMigrateAsync(Guid localAccountId, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(localAccountId.ToString());
        return user is not null && await CanMigrateAsync(user, cancellationToken);
    }

    private async Task<bool> CanMigrateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        if (!user.IsActive || user.IsDeleted || await _userManager.IsLockedOutAsync(user))
        {
            return false;
        }

        if (user.PersonId is not { } personId)
        {
            return true;
        }

        var person = await _dbContext.Persons
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == personId, cancellationToken);
        return person?.CanAuthenticate() == true;
    }

    private async Task<bool> HasConsumedOrActiveContinuationAsync(
        Guid localAccountId,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var stateId = await _dbContext.CredentialMigrationStateRecords
            .AsNoTracking()
            .Where(record => record.LocalAccountId == localAccountId)
            .Select(record => (Guid?)record.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return stateId is not null && await _dbContext.CredentialMigrationContinuations
            .AsNoTracking()
            .AnyAsync(continuation =>
                continuation.CredentialMigrationStateRecordId == stateId &&
                (continuation.ConsumedAtUtc != null || continuation.ExpiresAtUtc > now),
                cancellationToken);
    }

    private static EffectiveEmailOtpRequirement ToEffectiveEmailOtpRequirement(
        MigrationPolicyDecision policy,
        IReadOnlyCollection<ProofRequiredAction> requiredActions) =>
        policy.EffectiveEmailOtpPolicy switch
        {
            EmailOtpPolicy.Disabled => EffectiveEmailOtpRequirement.NotRequired,
            EmailOtpPolicy.ProviderRequested => requiredActions.Contains(ProofRequiredAction.EmailOtp)
                ? EffectiveEmailOtpRequirement.Required
                : EffectiveEmailOtpRequirement.NotRequired,
            EmailOtpPolicy.Required => EffectiveEmailOtpRequirement.Required,
            _ => EffectiveEmailOtpRequirement.Unspecified
        };

    private static bool RequiresEmailOtp(EffectiveEmailOtpRequirement requirement) =>
        requirement == EffectiveEmailOtpRequirement.Required;
}
