using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace Infrastructure.Services;

public sealed class NativePasswordRecoveryResetService :
    INativePasswordRecoveryResetService,
    INativeDirectoryRecoveryReconciliationService
{
    private const string RevocationReason = "native-password-recovery";

    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IOpenIddictAuthorizationManager _authorizationManager;
    private readonly IOpenIddictTokenManager _tokenManager;
    private readonly IRecoveryVerificationPolicyEvaluator _policyEvaluator;
    private readonly IForgotPasswordRoutingEvaluator _routingEvaluator;
    private readonly IDirectoryCredentialResetter _directoryCredentialResetter;
    private readonly IDirectoryCredentialVerifier _directoryCredentialVerifier;
    private readonly IRecoveryProofAuthorizer _recoveryAuthorizer;
    private readonly ILegacyPasswordSyncCoordinator _passwordSyncCoordinator;
    private readonly ForgotPasswordRecoveryOptions _options;
    private readonly DirectoryIntegrationOptions _directoryOptions;
    private readonly LegacyPasswordSyncOptions _legacyOptions;
    private readonly TimeProvider _timeProvider;

    public NativePasswordRecoveryResetService(
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IOpenIddictAuthorizationManager authorizationManager,
        IOpenIddictTokenManager tokenManager,
        IRecoveryVerificationPolicyEvaluator policyEvaluator,
        IForgotPasswordRoutingEvaluator routingEvaluator,
        IDirectoryCredentialResetter directoryCredentialResetter,
        IDirectoryCredentialVerifier directoryCredentialVerifier,
        IRecoveryProofAuthorizer recoveryAuthorizer,
        ILegacyPasswordSyncCoordinator passwordSyncCoordinator,
        IOptions<ForgotPasswordRecoveryOptions> options,
        IOptions<DirectoryIntegrationOptions> directoryOptions,
        IOptions<LegacyPasswordSyncOptions> legacyOptions,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _authorizationManager = authorizationManager;
        _tokenManager = tokenManager;
        _policyEvaluator = policyEvaluator;
        _routingEvaluator = routingEvaluator;
        _directoryCredentialResetter = directoryCredentialResetter;
        _directoryCredentialVerifier = directoryCredentialVerifier;
        _recoveryAuthorizer = recoveryAuthorizer;
        _passwordSyncCoordinator = passwordSyncCoordinator;
        _options = options.Value;
        _directoryOptions = directoryOptions.Value;
        _legacyOptions = legacyOptions.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<NativeDirectoryRecoveryReconciliationOutcome> ReconcileAsync(
        Guid actorAccountId,
        Guid localAccountId,
        string intendedPassword,
        CancellationToken cancellationToken = default)
        => Task.FromResult(NativeDirectoryRecoveryReconciliationOutcome.Unresolved);

    public async Task<NativeRecoveryResetResult> ResetAsync(
        NativeRecoveryResetRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsNativeRecoveryPermitted() ||
            (request.UseAdministrativeApproval && !_options.OrdinaryRecoveryAssistanceEnabled) ||
            request.RequestId == Guid.Empty ||
            (!request.UseAdministrativeApproval && string.IsNullOrWhiteSpace(request.Proof)) ||
            string.IsNullOrWhiteSpace(request.NewPassword) ||
            !IsValidContext(request.Context))
        {
            return Denied();
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var reservationCommitted = false;
        try
        {
            var runtimeMode = await _dbContext.SecurityPolicies
                .OrderBy(policy => policy.Id)
                .Select(policy => policy.ForgotPasswordMode)
                .FirstOrDefaultAsync(cancellationToken);
            if (_routingEvaluator.Evaluate(runtimeMode, null).PermittedMode != ForgotPasswordMode.Native)
            {
                await transaction.RollbackAsync(cancellationToken);
                return DeniedAfterRollback();
            }

            var challenge = await _dbContext.RecoveryProofChallenges
                .SingleOrDefaultAsync(candidate =>
                    candidate.Id == request.RequestId &&
                    candidate.Purpose == RecoveryProofPurpose.NativePasswordRecovery,
                    cancellationToken);
            if (challenge is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return DeniedAfterRollback();
            }

            var user = await _dbContext.Users
                .SingleOrDefaultAsync(candidate => candidate.Id == challenge.LocalAccountId, cancellationToken);
            var recoveryEmail = await _dbContext.RecoveryEmails
                .SingleOrDefaultAsync(candidate =>
                    candidate.Id == challenge.RecoveryEmailId &&
                    candidate.LocalAccountId == challenge.LocalAccountId,
                    cancellationToken);
            var authority = user is null
                ? null
                : await ResolveAuthorityAsync(user, cancellationToken);
            if (user is null || recoveryEmail?.VerifiedAtUtc is null || authority is null ||
                !HasCurrentNativeBinding(challenge, recoveryEmail, user, authority, request.Context) ||
                !request.UseAdministrativeApproval && !IsValidProof(challenge, recoveryEmail, request))
            {
                await transaction.RollbackAsync(cancellationToken);
                return DeniedAfterRollback();
            }

            var directoryDestinationEnabled = authority.IsDirectory &&
                _directoryOptions.Enabled && _directoryOptions.AuthenticationEnabled;
            var legacyDestinationEnabled = authority.IsDirectory &&
                _legacyOptions.Enabled &&
                _legacyOptions.IsCohortEnabled(LegacyPasswordSyncCohort.CompletedDirectoryRecovery);
            if (authority.IsDirectory && !directoryDestinationEnabled && !legacyDestinationEnabled)
            {
                await transaction.RollbackAsync(cancellationToken);
                return DeniedAfterRollback();
            }

            var decision = await _policyEvaluator.EvaluateAsync(user.Id, cancellationToken);
            if (!IsVerifiedLocalDestination(decision) ||
                !string.Equals(recoveryEmail.Address, decision.RecoveryEmail.Address, StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync(cancellationToken);
                return DeniedAfterRollback();
            }

            if (authority.IsDirectory)
            {
                var passwordValidation = await ValidatePasswordAsync(user, request.NewPassword);
                if (!passwordValidation.Succeeded)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _dbContext.ChangeTracker.Clear();
                    return new NativeRecoveryResetResult(
                        NativeRecoveryResetOutcome.PasswordRejected,
                        passwordValidation.Errors.Select(error => error.Code).ToArray());
                }
            }

            NativeRecoveryResetApproval? approval = null;
            if (request.UseAdministrativeApproval)
            {
                var approvals = await _dbContext.NativeRecoveryResetApprovals
                    .Where(candidate => candidate.RecoveryProofChallengeId == challenge.Id &&
                        candidate.RevokedAtUtc == null && candidate.ConsumedAtUtc == null)
                    .Take(2)
                    .ToListAsync(cancellationToken);
                if (approvals.Count != 1 || !IsValidApproval(approvals[0], challenge, user, recoveryEmail, authority, request.Context))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return DeniedAfterRollback();
                }
                approval = approvals[0];
            }

            var now = _timeProvider.GetUtcNow();
            if (approval is not null && (!approval.TryConsume(now) ||
                    !challenge.TryConsumeWithAdministrativeApproval(now)) ||
                approval is null && !challenge.TryConsume(now))
            {
                await transaction.RollbackAsync(cancellationToken);
                return DeniedAfterRollback();
            }

            var proofVerifiedAt = challenge.VerifiedAtUtc!.Value;
            if (approval is null && recoveryEmail.VerifiedAtUtc!.Value < proofVerifiedAt)
            {
                recoveryEmail.MarkVerified(proofVerifiedAt);
            }

            if (authority.IsDirectory)
            {
                if (directoryDestinationEnabled)
                {
                    if (await _dbContext.NativeDirectoryRecoveryAttempts.AnyAsync(
                            attempt => attempt.LocalAccountId == user.Id &&
                                (attempt.Status == NativeDirectoryRecoveryStatus.Reserved ||
                                 attempt.Status == NativeDirectoryRecoveryStatus.ReconciliationRequired),
                            cancellationToken))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return DeniedAfterRollback();
                    }

                    _dbContext.NativeDirectoryRecoveryAttempts.Add(new NativeDirectoryRecoveryAttempt(
                        challenge.Id,
                        user.Id,
                        authority.DirectoryObjectId,
                        _timeProvider.GetUtcNow()));
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    reservationCommitted = true;
                    _dbContext.ChangeTracker.Clear();
                    return await CompleteDirectoryResetAsync(
                        challenge.Id,
                        user.Id,
                        authority.DirectoryObjectId,
                        request.NewPassword,
                        cancellationToken);
                }

                var bindingId = await _dbContext.ProviderSubjectDirectoryBindings
                    .Where(binding => binding.LocalAccountId == user.Id &&
                        binding.DirectoryObjectId == authority.DirectoryObjectId)
                    .Select(binding => binding.Id)
                    .SingleAsync(cancellationToken);
                var concurrencyStamp = user.ConcurrencyStamp;
                var securityStamp = user.SecurityStamp;
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                reservationCommitted = true;
                _dbContext.ChangeTracker.Clear();
                return await CompleteLegacyOnlyRecoveryAsync(
                    challenge.Id,
                    challenge.Version,
                    user.Id,
                    authority.DirectoryObjectId,
                    bindingId,
                    concurrencyStamp!,
                    securityStamp!,
                    request.NewPassword,
                    cancellationToken);
            }

            var previousHash = user.PasswordHash!;
            var previousSecurityStamp = user.SecurityStamp;
            var previousChangeDate = user.LastPasswordChangeDate;
            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);

            // Recovery resets retain strength/history checks but are exempt from old expiry and minimum-age checks.
            user.LastPasswordChangeDate = null;
            var passwordResult = await _userManager.ResetPasswordAsync(user, resetToken, request.NewPassword);
            if (!passwordResult.Succeeded)
            {
                user.LastPasswordChangeDate = previousChangeDate;
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();
                return new NativeRecoveryResetResult(
                    NativeRecoveryResetOutcome.PasswordRejected,
                    passwordResult.Errors.Select(error => error.Code).ToArray());
            }

            if (string.IsNullOrWhiteSpace(user.PasswordHash) ||
                string.Equals(user.PasswordHash, previousHash, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(user.SecurityStamp) ||
                string.Equals(user.SecurityStamp, previousSecurityStamp, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The local password reset did not update credential security state.");
            }

            user.PasswordHistory = AddPasswordHistory(user.PasswordHistory, previousHash);
            user.LastPasswordChangeDate = _timeProvider.GetUtcNow().UtcDateTime;

            await RevokeSessionsAndTokensAsync(user, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new NativeRecoveryResetResult(NativeRecoveryResetOutcome.Succeeded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!reservationCommitted)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            _dbContext.ChangeTracker.Clear();
            throw;
        }
        catch
        {
            if (!reservationCommitted)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            _dbContext.ChangeTracker.Clear();
            return Denied();
        }
    }

    private async Task<RecoveryAuthority?> ResolveAuthorityAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (!user.IsActive || user.IsDeleted ||
            user.LockoutEnabled && user.LockoutEnd is { } lockoutEnd && lockoutEnd > now)
        {
            return null;
        }

        if (await _dbContext.NativeDirectoryRecoveryAttempts.AsNoTracking().AnyAsync(
                attempt => attempt.LocalAccountId == user.Id &&
                    (attempt.Status == NativeDirectoryRecoveryStatus.Reserved ||
                     attempt.Status == NativeDirectoryRecoveryStatus.ReconciliationRequired),
                cancellationToken))
        {
            return null;
        }

        var binding = await _dbContext.ProviderSubjectDirectoryBindings.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.LocalAccountId == user.Id, cancellationToken);
        var migration = await _dbContext.CredentialMigrationStateRecords.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.LocalAccountId == user.Id, cancellationToken);
        RecoveryAuthority? authority = null;
        if (binding is null && migration is null && !string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            authority = new RecoveryAuthority(false, Guid.Empty);
        }
        else if (_options.NativeDirectoryRecoveryEnabled && binding is not null && migration is not null &&
                 migration.State == CredentialMigrationState.LocalFinalized &&
                 migration.ProviderSubjectDirectoryBindingId == binding.Id &&
                 binding.LocalAccountId == user.Id)
        {
            authority = new RecoveryAuthority(true, binding.DirectoryObjectId);
        }

        if (authority is null)
        {
            return null;
        }

        if (user.PersonId is not { } personId)
        {
            return authority;
        }

        var person = await _dbContext.Persons.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == personId, cancellationToken);
        return person?.CanAuthenticate() == true ? authority : null;
    }

    private async Task<IdentityResult> ValidatePasswordAsync(ApplicationUser user, string password)
    {
        var errors = new List<IdentityError>();
        var previousChangeDate = user.LastPasswordChangeDate;
        user.LastPasswordChangeDate = null;
        try
        {
            foreach (var validator in _userManager.PasswordValidators)
            {
                var result = await validator.ValidateAsync(_userManager, user, password);
                if (!result.Succeeded)
                {
                    errors.AddRange(result.Errors);
                }
            }
        }
        finally
        {
            user.LastPasswordChangeDate = previousChangeDate;
        }

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed(errors.ToArray());
    }

    private async Task<NativeRecoveryResetResult> CompleteDirectoryResetAsync(
        Guid challengeId,
        Guid localAccountId,
        Guid directoryObjectId,
        string newPassword,
        CancellationToken cancellationToken)
    {
        DirectoryCredentialOperationResult operation;
        try
        {
            operation = await _directoryCredentialResetter.ResetCredentialAsync(
                directoryObjectId,
                newPassword,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CompleteAttemptAsync(
                challengeId,
                localAccountId,
                directoryObjectId,
                NativeDirectoryRecoveryStatus.ReconciliationRequired,
                CancellationToken.None);
            throw;
        }
        catch
        {
            await CompleteAttemptAsync(
                challengeId,
                localAccountId,
                directoryObjectId,
                NativeDirectoryRecoveryStatus.ReconciliationRequired,
                CancellationToken.None);
            return Denied();
        }

        if (operation.Outcome is DirectoryCredentialOperationOutcome.Unavailable or
            DirectoryCredentialOperationOutcome.Timeout)
        {
            await CompleteAttemptAsync(
                challengeId,
                localAccountId,
                directoryObjectId,
                NativeDirectoryRecoveryStatus.ReconciliationRequired,
                cancellationToken);
            return Denied();
        }

        if (operation.Outcome != DirectoryCredentialOperationOutcome.Succeeded)
        {
            await CompleteAttemptAsync(
                challengeId,
                localAccountId,
                directoryObjectId,
                NativeDirectoryRecoveryStatus.Denied,
                cancellationToken);
            return Denied();
        }

        return await FinalizeSuccessfulDirectoryRecoveryAsync(
            challengeId,
            localAccountId,
            directoryObjectId,
            newPassword,
            cancellationToken);
    }

    private async Task<NativeRecoveryResetResult> FinalizeSuccessfulDirectoryRecoveryAsync(
        Guid challengeId,
        Guid localAccountId,
        Guid directoryObjectId,
        string newPassword,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        LegacyPasswordSyncSourceReference? source = null;
        var bindingId = Guid.Empty;
        string? concurrencyStamp = null;
        string? securityStamp = null;
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var attempt = await FindActiveAttemptAsync(challengeId, localAccountId, directoryObjectId, cancellationToken);
                var user = await _dbContext.Users.SingleAsync(candidate => candidate.Id == localAccountId, cancellationToken);
                bindingId = await _dbContext.ProviderSubjectDirectoryBindings
                    .Where(binding => binding.LocalAccountId == localAccountId &&
                        binding.DirectoryObjectId == directoryObjectId)
                    .Select(binding => binding.Id)
                    .SingleAsync(cancellationToken);
                var securityStampResult = await _userManager.UpdateSecurityStampAsync(user);
                if (!securityStampResult.Succeeded)
                {
                    throw new InvalidOperationException("Directory recovery could not rotate local security state.");
                }

                await RevokeSessionsAndTokensAsync(user, cancellationToken);
                attempt.Complete(NativeDirectoryRecoveryStatus.Succeeded, _timeProvider.GetUtcNow());
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                source = new LegacyPasswordSyncSourceReference(
                    LegacyPasswordSyncSourceKind.NativeRecovery,
                    attempt.Id,
                    attempt.Version);
                concurrencyStamp = user.ConcurrencyStamp;
                securityStamp = user.SecurityStamp;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _dbContext.ChangeTracker.Clear();
                throw;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _dbContext.ChangeTracker.Clear();
                return Denied();
            }
        }

        if (_legacyOptions.Enabled &&
            _legacyOptions.IsCohortEnabled(LegacyPasswordSyncCohort.CompletedDirectoryRecovery))
        {
            try
            {
                await _passwordSyncCoordinator.SynchronizeAsync(
                    source!,
                    LegacyPasswordSyncCohort.CompletedDirectoryRecovery,
                    localAccountId,
                    bindingId,
                    concurrencyStamp!,
                    securityStamp!,
                    newPassword,
                    cancellationToken);
            }
            catch
            {
                // The completed directory recovery remains authoritative.
            }
        }

        return new NativeRecoveryResetResult(NativeRecoveryResetOutcome.Succeeded);
    }

    private async Task<NativeRecoveryResetResult> CompleteLegacyOnlyRecoveryAsync(
        Guid challengeId,
        long challengeVersion,
        Guid localAccountId,
        Guid directoryObjectId,
        Guid bindingId,
        string concurrencyStamp,
        string securityStamp,
        string newPassword,
        CancellationToken cancellationToken)
    {
        LegacyPasswordSyncResult syncResult;
        try
        {
            syncResult = await _passwordSyncCoordinator.SynchronizeAsync(
                new LegacyPasswordSyncSourceReference(
                    LegacyPasswordSyncSourceKind.NativeRecovery,
                    challengeId,
                    challengeVersion),
                LegacyPasswordSyncCohort.CompletedDirectoryRecovery,
                localAccountId,
                bindingId,
                concurrencyStamp,
                securityStamp,
                newPassword,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Denied();
        }

        if (syncResult.Outcome != LegacyPasswordSyncResultOutcome.Succeeded)
        {
            return Denied();
        }

        _dbContext.ChangeTracker.Clear();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var user = await _dbContext.Users.SingleAsync(
                candidate => candidate.Id == localAccountId,
                cancellationToken);
            var authority = await ResolveAuthorityAsync(user, cancellationToken);
            if (authority is null || !authority.IsDirectory ||
                authority.DirectoryObjectId != directoryObjectId ||
                !string.Equals(user.ConcurrencyStamp, concurrencyStamp, StringComparison.Ordinal) ||
                !string.Equals(user.SecurityStamp, securityStamp, StringComparison.Ordinal))
            {
                await transaction.RollbackAsync(cancellationToken);
                return DeniedAfterRollback();
            }

            var securityStampResult = await _userManager.UpdateSecurityStampAsync(user);
            if (!securityStampResult.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return DeniedAfterRollback();
            }

            await RevokeSessionsAndTokensAsync(user, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new NativeRecoveryResetResult(NativeRecoveryResetOutcome.Succeeded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _dbContext.ChangeTracker.Clear();
            return Denied();
        }
    }

    private async Task CompleteAttemptAsync(
        Guid challengeId,
        Guid localAccountId,
        Guid directoryObjectId,
        NativeDirectoryRecoveryStatus status,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var attempt = await FindActiveAttemptAsync(challengeId, localAccountId, directoryObjectId, cancellationToken);
        attempt.Complete(status, _timeProvider.GetUtcNow());
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private Task<NativeDirectoryRecoveryAttempt> FindActiveAttemptAsync(
        Guid challengeId,
        Guid localAccountId,
        Guid directoryObjectId,
        CancellationToken cancellationToken) =>
        _dbContext.NativeDirectoryRecoveryAttempts.SingleAsync(
            attempt => attempt.RecoveryProofChallengeId == challengeId &&
                attempt.LocalAccountId == localAccountId &&
                attempt.DirectoryObjectId == directoryObjectId &&
                (attempt.Status == NativeDirectoryRecoveryStatus.Reserved ||
                 attempt.Status == NativeDirectoryRecoveryStatus.ReconciliationRequired),
            cancellationToken);

    private async Task RevokeSessionsAndTokensAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var sessions = await _dbContext.UserSessions
            .Where(session => session.UserId == user.Id && session.RevokedUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.RevokedUtc = now;
            session.RevocationReason = RevocationReason;
        }

        var subject = user.Id.ToString();
        var authorizations = new List<object>();
        await foreach (var authorization in _authorizationManager.FindBySubjectAsync(subject, cancellationToken))
        {
            authorizations.Add(authorization);
        }

        foreach (var authorization in authorizations)
        {
            if (!await _authorizationManager.TryRevokeAsync(authorization, cancellationToken))
            {
                throw new InvalidOperationException("An OpenIddict authorization could not be revoked.");
            }
        }

        var tokens = new Dictionary<string, object>(StringComparer.Ordinal);
        await foreach (var token in _tokenManager.FindBySubjectAsync(subject, cancellationToken))
        {
            await AddTokenAsync(tokens, token, cancellationToken);
        }

        foreach (var authorization in authorizations)
        {
            var authorizationId = await _authorizationManager.GetIdAsync(authorization, cancellationToken);
            if (string.IsNullOrWhiteSpace(authorizationId))
            {
                throw new InvalidOperationException("An OpenIddict authorization has no identifier.");
            }

            await foreach (var token in _tokenManager.FindByAuthorizationIdAsync(authorizationId, cancellationToken))
            {
                await AddTokenAsync(tokens, token, cancellationToken);
            }
        }

        foreach (var token in tokens.Values)
        {
            if (!await _tokenManager.TryRevokeAsync(token, cancellationToken))
            {
                throw new InvalidOperationException("An OpenIddict token could not be revoked.");
            }
        }
    }

    private sealed record RecoveryAuthority(bool IsDirectory, Guid DirectoryObjectId);

    private bool IsValidProof(
        RecoveryProofChallenge challenge,
        RecoveryEmailRecord recoveryEmail,
        NativeRecoveryResetRequest request)
    {
        var now = _timeProvider.GetUtcNow();
        if (challenge.VerifiedAtUtc is null || challenge.ConsumedAtUtc is not null ||
            challenge.RevokedAtUtc is not null || challenge.ExpiresAtUtc <= now ||
            string.IsNullOrWhiteSpace(challenge.ProofTokenHash))
        {
            return false;
        }

        var emailBinding =
            $"{recoveryEmail.Id:N}:{recoveryEmail.LocalAccountId:N}:{recoveryEmail.Version}:{recoveryEmail.NormalizedAddress}";
        var proofBinding = RecoveryProofSecurity.BindToContext(
            string.Empty,
            request.Context.ContextHash,
            request.Context.CsrfHash,
            emailBinding);
        var expectedHash = RecoveryProofSecurity.Hash(
            RecoveryProofSecurity.BindToContext(request.Proof, string.Empty, string.Empty, proofBinding));
        return FixedTimeEquals(challenge.ProofTokenHash, expectedHash);
    }

    private static bool HasCurrentNativeBinding(
        RecoveryProofChallenge challenge,
        RecoveryEmailRecord recoveryEmail,
        ApplicationUser user,
        RecoveryAuthority authority,
        NativeRecoveryContext context) =>
        challenge.NativeDirectoryAuthority == authority.IsDirectory &&
        challenge.NativeDirectoryObjectId == (authority.IsDirectory ? authority.DirectoryObjectId : null) &&
        challenge.NativeRecoveryEmailVersion == recoveryEmail.Version &&
        string.Equals(challenge.NativeSecurityStamp, user.SecurityStamp, StringComparison.Ordinal) &&
        string.Equals(challenge.NativeContextHash, context.ContextHash, StringComparison.Ordinal) &&
        string.Equals(challenge.NativeCsrfHash, context.CsrfHash, StringComparison.Ordinal);

    private bool IsValidApproval(
        NativeRecoveryResetApproval approval,
        RecoveryProofChallenge challenge,
        ApplicationUser user,
        RecoveryEmailRecord recoveryEmail,
        RecoveryAuthority authority,
        NativeRecoveryContext context) =>
        approval.ExpiresAtUtc > _timeProvider.GetUtcNow() &&
        approval.LocalAccountId == user.Id &&
        approval.RecoveryEmailId == recoveryEmail.Id &&
        approval.RecoveryEmailVersion == recoveryEmail.Version &&
        approval.DirectoryAuthority == authority.IsDirectory &&
        approval.DirectoryObjectId == (authority.IsDirectory ? authority.DirectoryObjectId : null) &&
        string.Equals(approval.SecurityStamp, user.SecurityStamp, StringComparison.Ordinal) &&
        string.Equals(approval.ContextHash, context.ContextHash, StringComparison.Ordinal) &&
        string.Equals(approval.CsrfHash, context.CsrfHash, StringComparison.Ordinal) &&
        string.Equals(approval.ContextHash, challenge.NativeContextHash, StringComparison.Ordinal) &&
        string.Equals(approval.CsrfHash, challenge.NativeCsrfHash, StringComparison.Ordinal);

    private static string AddPasswordHistory(string serializedHistory, string previousHash)
    {
        List<string> history;
        try
        {
            history = JsonSerializer.Deserialize<List<string>>(serializedHistory) ?? [];
        }
        catch (JsonException)
        {
            history = [];
        }

        history.Insert(0, previousHash);
        return JsonSerializer.Serialize(history.Distinct(StringComparer.Ordinal).ToList());
    }

    private async Task AddTokenAsync(
        Dictionary<string, object> tokens,
        object token,
        CancellationToken cancellationToken)
    {
        var tokenId = await _tokenManager.GetIdAsync(token, cancellationToken);
        if (string.IsNullOrWhiteSpace(tokenId))
        {
            throw new InvalidOperationException("An OpenIddict token has no identifier.");
        }

        tokens.TryAdd(tokenId, token);
    }

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));

    private bool IsNativeRecoveryPermitted() =>
        _options.NativeRecoveryEnabled && _options.DeploymentCeiling == ForgotPasswordMode.Native;

    private static bool IsVerifiedLocalDestination(RecoveryVerificationPolicyDecision decision) =>
        decision.Enabled &&
        decision.RecoveryEmail.AddressSource == RecoveryEmailAddressSource.LocalRecord &&
        decision.RecoveryEmail.TrustOrigin == RecoveryEmailPolicyTrustOrigin.LocallyVerified &&
        decision.RecoveryEmail.CanReceiveRecoveryOtp &&
        !string.IsNullOrWhiteSpace(decision.RecoveryEmail.Address);

    private static bool IsValidContext(NativeRecoveryContext context) =>
        !string.IsNullOrWhiteSpace(context.ContextHash) && context.ContextHash.Length <= 256 &&
        !string.IsNullOrWhiteSpace(context.CsrfHash) && context.CsrfHash.Length <= 256;

    private static NativeRecoveryResetResult Denied() =>
        new(NativeRecoveryResetOutcome.Denied);

    private NativeRecoveryResetResult DeniedAfterRollback()
    {
        _dbContext.ChangeTracker.Clear();
        return Denied();
    }
}
