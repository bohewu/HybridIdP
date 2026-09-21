using Core.Application.Ports;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class RecoveryAssistanceContextService(
    ApplicationDbContext dbContext,
    IRecoveryProofAuthorizer authorizer,
    IRecoveryVerificationPolicyEvaluator policyEvaluator,
    IForgotPasswordRoutingEvaluator routingEvaluator,
    IDirectoryCredentialOperatorResolutionService operatorResolutionService,
    IOptions<ForgotPasswordRecoveryOptions> recoveryOptions,
    IOptions<CredentialMigrationOptions> migrationOptions,
    IOptions<DirectoryIntegrationOptions> directoryOptions,
    TimeProvider timeProvider) : IRecoveryAssistanceContextService
{
    private const string Available = "available";
    private const string Disabled = "disabled";
    private const string Unavailable = "unavailable";

    private readonly ForgotPasswordRecoveryOptions _recoveryOptions = recoveryOptions.Value;
    private readonly CredentialMigrationOptions _migrationOptions = migrationOptions.Value;
    private readonly DirectoryIntegrationOptions _directoryOptions = directoryOptions.Value;
    private readonly RecoveryProofStore _proofStore = new(dbContext, timeProvider);

    public async Task<RecoveryAssistanceContextResult> GetAsync(
        Guid actorAccountId,
        Guid targetAccountId,
        CancellationToken cancellationToken = default)
    {
        if (actorAccountId == Guid.Empty || targetAccountId == Guid.Empty ||
            !await authorizer.IsAdministratorAuthorizedAsync(actorAccountId, cancellationToken))
        {
            return new(RecoveryAssistanceContextOutcome.Unauthorized);
        }

        try
        {
            var pendingAttempts = await dbContext.NativeDirectoryRecoveryAttempts.AsNoTracking()
                .Where(attempt => attempt.LocalAccountId == targetAccountId &&
                    (attempt.Status == NativeDirectoryRecoveryStatus.Reserved ||
                     attempt.Status == NativeDirectoryRecoveryStatus.ReconciliationRequired))
                .Take(2)
                .ToListAsync(cancellationToken);
            if (pendingAttempts.Count > 0)
            {
                var pending = await ResolvePendingAsync(
                    actorAccountId,
                    targetAccountId,
                    pendingAttempts.Count,
                    cancellationToken);
                if (pending is null)
                {
                    return new(RecoveryAssistanceContextOutcome.Unauthorized);
                }

                return Success(UnavailableOrdinary(), UnavailableTemporary(), UnavailableMigration(), pending);
            }

            var authority = await ResolveAuthorityAsync(targetAccountId, cancellationToken);
            var ordinary = await ResolveOrdinaryAsync(targetAccountId, authority, cancellationToken);
            var temporary = ResolveTemporary(authority);
            var migration = await ResolveMigrationAsync(targetAccountId, cancellationToken);
            return Success(ordinary, temporary, migration, UnavailablePending());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Success(
                UnavailableOrdinary(),
                UnavailableTemporary(),
                UnavailableMigration(),
                UnavailablePending());
        }
    }

    private async Task<PendingDirectoryAvailabilityDto?> ResolvePendingAsync(
        Guid actorAccountId,
        Guid targetAccountId,
        int pendingAttemptCount,
        CancellationToken cancellationToken)
    {
        if (!_recoveryOptions.PendingDirectorySettlementEnabled || !_directoryOptions.Enabled)
        {
            return new(Disabled, false, false);
        }

        if (pendingAttemptCount != 1)
        {
            return UnavailablePending();
        }

        var result = await operatorResolutionService.GetPendingAsync(
            actorAccountId,
            targetAccountId,
            cancellationToken);
        return result.Outcome switch
        {
            DirectoryCredentialOperatorResolutionOutcome.Available when result.Attempt is not null =>
                new PendingDirectoryAvailabilityDto(
                    Available,
                    true,
                    await CanPreparePendingAsync(
                        actorAccountId,
                        targetAccountId,
                        result.Attempt,
                        cancellationToken)),
            DirectoryCredentialOperatorResolutionOutcome.Unauthorized => null,
            _ => UnavailablePending()
        };
    }

    private async Task<bool> CanPreparePendingAsync(
        Guid actorAccountId,
        Guid targetAccountId,
        DirectoryCredentialOperatorResolutionAttempt attempt,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = timeProvider.GetUtcNow();
            var operatorAccount = await dbContext.Users.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == actorAccountId, cancellationToken);
            var authority = await ResolveAuthorityAsync(targetAccountId, cancellationToken);
            if (operatorAccount is not { IsActive: true, IsDeleted: false } ||
                operatorAccount.LockoutEnabled && operatorAccount.LockoutEnd is { } lockoutEnd && lockoutEnd > now ||
                string.IsNullOrWhiteSpace(operatorAccount.SecurityStamp) ||
                authority.Kind != AccountAuthorityKind.Directory ||
                authority.DirectoryObjectId != attempt.DirectoryObjectId)
            {
                return false;
            }

            var recoveryEmails = await dbContext.RecoveryEmails.AsNoTracking()
                .Where(candidate => candidate.LocalAccountId == targetAccountId)
                .Take(2)
                .ToListAsync(cancellationToken);
            if (recoveryEmails.Count != 1 || recoveryEmails[0].VerifiedAtUtc is null)
            {
                return false;
            }

            var decision = await policyEvaluator.EvaluateAsync(targetAccountId, cancellationToken);
            if (!decision.Enabled ||
                decision.RecoveryEmail.AddressSource != RecoveryEmailAddressSource.LocalRecord ||
                decision.RecoveryEmail.TrustOrigin != RecoveryEmailPolicyTrustOrigin.LocallyVerified ||
                !decision.RecoveryEmail.CanReceiveRecoveryOtp ||
                decision.RecoveryEmail.HasSourceConflict ||
                !string.Equals(
                    decision.RecoveryEmail.Address,
                    recoveryEmails[0].Address,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var activePreparations = await dbContext.DirectorySettlementPreparations.AsNoTracking()
                .Where(candidate => candidate.AttemptId == attempt.AttemptId &&
                    (candidate.Status == DirectorySettlementPreparationStatus.Prepared ||
                     candidate.Status == DirectorySettlementPreparationStatus.OwnershipPending ||
                     candidate.Status == DirectorySettlementPreparationStatus.Verifying))
                .Take(2)
                .ToListAsync(cancellationToken);
            if (activePreparations.Count > 1)
            {
                return false;
            }

            return activePreparations.Count == 0 ||
                activePreparations[0].ExpiresAtUtc <= now ||
                activePreparations[0].AuthorizationExpiresAtUtc <= now;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private async Task<OrdinaryRecoveryAvailabilityDto> ResolveOrdinaryAsync(
        Guid targetAccountId,
        AccountAuthority authority,
        CancellationToken cancellationToken)
    {
        if (!_recoveryOptions.OrdinaryRecoveryAssistanceEnabled ||
            !_recoveryOptions.NativeRecoveryEnabled ||
            _recoveryOptions.DeploymentCeiling != ForgotPasswordMode.Native)
        {
            return new(Disabled, false, false, false);
        }

        var runtimeMode = await dbContext.SecurityPolicies.AsNoTracking()
            .OrderBy(policy => policy.Id)
            .Select(policy => policy.ForgotPasswordMode)
            .FirstOrDefaultAsync(cancellationToken);
        if (routingEvaluator.Evaluate(runtimeMode, null).PermittedMode != ForgotPasswordMode.Native)
        {
            return new(Disabled, false, false, false);
        }

        if (authority.Kind == AccountAuthorityKind.None)
        {
            return UnavailableOrdinary();
        }

        if (authority.Kind == AccountAuthorityKind.Directory &&
            (!_recoveryOptions.NativeDirectoryRecoveryEnabled || !_directoryOptions.Enabled))
        {
            return new(Disabled, false, false, false);
        }

        var now = timeProvider.GetUtcNow();
        var challenges = await dbContext.RecoveryProofChallenges.AsNoTracking()
            .Where(candidate => candidate.LocalAccountId == targetAccountId &&
                candidate.Purpose == RecoveryProofPurpose.NativePasswordRecovery &&
                candidate.RevokedAtUtc == null && candidate.ConsumedAtUtc == null &&
                candidate.VerifiedAtUtc == null && candidate.ExpiresAtUtc > now)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (challenges.Count != 1)
        {
            return UnavailableOrdinary();
        }

        var challenge = challenges[0];
        var emails = await dbContext.RecoveryEmails.AsNoTracking()
            .Where(candidate => candidate.Id == challenge.RecoveryEmailId &&
                candidate.LocalAccountId == targetAccountId)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (emails.Count != 1 || emails[0].VerifiedAtUtc is null ||
            challenge.NativeDirectoryAuthority != (authority.Kind == AccountAuthorityKind.Directory) ||
            challenge.NativeDirectoryObjectId != authority.DirectoryObjectId ||
            challenge.NativeRecoveryEmailVersion != emails[0].Version ||
            !string.Equals(challenge.NativeSecurityStamp, authority.SecurityStamp, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(challenge.NativeContextHash) ||
            string.IsNullOrWhiteSpace(challenge.NativeCsrfHash))
        {
            return UnavailableOrdinary();
        }

        var decision = await policyEvaluator.EvaluateAsync(targetAccountId, cancellationToken);
        if (!decision.Enabled ||
            decision.RecoveryEmail.AddressSource != RecoveryEmailAddressSource.LocalRecord ||
            decision.RecoveryEmail.TrustOrigin != RecoveryEmailPolicyTrustOrigin.LocallyVerified ||
            !decision.RecoveryEmail.CanReceiveRecoveryOtp ||
            string.IsNullOrWhiteSpace(decision.RecoveryEmail.Address) ||
            !string.Equals(emails[0].Address, decision.RecoveryEmail.Address, StringComparison.OrdinalIgnoreCase))
        {
            return UnavailableOrdinary();
        }

        return new(Available, true, true, true);
    }

    private TemporaryCredentialAvailabilityDto ResolveTemporary(AccountAuthority authority)
    {
        if (!_recoveryOptions.AdminTemporaryCredentialsEnabled)
        {
            return new(Disabled, false);
        }

        return authority.Kind switch
        {
            AccountAuthorityKind.Local => new(Available, true),
            AccountAuthorityKind.Directory when _directoryOptions.Enabled &&
                _directoryOptions.TemporaryCredentialCapabilityEnabled => new(Available, true),
            AccountAuthorityKind.Directory => new(Disabled, false),
            _ => UnavailableTemporary()
        };
    }

    private async Task<MigrationRecoveryAvailabilityDto> ResolveMigrationAsync(
        Guid targetAccountId,
        CancellationToken cancellationToken)
    {
        if (!_migrationOptions.Enabled || !_migrationOptions.RecoveryEmailEnabled ||
            !_migrationOptions.RecoveryAdminAssistanceEnabled)
        {
            return new(Disabled, false, false, false);
        }

        var continuation = await _proofStore.ResolveUniqueActiveContinuationAsync(
            targetAccountId,
            cancellationToken);
        if (continuation is null)
        {
            return UnavailableMigration();
        }

        var canResendOtp = false;
        if (_migrationOptions.MigrationEmailOtpEnabled)
        {
            var verifiedRecoveryEmails = await dbContext.RecoveryEmails.AsNoTracking()
                .Where(candidate => candidate.LocalAccountId == targetAccountId && candidate.VerifiedAtUtc != null)
                .Take(2)
                .ToListAsync(cancellationToken);
            canResendOtp = verifiedRecoveryEmails.Count == 1 &&
                await dbContext.Users.AsNoTracking()
                    .AnyAsync(candidate => candidate.Id == targetAccountId, cancellationToken);
        }

        return new(
            Available,
            canResendOtp,
            true,
            true);
    }

    private async Task<AccountAuthority> ResolveAuthorityAsync(
        Guid targetAccountId,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == targetAccountId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (user is not { IsActive: true, IsDeleted: false } ||
            string.IsNullOrWhiteSpace(user.SecurityStamp) ||
            user.LockoutEnabled && user.LockoutEnd is { } lockoutEnd && lockoutEnd > now)
        {
            return AccountAuthority.None;
        }

        if (user.PersonId is { } personId)
        {
            var person = await dbContext.Persons.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == personId, cancellationToken);
            if (person?.CanAuthenticate() != true)
            {
                return AccountAuthority.None;
            }
        }

        var bindings = await dbContext.ProviderSubjectDirectoryBindings.AsNoTracking()
            .Where(candidate => candidate.LocalAccountId == targetAccountId)
            .Take(2)
            .ToListAsync(cancellationToken);
        var migrations = await dbContext.CredentialMigrationStateRecords.AsNoTracking()
            .Where(candidate => candidate.LocalAccountId == targetAccountId)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (bindings.Count == 0 && migrations.Count == 0 && !string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return new(AccountAuthorityKind.Local, null, user.SecurityStamp);
        }

        if (bindings.Count == 1 && migrations.Count == 1 &&
            migrations[0].State == CredentialMigrationState.LocalFinalized &&
            migrations[0].ProviderSubjectDirectoryBindingId == bindings[0].Id)
        {
            return new(AccountAuthorityKind.Directory, bindings[0].DirectoryObjectId, user.SecurityStamp);
        }

        return AccountAuthority.None;
    }

    private static RecoveryAssistanceContextResult Success(
        OrdinaryRecoveryAvailabilityDto ordinary,
        TemporaryCredentialAvailabilityDto temporary,
        MigrationRecoveryAvailabilityDto migration,
        PendingDirectoryAvailabilityDto pending) =>
        new(
            RecoveryAssistanceContextOutcome.Available,
            new RecoveryAssistanceContextDto(ordinary, temporary, migration, pending));

    private static OrdinaryRecoveryAvailabilityDto UnavailableOrdinary() =>
        new(Unavailable, false, false, false);

    private static TemporaryCredentialAvailabilityDto UnavailableTemporary() =>
        new(Unavailable, false);

    private static MigrationRecoveryAvailabilityDto UnavailableMigration() =>
        new(Unavailable, false, false, false);

    private static PendingDirectoryAvailabilityDto UnavailablePending() =>
        new(Unavailable, false, false);

    private enum AccountAuthorityKind
    {
        None,
        Local,
        Directory
    }

    private sealed record AccountAuthority(
        AccountAuthorityKind Kind,
        Guid? DirectoryObjectId,
        string? SecurityStamp)
    {
        public static AccountAuthority None { get; } = new(AccountAuthorityKind.None, null, null);
    }
}
