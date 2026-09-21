using Core.Application.Ports;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

internal sealed class RecoveryProofStore
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public RecoveryProofStore(ApplicationDbContext dbContext, TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    public async Task<ResolvedMigrationContinuation?> ResolveContinuationAsync(
        string continuation,
        MigrationContinuationContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(continuation) ||
            string.IsNullOrWhiteSpace(context.ContextHash) ||
            string.IsNullOrWhiteSpace(context.CsrfHash))
        {
            return null;
        }

        var tokenHash = RecoveryProofSecurity.Hash(continuation);
        var ticket = await _dbContext.CredentialMigrationContinuations.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == tokenHash, cancellationToken);
        if (ticket is null || ticket.ConsumedAtUtc is not null || ticket.ExpiresAtUtc <= UtcNow ||
            ticket.ContextHash != context.ContextHash || ticket.CsrfHash != context.CsrfHash)
        {
            return null;
        }

        var state = await _dbContext.CredentialMigrationStateRecords.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == ticket.CredentialMigrationStateRecordId, cancellationToken);
        return state is
        {
            State: CredentialMigrationState.ProofValidated,
            EffectiveEmailOtpRequirement: EffectiveEmailOtpRequirement.Required
        }
            ? new ResolvedMigrationContinuation(ticket.Id, state.LocalAccountId, ticket.ExpiresAtUtc)
            : null;
    }

    public async Task<ResolvedMigrationContinuation?> ResolveUniqueActiveContinuationAsync(
        Guid localAccountId,
        CancellationToken cancellationToken)
    {
        if (localAccountId == Guid.Empty)
        {
            return null;
        }

        var now = UtcNow;
        var state = await _dbContext.CredentialMigrationStateRecords.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.LocalAccountId == localAccountId, cancellationToken);
        if (state is not
            {
                State: CredentialMigrationState.ProofValidated,
                EffectiveEmailOtpRequirement: EffectiveEmailOtpRequirement.Required
            })
        {
            return null;
        }

        var unconsumed = await _dbContext.CredentialMigrationContinuations.AsNoTracking()
            .Where(ticket =>
                ticket.CredentialMigrationStateRecordId == state.Id &&
                ticket.ConsumedAtUtc == null)
            .Select(ticket => new ResolvedMigrationContinuation(ticket.Id, state.LocalAccountId, ticket.ExpiresAtUtc))
            .ToListAsync(cancellationToken);
        var candidates = unconsumed.Where(ticket => ticket.ExpiresAtUtc > now).Take(2).ToList();

        return candidates.Count == 1 ? candidates[0] : null;
    }

    public async Task<ChallengeReservation> ReserveChallengeAttemptAsync(
        Guid localAccountId,
        RecoveryProofPurpose purpose,
        Guid? continuationId,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        var now = UtcNow;
        var candidates = await _dbContext.RecoveryProofChallenges
            .Where(challenge =>
                challenge.LocalAccountId == localAccountId &&
                challenge.Purpose == purpose &&
                challenge.CredentialMigrationContinuationId == continuationId &&
                challenge.RevokedAtUtc == null &&
                challenge.ConsumedAtUtc == null &&
                challenge.VerifiedAtUtc == null)
            .ToListAsync(cancellationToken);
        var challenge = candidates.OrderByDescending(candidate => candidate.CreatedAtUtc).FirstOrDefault();
        if (challenge is null || !challenge.TryReserveAttempt(now, maxAttempts))
        {
            return new ChallengeReservation(
                await ClassifyChallengeAsync(localAccountId, purpose, continuationId, maxAttempts, cancellationToken));
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new ChallengeReservation(RecoveryProofOutcome.Success, challenge);
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            return new ChallengeReservation(
                await ClassifyChallengeAsync(localAccountId, purpose, continuationId, maxAttempts, cancellationToken));
        }
    }

    public async Task<ChallengeReservation> ReserveChallengeAttemptAsync(
        Guid challengeId,
        RecoveryProofPurpose purpose,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        var challenge = await _dbContext.RecoveryProofChallenges
            .SingleOrDefaultAsync(candidate => candidate.Id == challengeId && candidate.Purpose == purpose, cancellationToken);
        if (challenge is null || !challenge.TryReserveAttempt(UtcNow, maxAttempts))
        {
            return new ChallengeReservation(RecoveryProofOutcome.Invalid);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new ChallengeReservation(RecoveryProofOutcome.Success, challenge);
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            return new ChallengeReservation(RecoveryProofOutcome.Invalid);
        }
    }

    public async Task RevokeChallengesAsync(Guid recoveryEmailId, CancellationToken cancellationToken)
    {
        var challenges = await _dbContext.RecoveryProofChallenges
            .Where(challenge =>
                challenge.RecoveryEmailId == recoveryEmailId &&
                challenge.RevokedAtUtc == null &&
                challenge.ConsumedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var challenge in challenges)
        {
            challenge.Revoke(UtcNow);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeChallengesAsync(
        Guid recoveryEmailId,
        RecoveryProofPurpose purpose,
        CancellationToken cancellationToken)
    {
        var challenges = await _dbContext.RecoveryProofChallenges
            .Where(challenge =>
                challenge.RecoveryEmailId == recoveryEmailId &&
                challenge.Purpose == purpose &&
                challenge.RevokedAtUtc == null &&
                challenge.ConsumedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var challenge in challenges)
        {
            challenge.Revoke(UtcNow);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<RecoveryProofOutcome> CompleteAddressVerificationAsync(
        RecoveryProofChallenge challenge,
        CancellationToken cancellationToken)
    {
        var now = UtcNow;
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var email = await _dbContext.RecoveryEmails
            .SingleOrDefaultAsync(candidate => candidate.Id == challenge.RecoveryEmailId, cancellationToken);
        if (email is null || !challenge.TryMarkAddressVerified(now))
        {
            await transaction.RollbackAsync(cancellationToken);
            return RecoveryProofOutcome.Replayed;
        }

        email.MarkVerified(now);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RecoveryProofOutcome.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
            return RecoveryProofOutcome.Replayed;
        }
    }

    public async Task<MigrationOtpVerificationResult> CompleteMigrationVerificationAsync(
        RecoveryProofChallenge challenge,
        CancellationToken cancellationToken)
    {
        var now = UtcNow;
        var proof = RecoveryProofSecurity.GenerateOpaqueValue();
        var proofHash = RecoveryProofSecurity.Hash(proof);
        if (!challenge.TryMarkMigrationVerified(proofHash, now))
        {
            return new MigrationOtpVerificationResult(RecoveryProofOutcome.Replayed);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new MigrationOtpVerificationResult(RecoveryProofOutcome.Success, proof);
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            return new MigrationOtpVerificationResult(RecoveryProofOutcome.Replayed);
        }
    }

    public async Task<NativeRecoveryVerificationResult> CompleteNativeRecoveryVerificationAsync(
        RecoveryProofChallenge challenge,
        string proofBinding,
        string contextHash,
        string csrfHash,
        bool promoteRecoveryEmail,
        CancellationToken cancellationToken)
    {
        await using var transaction = promoteRecoveryEmail
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var email = promoteRecoveryEmail
            ? await _dbContext.RecoveryEmails.SingleOrDefaultAsync(
                candidate => candidate.Id == challenge.RecoveryEmailId &&
                    candidate.LocalAccountId == challenge.LocalAccountId,
                cancellationToken)
            : null;
        var now = UtcNow;
        if (promoteRecoveryEmail && (email is null || email.VerifiedAtUtc is not null))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            return new NativeRecoveryVerificationResult(NativeRecoveryVerificationOutcome.Denied);
        }

        email?.MarkVerified(now);
        if (email is not null)
        {
            if (!string.IsNullOrWhiteSpace(challenge.NativeContextHash) &&
                !string.IsNullOrWhiteSpace(challenge.NativeCsrfHash) &&
                !string.IsNullOrWhiteSpace(challenge.NativeSecurityStamp) &&
                challenge.NativeDirectoryAuthority is not null)
            {
                challenge.BindNativeAssistance(
                    challenge.NativeContextHash,
                    challenge.NativeCsrfHash,
                    challenge.NativeDirectoryAuthority.Value,
                    challenge.NativeDirectoryObjectId,
                    email.Version,
                    challenge.NativeSecurityStamp);
            }
            var emailBinding =
                $"{email.Id:N}:{email.LocalAccountId:N}:{email.Version}:{email.NormalizedAddress}";
            proofBinding = RecoveryProofSecurity.BindToContext(
                string.Empty,
                contextHash,
                csrfHash,
                emailBinding);
        }

        var proof = RecoveryProofSecurity.GenerateOpaqueValue();
        var proofHash = RecoveryProofSecurity.Hash(
            RecoveryProofSecurity.BindToContext(proof, string.Empty, string.Empty, proofBinding));
        if (!challenge.TryMarkNativeRecoveryVerified(proofHash, now))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();
            }
            return new NativeRecoveryVerificationResult(NativeRecoveryVerificationOutcome.Denied);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            return new NativeRecoveryVerificationResult(NativeRecoveryVerificationOutcome.Verified, proof);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            _dbContext.ChangeTracker.Clear();
            return new NativeRecoveryVerificationResult(NativeRecoveryVerificationOutcome.Denied);
        }
    }

    public async Task<RecoveryProofOutcome> ConsumeMigrationProofAsync(
        Guid continuationId,
        string proof,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(proof))
        {
            return RecoveryProofOutcome.Missing;
        }

        var now = UtcNow;
        var proofHash = RecoveryProofSecurity.Hash(proof);
        var existing = await _dbContext.RecoveryProofChallenges
            .SingleOrDefaultAsync(challenge => challenge.ProofTokenHash == proofHash, cancellationToken);
        if (existing is null || existing.CredentialMigrationContinuationId != continuationId ||
            existing.Purpose != RecoveryProofPurpose.MigrationOtp)
        {
            return RecoveryProofOutcome.Invalid;
        }

        if (existing.ConsumedAtUtc is not null || existing.RevokedAtUtc is not null)
        {
            return RecoveryProofOutcome.Replayed;
        }

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
            return RecoveryProofOutcome.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            return RecoveryProofOutcome.Replayed;
        }
    }

    public async Task MarkChallengeRevokedAsync(Guid challengeId, CancellationToken cancellationToken)
    {
        var challenge = await _dbContext.RecoveryProofChallenges
            .SingleOrDefaultAsync(candidate => candidate.Id == challengeId, cancellationToken);
        if (challenge is not null)
        {
            challenge.Revoke(UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<RecoveryProofOutcome> ClassifyChallengeAsync(
        Guid localAccountId,
        RecoveryProofPurpose purpose,
        Guid? continuationId,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        var challenges = await _dbContext.RecoveryProofChallenges.AsNoTracking()
            .Where(candidate =>
                candidate.LocalAccountId == localAccountId &&
                candidate.Purpose == purpose &&
                candidate.CredentialMigrationContinuationId == continuationId)
            .ToListAsync(cancellationToken);
        var challenge = challenges.OrderByDescending(candidate => candidate.CreatedAtUtc).FirstOrDefault();
        if (challenge is null || challenge.RevokedAtUtc is not null)
        {
            return RecoveryProofOutcome.Missing;
        }

        if (challenge.ConsumedAtUtc is not null || challenge.VerifiedAtUtc is not null)
        {
            return RecoveryProofOutcome.Replayed;
        }

        if (challenge.ExpiresAtUtc <= UtcNow)
        {
            return RecoveryProofOutcome.Expired;
        }

        return challenge.VerificationAttempts >= maxAttempts
            ? RecoveryProofOutcome.Exhausted
            : RecoveryProofOutcome.Unavailable;
    }
}

internal sealed record ResolvedMigrationContinuation(Guid Id, Guid LocalAccountId, DateTimeOffset ExpiresAtUtc);
internal sealed record ChallengeReservation(RecoveryProofOutcome Outcome, RecoveryProofChallenge? Challenge = null);
