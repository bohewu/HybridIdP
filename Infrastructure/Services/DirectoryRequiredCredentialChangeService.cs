using Core.Application.Ports;
using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace Infrastructure.Services;

public sealed class DirectoryRequiredCredentialChangeService(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IDirectoryTemporaryCredentialCapability capability,
    ISecurityPolicyService securityPolicyService,
    IOpenIddictAuthorizationManager authorizationManager,
    IOpenIddictTokenManager tokenManager,
    ILegacyPasswordSyncCoordinator passwordSyncCoordinator,
    IOptions<DirectoryIntegrationOptions> directoryOptions,
    IOptions<LegacyPasswordSyncOptions> legacyOptions,
    TimeProvider timeProvider) : IDirectoryRequiredCredentialChangeService
{
    private const string RevocationReason = "directory-required-credential-change";
    private readonly DirectoryIntegrationOptions _directoryOptions = directoryOptions.Value;
    private readonly LegacyPasswordSyncOptions _legacyOptions = legacyOptions.Value;

    public async Task<RecoveryProofOutcome> ChangeAsync(
        DirectoryRequiredCredentialChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.LocalAccountId == Guid.Empty || request.DirectoryObjectId == Guid.Empty ||
            string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewPassword))
        {
            return RecoveryProofOutcome.Invalid;
        }

        var valid = await IsValidAuthorityAsync(request, cancellationToken);
        if (!valid) return RecoveryProofOutcome.Unavailable;

        var directoryDestinationEnabled =
            _directoryOptions.Enabled && _directoryOptions.TemporaryCredentialCapabilityEnabled;
        var legacyDestinationEnabled = _legacyOptions.Enabled &&
            _legacyOptions.IsCohortEnabled(LegacyPasswordSyncCohort.CompletedDirectoryRequiredChange);
        if (!directoryDestinationEnabled && !legacyDestinationEnabled)
        {
            return RecoveryProofOutcome.Invalid;
        }

        var attempt = new NativeDirectoryRecoveryAttempt(
            request.LocalAccountId, request.DirectoryObjectId,
            NativeDirectoryCredentialOperationKind.RequiredChange, timeProvider.GetUtcNow());
        try
        {
            await using var reservation = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            dbContext.NativeDirectoryRecoveryAttempts.Add(attempt);
            await dbContext.SaveChangesAsync(cancellationToken);
            await reservation.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            dbContext.ChangeTracker.Clear();
            throw;
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            return RecoveryProofOutcome.Unavailable;
        }

        if (directoryDestinationEnabled)
        {
            DirectoryCredentialOperationResult operation;
            try
            {
                operation = await capability.ChangeRequiredCredentialAsync(
                    request.DirectoryObjectId, request.CurrentPassword, request.NewPassword, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await CompleteAttemptAsync(attempt.Id, request, NativeDirectoryRecoveryStatus.ReconciliationRequired, CancellationToken.None);
                throw;
            }
            catch
            {
                await CompleteAttemptAsync(attempt.Id, request, NativeDirectoryRecoveryStatus.ReconciliationRequired, CancellationToken.None);
                return RecoveryProofOutcome.Unavailable;
            }

            if (operation.Outcome is DirectoryCredentialOperationOutcome.Unavailable or DirectoryCredentialOperationOutcome.Timeout)
            {
                await CompleteAttemptAsync(attempt.Id, request, NativeDirectoryRecoveryStatus.ReconciliationRequired, cancellationToken);
                return RecoveryProofOutcome.Unavailable;
            }
            if (operation.Outcome != DirectoryCredentialOperationOutcome.Succeeded)
            {
                await CompleteAttemptAsync(attempt.Id, request, NativeDirectoryRecoveryStatus.Denied, cancellationToken);
                await RecordFailureAsync(request.LocalAccountId);
                return RecoveryProofOutcome.Invalid;
            }
        }

        LegacyPasswordSyncSourceReference? syncSource = null;
        var syncBindingId = Guid.Empty;
        string? syncConcurrencyStamp = null;
        string? syncSecurityStamp = null;
        try
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var currentAttempt = await FindAttemptAsync(attempt.Id, request, cancellationToken);
            var user = await dbContext.Users.SingleAsync(candidate => candidate.Id == request.LocalAccountId, cancellationToken);
            if (!await IsValidAuthorityAsync(request, cancellationToken) || !user.RequiresPasswordChange)
                throw new InvalidOperationException("Directory credential authority changed before finalization.");
            syncBindingId = await dbContext.ProviderSubjectDirectoryBindings
                .Where(binding => binding.LocalAccountId == request.LocalAccountId &&
                    binding.DirectoryObjectId == request.DirectoryObjectId)
                .Select(binding => binding.Id)
                .SingleAsync(cancellationToken);

            if (directoryDestinationEnabled)
            {
                user.RequiresPasswordChange = false;
                var stamp = await userManager.UpdateSecurityStampAsync(user);
                if (!stamp.Succeeded) throw new InvalidOperationException("Local security state was not rotated.");
                await userManager.ResetAccessFailedCountAsync(user);
                await RevokeSessionsAndTokensAsync(user, cancellationToken);
                currentAttempt.Complete(NativeDirectoryRecoveryStatus.Succeeded, timeProvider.GetUtcNow());
            }
            else
            {
                currentAttempt.Complete(
                    NativeDirectoryRecoveryStatus.LegacyPasswordSyncAuthorized,
                    timeProvider.GetUtcNow());
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            syncSource = new LegacyPasswordSyncSourceReference(
                LegacyPasswordSyncSourceKind.RequiredChange,
                currentAttempt.Id,
                currentAttempt.Version);
            syncConcurrencyStamp = user.ConcurrencyStamp;
            syncSecurityStamp = user.SecurityStamp;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            dbContext.ChangeTracker.Clear();
            await CompleteAttemptAsync(attempt.Id, request, NativeDirectoryRecoveryStatus.ReconciliationRequired, CancellationToken.None);
            throw;
        }
        catch
        {
            dbContext.ChangeTracker.Clear();
            await CompleteAttemptAsync(attempt.Id, request, NativeDirectoryRecoveryStatus.ReconciliationRequired, CancellationToken.None);
            return RecoveryProofOutcome.Unavailable;
        }

        if (legacyDestinationEnabled)
        {
            try
            {
                var syncResult = await passwordSyncCoordinator.SynchronizeAsync(
                    syncSource!,
                    LegacyPasswordSyncCohort.CompletedDirectoryRequiredChange,
                    request.LocalAccountId,
                    syncBindingId,
                    syncConcurrencyStamp!,
                    syncSecurityStamp!,
                    request.NewPassword,
                    cancellationToken);
                if (!directoryDestinationEnabled &&
                    syncResult.Outcome != LegacyPasswordSyncResultOutcome.Succeeded)
                {
                    return RecoveryProofOutcome.Unavailable;
                }
            }
            catch
            {
                if (!directoryDestinationEnabled)
                {
                    return RecoveryProofOutcome.Unavailable;
                }

                // The completed required-change ceremony remains authoritative.
            }
        }

        if (!directoryDestinationEnabled &&
            !await FinalizeLegacyOnlyLocalStateAsync(request, cancellationToken))
        {
            return RecoveryProofOutcome.Unavailable;
        }

        return RecoveryProofOutcome.Success;
    }

    private async Task<bool> FinalizeLegacyOnlyLocalStateAsync(
        DirectoryRequiredCredentialChangeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var user = await dbContext.Users.SingleAsync(
                candidate => candidate.Id == request.LocalAccountId,
                cancellationToken);
            if (!await IsValidAuthorityAsync(request, cancellationToken) || !user.RequiresPasswordChange)
            {
                return false;
            }

            user.RequiresPasswordChange = false;
            var stamp = await userManager.UpdateSecurityStampAsync(user);
            if (!stamp.Succeeded)
            {
                return false;
            }

            await userManager.ResetAccessFailedCountAsync(user);
            await RevokeSessionsAndTokensAsync(user, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            dbContext.ChangeTracker.Clear();
            throw;
        }
        catch
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    private Task<bool> IsValidAuthorityAsync(DirectoryRequiredCredentialChangeRequest request, CancellationToken cancellationToken) =>
        (from state in dbContext.CredentialMigrationStateRecords.AsNoTracking()
         join binding in dbContext.ProviderSubjectDirectoryBindings.AsNoTracking()
             on state.ProviderSubjectDirectoryBindingId equals binding.Id
         join user in dbContext.Users.AsNoTracking()
             on state.LocalAccountId equals user.Id
         where state.LocalAccountId == request.LocalAccountId &&
               state.State == CredentialMigrationState.LocalFinalized &&
               binding.LocalAccountId == request.LocalAccountId &&
               user.IsActive && !user.IsDeleted && user.RequiresPasswordChange &&
               binding.DirectoryObjectId == request.DirectoryObjectId
         select state.Id).AnyAsync(cancellationToken);

    private async Task RecordFailureAsync(Guid localAccountId)
    {
        var user = await userManager.FindByIdAsync(localAccountId.ToString());
        if (user is null) return;
        var policy = await securityPolicyService.GetCurrentPolicyAsync();
        if (policy.MaxFailedAccessAttempts <= 0) return;
        var result = await userManager.AccessFailedAsync(user);
        if (result.Succeeded && await userManager.GetAccessFailedCountAsync(user) >= policy.MaxFailedAccessAttempts)
            await userManager.SetLockoutEndDateAsync(user, timeProvider.GetUtcNow().AddMinutes(policy.LockoutDurationMinutes));
    }

    private async Task CompleteAttemptAsync(Guid attemptId, DirectoryRequiredCredentialChangeRequest request,
        NativeDirectoryRecoveryStatus status, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var attempt = await FindAttemptAsync(attemptId, request, cancellationToken);
        attempt.Complete(status, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task<NativeDirectoryRecoveryAttempt> FindAttemptAsync(Guid attemptId,
        DirectoryRequiredCredentialChangeRequest request, CancellationToken cancellationToken) =>
        dbContext.NativeDirectoryRecoveryAttempts.SingleAsync(candidate =>
            candidate.Id == attemptId && candidate.LocalAccountId == request.LocalAccountId &&
            candidate.DirectoryObjectId == request.DirectoryObjectId &&
            candidate.OperationKind == NativeDirectoryCredentialOperationKind.RequiredChange &&
            (candidate.Status == NativeDirectoryRecoveryStatus.Reserved ||
             candidate.Status == NativeDirectoryRecoveryStatus.ReconciliationRequired), cancellationToken);

    private async Task RevokeSessionsAndTokensAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var sessions = await dbContext.UserSessions.Where(session =>
            session.UserId == user.Id && session.RevokedUtc == null).ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.RevokedUtc = timeProvider.GetUtcNow().UtcDateTime;
            session.RevocationReason = RevocationReason;
        }

        var authorizations = new List<object>();
        await foreach (var authorization in authorizationManager.FindBySubjectAsync(user.Id.ToString(), cancellationToken))
            authorizations.Add(authorization);
        foreach (var authorization in authorizations)
            if (!await authorizationManager.TryRevokeAsync(authorization, cancellationToken))
                throw new InvalidOperationException("An OpenIddict authorization could not be revoked.");

        var tokens = new Dictionary<string, object>(StringComparer.Ordinal);
        await foreach (var token in tokenManager.FindBySubjectAsync(user.Id.ToString(), cancellationToken))
            await AddTokenAsync(tokens, token, cancellationToken);
        foreach (var authorization in authorizations)
        {
            var id = await authorizationManager.GetIdAsync(authorization, cancellationToken);
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("An authorization has no identifier.");
            await foreach (var token in tokenManager.FindByAuthorizationIdAsync(id, cancellationToken))
                await AddTokenAsync(tokens, token, cancellationToken);
        }
        foreach (var token in tokens.Values)
            if (!await tokenManager.TryRevokeAsync(token, cancellationToken))
                throw new InvalidOperationException("An OpenIddict token could not be revoked.");
    }

    private async Task AddTokenAsync(Dictionary<string, object> tokens, object token, CancellationToken cancellationToken)
    {
        var id = await tokenManager.GetIdAsync(token, cancellationToken);
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("A token has no identifier.");
        tokens.TryAdd(id, token);
    }
}
