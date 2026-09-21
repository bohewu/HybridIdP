using Core.Application;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace Infrastructure.Services;

public sealed class DirectoryCredentialOperatorResolutionService(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IDirectoryCredentialVerifier directoryCredentialVerifier,
    IOpenIddictAuthorizationManager authorizationManager,
    IOpenIddictTokenManager tokenManager,
    IRecoveryProofAuthorizer authorizer,
    IRecoveryProofAudit audit,
    IRecoveryVerificationPolicyEvaluator policyEvaluator,
    IEmailService emailService,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IOptions<ForgotPasswordRecoveryOptions> recoveryOptions,
    IOptions<DirectoryIntegrationOptions> directoryOptions,
    TimeProvider timeProvider) : IDirectoryCredentialOperatorResolutionService
{
    private const string RevocationReason = "directory-credential-operator-resolution";
    private readonly ForgotPasswordRecoveryOptions _recoveryOptions = recoveryOptions.Value;
    private readonly DirectoryIntegrationOptions _directoryOptions = directoryOptions.Value;

    public async Task<DirectoryCredentialOperatorResolutionLookupResult> GetPendingAsync(
        Guid actorAccountId, Guid localAccountId, CancellationToken cancellationToken = default)
    {
        if (actorAccountId == Guid.Empty || localAccountId == Guid.Empty ||
            !await authorizer.IsAdministratorAuthorizedAsync(actorAccountId, cancellationToken) ||
            !await HasCurrentUsersUpdatePermissionAsync(actorAccountId, cancellationToken))
        {
            return new(DirectoryCredentialOperatorResolutionOutcome.Unauthorized);
        }

        try
        {
            var attempt = await dbContext.NativeDirectoryRecoveryAttempts.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.LocalAccountId == localAccountId &&
                    (candidate.Status == NativeDirectoryRecoveryStatus.Reserved ||
                     candidate.Status == NativeDirectoryRecoveryStatus.ReconciliationRequired), cancellationToken);
            var authority = await ResolveCurrentAuthorityAsync(localAccountId, cancellationToken);
            if (attempt is null || !IsSupportedKind(attempt.OperationKind) ||
                authority is null || authority.Binding.DirectoryObjectId != attempt.DirectoryObjectId)
            {
                return UnavailableLookup();
            }

            return new(DirectoryCredentialOperatorResolutionOutcome.Available,
                new DirectoryCredentialOperatorResolutionAttempt(attempt.Id, attempt.Version,
                    attempt.OperationKind, attempt.Status, attempt.DirectoryObjectId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return UnavailableLookup(); }
    }

    public async Task<DirectorySettlementPreparationResult> PrepareAsync(
        DirectorySettlementPreparationRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsCapabilityAvailable() || !IsValidPreparationRequest(request))
        {
            return new(DirectoryCredentialOperatorResolutionOutcome.Unavailable);
        }
        if (!await authorizer.IsAdministratorAuthorizedAsync(request.ActorAccountId, cancellationToken) ||
            !await HasCurrentUsersUpdatePermissionAsync(request.ActorAccountId, cancellationToken))
        {
            return new(DirectoryCredentialOperatorResolutionOutcome.Unauthorized);
        }

        try
        {
            var now = timeProvider.GetUtcNow();
            var operatorAccount = await dbContext.Users.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == request.ActorAccountId, cancellationToken);
            var authority = await ResolveCurrentAuthorityAsync(request.LocalAccountId, cancellationToken);
            var recoveryEmail = await ResolveVerifiedRecoveryEmailAsync(request.LocalAccountId, cancellationToken);
            if (!IsActiveAccount(operatorAccount, now) || string.IsNullOrWhiteSpace(operatorAccount!.SecurityStamp) ||
                authority is null || authority.Binding.DirectoryObjectId != request.DirectoryObjectId ||
                recoveryEmail is null || string.IsNullOrWhiteSpace(authority.User.SecurityStamp))
            {
                return new(DirectoryCredentialOperatorResolutionOutcome.Unavailable);
            }

            var continuation = RecoveryProofSecurity.GenerateOpaqueValue();
            DirectorySettlementPreparation preparation;
            await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
            {
                var attempt = await dbContext.NativeDirectoryRecoveryAttempts.SingleAsync(candidate =>
                    candidate.Id == request.AttemptId && candidate.LocalAccountId == request.LocalAccountId &&
                    candidate.DirectoryObjectId == request.DirectoryObjectId && candidate.OperationKind == request.OperationKind &&
                    candidate.Status == request.ExpectedStatus && candidate.Version == request.ExpectedVersion, cancellationToken);
                var activePreparation = await dbContext.DirectorySettlementPreparations.SingleOrDefaultAsync(candidate =>
                    candidate.AttemptId == attempt.Id &&
                    (candidate.Status == DirectorySettlementPreparationStatus.Prepared ||
                     candidate.Status == DirectorySettlementPreparationStatus.OwnershipPending ||
                     candidate.Status == DirectorySettlementPreparationStatus.Verifying), cancellationToken);
                if (activePreparation is not null && activePreparation.ExpiresAtUtc > now &&
                    activePreparation.AuthorizationExpiresAtUtc > now)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new(DirectoryCredentialOperatorResolutionOutcome.Unavailable);
                }
                if (activePreparation is not null)
                {
                    activePreparation.Cancel(now);
                    if (activePreparation.OwnershipChallengeId is { } activeChallengeId)
                    {
                        var activeChallenge = await dbContext.RecoveryProofChallenges.SingleOrDefaultAsync(
                            candidate => candidate.Id == activeChallengeId, cancellationToken);
                        activeChallenge?.Revoke(now);
                    }
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                attempt.ReserveSettlementPreparation(request.ExpectedVersion, now);
                preparation = new DirectorySettlementPreparation(
                    attempt.Id, attempt.Version, attempt.OperationKind, attempt.Status, attempt.LocalAccountId,
                    attempt.DirectoryObjectId, authority.Binding.Id, authority.Migration.Id, authority.Migration.Version,
                    recoveryEmail.Id, recoveryEmail.Version, authority.User.SecurityStamp!, operatorAccount.Id,
                    operatorAccount.SecurityStamp!, now,
                    now.AddMinutes(_recoveryOptions.PendingDirectorySettlementAuthorizationMinutes), request.Disposition,
                    request.EvidenceCategory, request.EvidenceReference.Trim(), RecoveryProofSecurity.Hash(continuation), now,
                    now.AddMinutes(_recoveryOptions.PendingDirectorySettlementLifetimeMinutes));
                dbContext.DirectorySettlementPreparations.Add(preparation);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            try
            {
                await emailService.SendEmailAsync(recoveryEmail.Address, "Pending directory credential settlement",
                    $"Use this one-time continuation in the approved recovery page: {continuation}", false, cancellationToken);
                return new(DirectoryCredentialOperatorResolutionOutcome.Available, preparation.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                await CancelPreparationAsync(preparation.Id, cancellationToken);
                return new(DirectoryCredentialOperatorResolutionOutcome.Unavailable);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            dbContext.ChangeTracker.Clear();
            return new(DirectoryCredentialOperatorResolutionOutcome.Unavailable);
        }
    }

    public async Task<DirectorySettlementClaimResult> ClaimAsync(
        DirectorySettlementClaimRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsCapabilityAvailable() || string.IsNullOrWhiteSpace(request.Continuation) ||
            request.Continuation.Length > 256 || !IsValidContext(request.Context))
        {
            return UnavailableClaim();
        }

        try
        {
            var now = timeProvider.GetUtcNow();
            var continuationHash = RecoveryProofSecurity.Hash(request.Continuation);
            var snapshot = await dbContext.DirectorySettlementPreparations.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.ContinuationHash == continuationHash &&
                    candidate.Status == DirectorySettlementPreparationStatus.Prepared,
                    cancellationToken);
            if (snapshot is null || !await HasValidPreparationBindingsAsync(snapshot, true, cancellationToken))
            {
                return UnavailableClaim();
            }

            var recoveryEmail = await ResolveVerifiedRecoveryEmailAsync(snapshot.LocalAccountId, cancellationToken);
            var user = await dbContext.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == snapshot.LocalAccountId,
                cancellationToken);
            if (recoveryEmail is null || recoveryEmail.Id != snapshot.RecoveryEmailId ||
                recoveryEmail.Version != snapshot.RecoveryEmailVersion)
            {
                return UnavailableClaim();
            }

            var code = RecoveryProofSecurity.GenerateNumericCode();
            var codeHash = passwordHasher.HashPassword(user, RecoveryProofSecurity.BindToContext(code,
                request.Context.ContextHash, request.Context.CsrfHash, CreateRecoveryEmailBinding(recoveryEmail)));
            var challenge = new RecoveryProofChallenge(recoveryEmail.Id, user.Id,
                RecoveryProofPurpose.PendingDirectorySettlement, codeHash, now, snapshot.ExpiresAtUtc);

            dbContext.ChangeTracker.Clear();
            await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
            {
                var preparation = await dbContext.DirectorySettlementPreparations.SingleAsync(candidate =>
                    candidate.Id == snapshot.Id && candidate.Version == snapshot.Version &&
                    candidate.Status == DirectorySettlementPreparationStatus.Prepared, cancellationToken);
                dbContext.RecoveryProofChallenges.Add(challenge);
                if (!preparation.TryClaim(challenge.Id, request.Context.ContextHash, request.Context.CsrfHash, now))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return UnavailableClaim();
                }
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            try
            {
                await emailService.SendEmailAsync(recoveryEmail.Address, "Pending settlement ownership verification",
                    $"Your verification code is {code}. It expires shortly.", false, cancellationToken);
                return new(DirectorySettlementVerificationOutcome.ChallengeIssued, snapshot.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                await CancelPreparationAsync(snapshot.Id, cancellationToken);
                return UnavailableClaim();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            dbContext.ChangeTracker.Clear();
            return UnavailableClaim();
        }
    }

    public async Task<DirectorySettlementVerificationOutcome> VerifyAndFinalizeAsync(
        DirectorySettlementVerificationRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsCapabilityAvailable() || request.PreparationId == Guid.Empty || request.OwnershipCode.Length != 6 ||
            request.OwnershipCode.Any(character => !char.IsAsciiDigit(character)) ||
            string.IsNullOrEmpty(request.CurrentCredential) || request.CurrentCredential.Length > 1024 ||
            !IsValidContext(request.Context))
        {
            return DirectorySettlementVerificationOutcome.Unavailable;
        }

        try
        {
            var now = timeProvider.GetUtcNow();
            var snapshot = await dbContext.DirectorySettlementPreparations.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == request.PreparationId &&
                    candidate.Status == DirectorySettlementPreparationStatus.OwnershipPending,
                    cancellationToken);
            if (snapshot is null || !MatchesContext(snapshot, request.Context) ||
                !await HasValidPreparationBindingsAsync(snapshot, true, cancellationToken))
            {
                return DirectorySettlementVerificationOutcome.Unavailable;
            }

            var user = await dbContext.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == snapshot.LocalAccountId,
                cancellationToken);
            var recoveryEmail = await dbContext.RecoveryEmails.AsNoTracking().SingleAsync(candidate =>
                candidate.Id == snapshot.RecoveryEmailId && candidate.LocalAccountId == snapshot.LocalAccountId,
                cancellationToken);
            var challengeSnapshot = await dbContext.RecoveryProofChallenges.AsNoTracking().SingleAsync(candidate =>
                candidate.Id == snapshot.OwnershipChallengeId &&
                candidate.Purpose == RecoveryProofPurpose.PendingDirectorySettlement, cancellationToken);
            var boundCode = RecoveryProofSecurity.BindToContext(request.OwnershipCode, request.Context.ContextHash,
                request.Context.CsrfHash, CreateRecoveryEmailBinding(recoveryEmail));
            if (passwordHasher.VerifyHashedPassword(user, challengeSnapshot.CodeHash, boundCode) == PasswordVerificationResult.Failed)
            {
                await ReserveDeniedOwnershipAttemptAsync(snapshot.Id, challengeSnapshot.Id, cancellationToken);
                return DirectorySettlementVerificationOutcome.Unavailable;
            }

            dbContext.ChangeTracker.Clear();
            await using (var reserveTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
            {
                var preparation = await dbContext.DirectorySettlementPreparations.SingleAsync(candidate =>
                    candidate.Id == snapshot.Id && candidate.Version == snapshot.Version &&
                    candidate.Status == DirectorySettlementPreparationStatus.OwnershipPending, cancellationToken);
                var challenge = await dbContext.RecoveryProofChallenges.SingleAsync(candidate =>
                    candidate.Id == challengeSnapshot.Id && candidate.Version == challengeSnapshot.Version &&
                    candidate.ConsumedAtUtc == null && candidate.RevokedAtUtc == null, cancellationToken);
                if (!challenge.TryReserveAttempt(now, _recoveryOptions.NativeOtpMaxAttempts) ||
                    !preparation.TryReserveVerification(now))
                {
                    await reserveTransaction.RollbackAsync(cancellationToken);
                    return DirectorySettlementVerificationOutcome.Unavailable;
                }
                await dbContext.SaveChangesAsync(cancellationToken);
                await reserveTransaction.CommitAsync(cancellationToken);
            }

            var verification = await directoryCredentialVerifier.VerifyCredentialAsync(snapshot.DirectoryObjectId,
                request.CurrentCredential, cancellationToken);
            if (verification.Outcome != DirectoryCredentialOutcome.Authenticated || verification.Identity is not
                { ObjectId: var objectId, IsEligible: true, IsEnabled: true, IsLocked: false } ||
                objectId != snapshot.DirectoryObjectId)
            {
                await CancelPreparationAsync(snapshot.Id, cancellationToken);
                return DirectorySettlementVerificationOutcome.Unavailable;
            }
            return await FinalizeAsync(snapshot.Id, request.Context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            dbContext.ChangeTracker.Clear();
            return DirectorySettlementVerificationOutcome.Unavailable;
        }
    }

    public async Task<DirectoryCredentialOperatorResolutionOutcome> CancelAsync(
        Guid actorAccountId, Guid preparationId, CancellationToken cancellationToken = default)
    {
        if (actorAccountId == Guid.Empty || preparationId == Guid.Empty ||
            !await authorizer.IsAdministratorAuthorizedAsync(actorAccountId, cancellationToken) ||
            !await HasCurrentUsersUpdatePermissionAsync(actorAccountId, cancellationToken))
        {
            return DirectoryCredentialOperatorResolutionOutcome.Unauthorized;
        }
        try
        {
            var preparation = await dbContext.DirectorySettlementPreparations.SingleOrDefaultAsync(candidate =>
                candidate.Id == preparationId && candidate.OperatorAccountId == actorAccountId, cancellationToken);
            if (preparation is null) return DirectoryCredentialOperatorResolutionOutcome.Unavailable;
            preparation.Cancel(timeProvider.GetUtcNow());
            if (preparation.OwnershipChallengeId is { } challengeId)
            {
                var challenge = await dbContext.RecoveryProofChallenges.SingleOrDefaultAsync(candidate => candidate.Id == challengeId,
                    cancellationToken);
                challenge?.Revoke(timeProvider.GetUtcNow());
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            return DirectoryCredentialOperatorResolutionOutcome.Resolved;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            dbContext.ChangeTracker.Clear();
            return DirectoryCredentialOperatorResolutionOutcome.Unavailable;
        }
    }

    public async Task<DirectorySettlementVerificationOutcome> CancelUserAsync(
        Guid preparationId, NativeRecoveryContext context, CancellationToken cancellationToken = default)
    {
        if (preparationId == Guid.Empty || !IsValidContext(context))
            return DirectorySettlementVerificationOutcome.Unavailable;
        try
        {
            var preparation = await dbContext.DirectorySettlementPreparations.SingleOrDefaultAsync(candidate =>
                candidate.Id == preparationId &&
                (candidate.Status == DirectorySettlementPreparationStatus.OwnershipPending ||
                 candidate.Status == DirectorySettlementPreparationStatus.Verifying),
                cancellationToken);
            if (preparation is null || !MatchesContext(preparation, context))
                return DirectorySettlementVerificationOutcome.Unavailable;
            preparation.Cancel(timeProvider.GetUtcNow());
            if (preparation.OwnershipChallengeId is { } challengeId)
            {
                var challenge = await dbContext.RecoveryProofChallenges.SingleAsync(candidate => candidate.Id == challengeId,
                    cancellationToken);
                challenge.Revoke(timeProvider.GetUtcNow());
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            return DirectorySettlementVerificationOutcome.Resolved;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            dbContext.ChangeTracker.Clear();
            return DirectorySettlementVerificationOutcome.Unavailable;
        }
    }

    private async Task<DirectorySettlementVerificationOutcome> FinalizeAsync(
        Guid preparationId, NativeRecoveryContext context, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var now = timeProvider.GetUtcNow();
            var preparation = await dbContext.DirectorySettlementPreparations.SingleAsync(candidate =>
                candidate.Id == preparationId && candidate.Status == DirectorySettlementPreparationStatus.Verifying,
                cancellationToken);
            if (!MatchesContext(preparation, context) ||
                !await HasValidPreparationBindingsAsync(preparation, true, cancellationToken))
            {
                throw new InvalidOperationException("Settlement preparation bindings changed.");
            }
            var attempt = await dbContext.NativeDirectoryRecoveryAttempts.SingleAsync(candidate =>
                candidate.Id == preparation.AttemptId && candidate.LocalAccountId == preparation.LocalAccountId &&
                candidate.DirectoryObjectId == preparation.DirectoryObjectId &&
                candidate.OperationKind == preparation.OperationKind && candidate.Status == preparation.ExpectedAttemptStatus &&
                candidate.Version == preparation.ExpectedAttemptVersion, cancellationToken);
            var challenge = await dbContext.RecoveryProofChallenges.SingleAsync(candidate =>
                candidate.Id == preparation.OwnershipChallengeId &&
                candidate.Purpose == RecoveryProofPurpose.PendingDirectorySettlement &&
                candidate.ConsumedAtUtc == null && candidate.RevokedAtUtc == null, cancellationToken);
            var user = await dbContext.Users.SingleAsync(candidate => candidate.Id == preparation.LocalAccountId,
                cancellationToken);
            if (!preparation.TryConsume(now) || !challenge.TryConsumePendingSettlement(now))
            {
                throw new InvalidOperationException("Settlement preparation was already consumed.");
            }
            if (preparation.OperationKind != NativeDirectoryCredentialOperationKind.NativeReset ||
                preparation.Disposition == DirectorySettlementDisposition.ApprovedOutOfBandRecoveryCompleted)
            {
                user.RequiresPasswordChange = true;
            }
            var securityStamp = await userManager.UpdateSecurityStampAsync(user);
            if (!securityStamp.Succeeded) throw new InvalidOperationException("Local security state was not rotated.");
            await RevokeSessionsAndTokensAsync(user, cancellationToken);
            attempt.Complete(NativeDirectoryRecoveryStatus.OperatorResolved, now);
            await dbContext.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(new RecoveryProofAuditEvent(attempt.Id,
                RecoveryProofAuditCategory.DirectoryCredentialOperatorResolved, user.Id,
                preparation.OperatorAccountId), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return DirectorySettlementVerificationOutcome.Resolved;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            return DirectorySettlementVerificationOutcome.Unavailable;
        }
    }

    private async Task<bool> HasValidPreparationBindingsAsync(
        DirectorySettlementPreparation preparation, bool requireOperatorWindow, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (!IsCapabilityAvailable() || preparation.CancelledAtUtc is not null || preparation.ConsumedAtUtc is not null ||
            preparation.ExpiresAtUtc <= now || !preparation.MfaAuthorized || !preparation.UsersUpdateAuthorized ||
            !preparation.OriginalWritersDrained || requireOperatorWindow && preparation.AuthorizationExpiresAtUtc <= now)
        {
            return false;
        }
        var attemptExists = await dbContext.NativeDirectoryRecoveryAttempts.AsNoTracking().AnyAsync(candidate =>
            candidate.Id == preparation.AttemptId && candidate.LocalAccountId == preparation.LocalAccountId &&
            candidate.DirectoryObjectId == preparation.DirectoryObjectId && candidate.OperationKind == preparation.OperationKind &&
            candidate.Status == preparation.ExpectedAttemptStatus && candidate.Version == preparation.ExpectedAttemptVersion,
            cancellationToken);
        var authority = await ResolveCurrentAuthorityAsync(preparation.LocalAccountId, cancellationToken);
        var recoveryEmail = await ResolveVerifiedRecoveryEmailAsync(preparation.LocalAccountId, cancellationToken);
        var operatorAccount = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == preparation.OperatorAccountId, cancellationToken);
        return attemptExists && authority is not null && authority.Binding.Id == preparation.BindingId &&
            authority.Binding.DirectoryObjectId == preparation.DirectoryObjectId &&
            authority.Migration.Id == preparation.MigrationId && authority.Migration.Version == preparation.MigrationVersion &&
            string.Equals(authority.User.SecurityStamp, preparation.AccountSecurityStamp, StringComparison.Ordinal) &&
            recoveryEmail?.Id == preparation.RecoveryEmailId && recoveryEmail.Version == preparation.RecoveryEmailVersion &&
            IsActiveAccount(operatorAccount, now) &&
            string.Equals(operatorAccount!.SecurityStamp, preparation.OperatorSecurityStamp, StringComparison.Ordinal) &&
            await HasCurrentUsersUpdatePermissionAsync(preparation.OperatorAccountId, cancellationToken);
    }

    private async Task<CurrentAuthority?> ResolveCurrentAuthorityAsync(Guid localAccountId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == localAccountId,
            cancellationToken);
        if (!IsActiveAccount(user, timeProvider.GetUtcNow())) return null;
        if (user!.PersonId is { } personId)
        {
            var person = await dbContext.Persons.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == personId,
                cancellationToken);
            if (person?.CanAuthenticate() != true) return null;
        }
        var binding = await dbContext.ProviderSubjectDirectoryBindings.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.LocalAccountId == localAccountId, cancellationToken);
        var migration = await dbContext.CredentialMigrationStateRecords.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.LocalAccountId == localAccountId, cancellationToken);
        return binding is not null && migration is not null && migration.State == CredentialMigrationState.LocalFinalized &&
            migration.ProviderSubjectDirectoryBindingId == binding.Id
            ? new CurrentAuthority(user, binding, migration) : null;
    }

    private async Task<RecoveryEmailRecord?> ResolveVerifiedRecoveryEmailAsync(
        Guid localAccountId, CancellationToken cancellationToken)
    {
        var recoveryEmail = await dbContext.RecoveryEmails.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.LocalAccountId == localAccountId, cancellationToken);
        if (recoveryEmail?.VerifiedAtUtc is null) return null;
        var decision = await policyEvaluator.EvaluateAsync(localAccountId, cancellationToken);
        return decision.Enabled && decision.RecoveryEmail.AddressSource == RecoveryEmailAddressSource.LocalRecord &&
            decision.RecoveryEmail.TrustOrigin == RecoveryEmailPolicyTrustOrigin.LocallyVerified &&
            decision.RecoveryEmail.CanReceiveRecoveryOtp && !decision.RecoveryEmail.HasSourceConflict &&
            string.Equals(decision.RecoveryEmail.Address, recoveryEmail.Address, StringComparison.OrdinalIgnoreCase)
            ? recoveryEmail : null;
    }

    private async Task<bool> HasCurrentUsersUpdatePermissionAsync(Guid operatorAccountId, CancellationToken cancellationToken)
    {
        var roleIds = await dbContext.UserRoles.AsNoTracking().Where(membership => membership.UserId == operatorAccountId)
            .Select(membership => membership.RoleId).ToListAsync(cancellationToken);
        var roles = await dbContext.Roles.AsNoTracking().Where(role => roleIds.Contains(role.Id)).ToListAsync(cancellationToken);
        return roles.Any(role => string.Equals(role.Name, AuthConstants.Roles.Admin, StringComparison.OrdinalIgnoreCase) ||
            (role.Permissions ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(permission => permission.Trim()).Contains(Permissions.Users.Update, StringComparer.OrdinalIgnoreCase));
    }

    private async Task ReserveDeniedOwnershipAttemptAsync(Guid preparationId, Guid challengeId,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        try
        {
            var challenge = await dbContext.RecoveryProofChallenges.SingleAsync(candidate =>
                candidate.Id == challengeId && candidate.ConsumedAtUtc == null && candidate.RevokedAtUtc == null,
                cancellationToken);
            if (challenge.TryReserveAttempt(timeProvider.GetUtcNow(), _recoveryOptions.NativeOtpMaxAttempts))
            {
                if (challenge.VerificationAttempts >= _recoveryOptions.NativeOtpMaxAttempts)
                {
                    challenge.Revoke(timeProvider.GetUtcNow());
                    var preparation = await dbContext.DirectorySettlementPreparations.SingleAsync(candidate =>
                        candidate.Id == preparationId, cancellationToken);
                    preparation.Cancel(timeProvider.GetUtcNow());
                }
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch { dbContext.ChangeTracker.Clear(); }
    }

    private async Task CancelPreparationAsync(Guid preparationId, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        try
        {
            var preparation = await dbContext.DirectorySettlementPreparations.SingleOrDefaultAsync(candidate =>
                candidate.Id == preparationId, cancellationToken);
            if (preparation is null) return;
            preparation.Cancel(timeProvider.GetUtcNow());
            if (preparation.OwnershipChallengeId is { } challengeId)
            {
                var challenge = await dbContext.RecoveryProofChallenges.SingleOrDefaultAsync(candidate => candidate.Id == challengeId,
                    cancellationToken);
                challenge?.Revoke(timeProvider.GetUtcNow());
            }
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch { dbContext.ChangeTracker.Clear(); }
    }

    private async Task RevokeSessionsAndTokensAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var sessions = await dbContext.UserSessions.Where(session => session.UserId == user.Id && session.RevokedUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.RevokedUtc = timeProvider.GetUtcNow().UtcDateTime;
            session.RevocationReason = RevocationReason;
        }
        var subject = user.Id.ToString();
        var authorizations = new List<object>();
        await foreach (var authorization in authorizationManager.FindBySubjectAsync(subject, cancellationToken))
            authorizations.Add(authorization);
        foreach (var authorization in authorizations)
            if (!await authorizationManager.TryRevokeAsync(authorization, cancellationToken))
                throw new InvalidOperationException("An OpenIddict authorization could not be revoked.");
        var tokens = new Dictionary<string, object>(StringComparer.Ordinal);
        await foreach (var token in tokenManager.FindBySubjectAsync(subject, cancellationToken))
            await AddTokenAsync(tokens, token, cancellationToken);
        foreach (var authorization in authorizations)
        {
            var authorizationId = await authorizationManager.GetIdAsync(authorization, cancellationToken);
            if (string.IsNullOrWhiteSpace(authorizationId))
                throw new InvalidOperationException("An OpenIddict authorization has no identifier.");
            await foreach (var token in tokenManager.FindByAuthorizationIdAsync(authorizationId, cancellationToken))
                await AddTokenAsync(tokens, token, cancellationToken);
        }
        foreach (var token in tokens.Values)
            if (!await tokenManager.TryRevokeAsync(token, cancellationToken))
                throw new InvalidOperationException("An OpenIddict token could not be revoked.");
    }

    private async Task AddTokenAsync(Dictionary<string, object> tokens, object token, CancellationToken cancellationToken)
    {
        var id = await tokenManager.GetIdAsync(token, cancellationToken);
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("An OpenIddict token has no identifier.");
        tokens.TryAdd(id, token);
    }

    private bool IsCapabilityAvailable() =>
        _recoveryOptions.PendingDirectorySettlementEnabled && _directoryOptions.Enabled;
    private static bool IsValidPreparationRequest(DirectorySettlementPreparationRequest request) =>
        request.ActorAccountId != Guid.Empty && request.LocalAccountId != Guid.Empty && request.AttemptId != Guid.Empty &&
        request.ExpectedVersion > 0 && request.DirectoryObjectId != Guid.Empty && IsSupportedKind(request.OperationKind) &&
        request.ExpectedStatus is NativeDirectoryRecoveryStatus.Reserved or NativeDirectoryRecoveryStatus.ReconciliationRequired &&
        request.OriginalWritersDrained && Enum.IsDefined(request.Disposition) && Enum.IsDefined(request.EvidenceCategory) &&
        IsBoundedEvidenceReference(request.EvidenceReference) &&
        IsCompatibleEvidence(request.Disposition, request.EvidenceCategory);
    private static bool IsSupportedKind(NativeDirectoryCredentialOperationKind kind) =>
        kind is NativeDirectoryCredentialOperationKind.NativeReset or NativeDirectoryCredentialOperationKind.AdminTemporaryIssue or
            NativeDirectoryCredentialOperationKind.RequiredChange;
    private static bool IsCompatibleEvidence(DirectorySettlementDisposition disposition,
        DirectorySettlementEvidenceCategory category) => disposition switch
        {
            DirectorySettlementDisposition.OriginalOperationSettled =>
                category == DirectorySettlementEvidenceCategory.ApprovedDirectoryOperation,
            DirectorySettlementDisposition.ApprovedOutOfBandRecoveryCompleted =>
                category == DirectorySettlementEvidenceCategory.ApprovedRecoveryOperation,
            _ => false
        };
    private static bool IsBoundedEvidenceReference(string value) => !string.IsNullOrWhiteSpace(value) &&
        value.Trim().Length <= 200 && value.Trim().All(character => char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_' or ':' or '.' or '/');
    private static bool IsValidContext(NativeRecoveryContext context) =>
        !string.IsNullOrWhiteSpace(context.ContextHash) && context.ContextHash.Length <= 256 &&
        !string.IsNullOrWhiteSpace(context.CsrfHash) && context.CsrfHash.Length <= 256;
    private static bool MatchesContext(DirectorySettlementPreparation preparation, NativeRecoveryContext context) =>
        string.Equals(preparation.ContextHash, context.ContextHash, StringComparison.Ordinal) &&
        string.Equals(preparation.CsrfHash, context.CsrfHash, StringComparison.Ordinal);
    private static bool IsActiveAccount(ApplicationUser? user, DateTimeOffset now) =>
        user is { IsActive: true, IsDeleted: false } &&
        !(user.LockoutEnabled && user.LockoutEnd is { } lockoutEnd && lockoutEnd > now);
    private static string CreateRecoveryEmailBinding(RecoveryEmailRecord recoveryEmail) =>
        $"{recoveryEmail.Id:N}:{recoveryEmail.LocalAccountId:N}:{recoveryEmail.Version}:{recoveryEmail.NormalizedAddress}";
    private static DirectoryCredentialOperatorResolutionLookupResult UnavailableLookup() =>
        new(DirectoryCredentialOperatorResolutionOutcome.Unavailable);
    private static DirectorySettlementClaimResult UnavailableClaim() =>
        new(DirectorySettlementVerificationOutcome.Unavailable);
    private sealed record CurrentAuthority(ApplicationUser User, ProviderSubjectDirectoryBinding Binding,
        CredentialMigrationStateRecord Migration);
}
