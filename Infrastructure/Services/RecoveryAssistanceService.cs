using Core.Application.Ports;
using Core.Domain.Entities;
using Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class RecoveryAssistanceService : IRecoveryAssistanceService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IRecoveryProofAuthorizer _authorizer;
    private readonly RecoveryEmailService _recoveryEmailService;
    private readonly MigrationOtpProofService _migrationOtpProofService;
    private readonly IRecoveryProofAudit _audit;
    private readonly CredentialMigrationOptions _options;
    private readonly RecoveryProofStore _store;

    public RecoveryAssistanceService(
        ApplicationDbContext dbContext,
        IRecoveryProofAuthorizer authorizer,
        RecoveryEmailService recoveryEmailService,
        MigrationOtpProofService migrationOtpProofService,
        IRecoveryProofAudit audit,
        IOptions<CredentialMigrationOptions> options,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _authorizer = authorizer;
        _recoveryEmailService = recoveryEmailService;
        _migrationOtpProofService = migrationOtpProofService;
        _audit = audit;
        _options = options.Value;
        _store = new RecoveryProofStore(dbContext, timeProvider);
    }

    public async Task<MigrationOtpSendResult> ResendMigrationOtpAsync(
        AdminMigrationOtpResendRequest request,
        CancellationToken cancellationToken = default)
    {
        var continuation = await AuthorizeAndResolveAsync(
            request.ActorAccountId,
            request.TargetAccountId,
            cancellationToken);
        if (continuation.Outcome != RecoveryProofOutcome.Success)
        {
            return new MigrationOtpSendResult(continuation.Outcome);
        }

        var result = await _migrationOtpProofService.SendForAdministratorAsync(
            continuation.Continuation!,
            cancellationToken);
        if (result.Outcome == RecoveryProofOutcome.Success)
        {
            await RecordAuditAsync(
                RecoveryProofAuditCategory.AdminMigrationOtpResent,
                continuation.Continuation!.LocalAccountId,
                request.ActorAccountId,
                cancellationToken);
        }

        return result;
    }

    public async Task<RecoveryEmailChangeResult> ReplaceRecoveryEmailAsync(
        AdminRecoveryEmailReplacementRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HasRequiredEvidence(request.IdentityCheckEvidence, request.Reason))
        {
            return new RecoveryEmailChangeResult(RecoveryProofOutcome.Invalid);
        }

        var continuation = await AuthorizeAndResolveAsync(
            request.ActorAccountId,
            request.TargetAccountId,
            cancellationToken);
        if (continuation.Outcome != RecoveryProofOutcome.Success)
        {
            return new RecoveryEmailChangeResult(continuation.Outcome);
        }

        var result = await _recoveryEmailService.BeginAdministrativeReplacementAsync(
            continuation.Continuation!,
            request.ActorAccountId,
            request.CandidateAddress,
            request.IdentityCheckEvidence.Trim(),
            request.Reason.Trim(),
            cancellationToken);
        if (result.Outcome == RecoveryProofOutcome.Success)
        {
            await RecordAuditAsync(
                RecoveryProofAuditCategory.AdminRecoveryAddressReplaced,
                continuation.Continuation!.LocalAccountId,
                request.ActorAccountId,
                cancellationToken);
        }

        return result;
    }

    public async Task<ResetApprovalIssueResult> IssueResetApprovalAsync(
        AdminResetApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HasRequiredEvidence(request.IdentityCheckEvidence, request.Reason))
        {
            return new ResetApprovalIssueResult(RecoveryProofOutcome.Invalid);
        }

        var continuation = await AuthorizeAndResolveAsync(
            request.ActorAccountId,
            request.TargetAccountId,
            cancellationToken);
        if (continuation.Outcome != RecoveryProofOutcome.Success)
        {
            return new ResetApprovalIssueResult(continuation.Outcome);
        }

        var resolved = continuation.Continuation!;
        var now = _store.UtcNow;
        var authorizationBinding = RecoveryProofSecurity.GenerateOpaqueValue();
        var expiry = now.AddMinutes(_options.RecoveryApprovalLifetimeMinutes);
        if (expiry > resolved.ExpiresAtUtc)
        {
            expiry = resolved.ExpiresAtUtc;
        }

        var activeApprovals = await _dbContext.RecoveryResetApprovals
            .Where(candidate =>
                candidate.CredentialMigrationContinuationId == resolved.Id &&
                candidate.RevokedAtUtc == null &&
                candidate.ConsumedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var activeApproval in activeApprovals)
        {
            activeApproval.Revoke(now);
        }

        _dbContext.RecoveryResetApprovals.Add(new RecoveryResetApproval(
            resolved.LocalAccountId,
            resolved.Id,
            request.ActorAccountId,
            request.Reason.Trim(),
            request.IdentityCheckEvidence.Trim(),
            RecoveryProofSecurity.Hash(authorizationBinding),
            now,
            expiry));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await RecordAuditAsync(
            RecoveryProofAuditCategory.AdminResetApprovalIssued,
            resolved.LocalAccountId,
            request.ActorAccountId,
            cancellationToken);
        return new ResetApprovalIssueResult(RecoveryProofOutcome.Success);
    }

    public async Task<RecoveryProofOutcome> ConsumeResetApprovalAsync(
        ResetApprovalConsumptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.RecoveryAdminAssistanceEnabled)
        {
            return RecoveryProofOutcome.Unavailable;
        }

        var continuation = await _store.ResolveContinuationAsync(
            request.Continuation,
            request.Context,
            cancellationToken);
        if (continuation is null)
        {
            return RecoveryProofOutcome.Missing;
        }

        var now = _store.UtcNow;
        var activeApprovals = await _dbContext.RecoveryResetApprovals
            .Where(candidate =>
                candidate.CredentialMigrationContinuationId == continuation.Id &&
                candidate.LocalAccountId == continuation.LocalAccountId &&
                candidate.RevokedAtUtc == null &&
                candidate.ConsumedAtUtc == null)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (activeApprovals.Count > 1)
        {
            return RecoveryProofOutcome.Missing;
        }

        if (activeApprovals.Count == 0)
        {
            var priorApprovalExists = await _dbContext.RecoveryResetApprovals.AsNoTracking()
                .AnyAsync(candidate =>
                    candidate.CredentialMigrationContinuationId == continuation.Id &&
                    candidate.LocalAccountId == continuation.LocalAccountId,
                    cancellationToken);
            return priorApprovalExists ? RecoveryProofOutcome.Replayed : RecoveryProofOutcome.Missing;
        }

        var existing = activeApprovals[0];
        if (existing.ExpiresAtUtc <= now)
        {
            return RecoveryProofOutcome.Expired;
        }

        if (!existing.TryConsume(now))
        {
            return RecoveryProofOutcome.Replayed;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await RecordAuditAsync(
                RecoveryProofAuditCategory.AdminResetApprovalConsumed,
                continuation.LocalAccountId,
                existing.ActorAccountId,
                cancellationToken);
            return RecoveryProofOutcome.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            return RecoveryProofOutcome.Replayed;
        }
    }

    public async Task<RecoveryProofOutcome> GetResetApprovalStatusAsync(
        ResetApprovalConsumptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.RecoveryAdminAssistanceEnabled)
        {
            return RecoveryProofOutcome.Unavailable;
        }

        var continuation = await _store.ResolveContinuationAsync(
            request.Continuation,
            request.Context,
            cancellationToken);
        if (continuation is null)
        {
            return RecoveryProofOutcome.Missing;
        }

        var activeApprovals = await _dbContext.RecoveryResetApprovals.AsNoTracking()
            .Where(candidate =>
                candidate.CredentialMigrationContinuationId == continuation.Id &&
                candidate.LocalAccountId == continuation.LocalAccountId &&
                candidate.RevokedAtUtc == null &&
                candidate.ConsumedAtUtc == null)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (activeApprovals.Count > 1)
        {
            return RecoveryProofOutcome.Missing;
        }

        if (activeApprovals.Count == 0)
        {
            var priorApprovalExists = await _dbContext.RecoveryResetApprovals.AsNoTracking()
                .AnyAsync(candidate =>
                    candidate.CredentialMigrationContinuationId == continuation.Id &&
                    candidate.LocalAccountId == continuation.LocalAccountId,
                    cancellationToken);
            return priorApprovalExists ? RecoveryProofOutcome.Replayed : RecoveryProofOutcome.Missing;
        }

        return activeApprovals[0].ExpiresAtUtc <= _store.UtcNow
            ? RecoveryProofOutcome.Expired
            : RecoveryProofOutcome.Success;
    }

    private async Task<AuthorizationResolution> AuthorizeAndResolveAsync(
        Guid actorAccountId,
        Guid targetAccountId,
        CancellationToken cancellationToken)
    {
        if (!_options.RecoveryAdminAssistanceEnabled)
        {
            return new AuthorizationResolution(RecoveryProofOutcome.Unavailable);
        }

        if (!await _authorizer.IsAdministratorAuthorizedAsync(actorAccountId, cancellationToken))
        {
            return new AuthorizationResolution(RecoveryProofOutcome.Unauthorized);
        }

        var resolved = await _store.ResolveUniqueActiveContinuationAsync(targetAccountId, cancellationToken);
        return resolved is null
            ? new AuthorizationResolution(RecoveryProofOutcome.Missing)
            : new AuthorizationResolution(RecoveryProofOutcome.Success, resolved);
    }

    private static bool HasRequiredEvidence(string identityCheckEvidence, string reason) =>
        !string.IsNullOrWhiteSpace(identityCheckEvidence) && identityCheckEvidence.Trim().Length <= 500 &&
        !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 500;

    private Task RecordAuditAsync(
        RecoveryProofAuditCategory category,
        Guid targetAccountId,
        Guid actorAccountId,
        CancellationToken cancellationToken) =>
        _audit.RecordAsync(
            new RecoveryProofAuditEvent(Guid.NewGuid(), category, targetAccountId, actorAccountId),
            cancellationToken);

    private sealed record AuthorizationResolution(
        RecoveryProofOutcome Outcome,
        ResolvedMigrationContinuation? Continuation = null);
}
