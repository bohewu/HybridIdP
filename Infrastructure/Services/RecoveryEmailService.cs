using Core.Application;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class RecoveryEmailService : IRecoveryEmailService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IRecoveryProofAuthorizer _authorizer;
    private readonly IEmailService _emailService;
    private readonly IPasswordHasher<ApplicationUser> _passwordHasher;
    private readonly IRecoveryProofAudit _audit;
    private readonly CredentialMigrationOptions _options;
    private readonly RecoveryProofStore _store;

    public RecoveryEmailService(
        ApplicationDbContext dbContext,
        IRecoveryProofAuthorizer authorizer,
        IEmailService emailService,
        IPasswordHasher<ApplicationUser> passwordHasher,
        IRecoveryProofAudit audit,
        IOptions<CredentialMigrationOptions> options,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _authorizer = authorizer;
        _emailService = emailService;
        _passwordHasher = passwordHasher;
        _audit = audit;
        _options = options.Value;
        _store = new RecoveryProofStore(dbContext, timeProvider);
    }

    public async Task<RecoveryEmailStatus> GetStatusAsync(
        Guid localAccountId,
        CancellationToken cancellationToken = default)
    {
        if (!_options.RecoveryEmailEnabled ||
            !await _authorizer.IsSelfServiceAuthorizedAsync(localAccountId, cancellationToken))
        {
            return new RecoveryEmailStatus(false, false);
        }

        var record = await _dbContext.RecoveryEmails.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.LocalAccountId == localAccountId, cancellationToken);
        return record is null
            ? new RecoveryEmailStatus(false, false)
            : new RecoveryEmailStatus(true, record.VerifiedAtUtc is not null, RecoveryProofSecurity.MaskAddress(record.Address));
    }

    public async Task<RecoveryEmailChangeResult> BeginAuthenticatedChangeAsync(
        RecoveryEmailChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.RecoveryEmailEnabled)
        {
            return new RecoveryEmailChangeResult(RecoveryProofOutcome.Unavailable);
        }

        if (!await _authorizer.IsSelfServiceAuthorizedAsync(request.LocalAccountId, cancellationToken))
        {
            return new RecoveryEmailChangeResult(RecoveryProofOutcome.Unauthorized);
        }

        var result = await BeginChangeCoreAsync(
            request.LocalAccountId,
            request.CandidateAddress,
            null,
            null,
            null,
            null,
            cancellationToken);
        if (result.Outcome == RecoveryProofOutcome.Success)
        {
            await _audit.RecordAsync(
                new RecoveryProofAuditEvent(
                    Guid.NewGuid(),
                    RecoveryProofAuditCategory.RecoveryAddressChangeStarted,
                    request.LocalAccountId,
                    request.LocalAccountId),
                cancellationToken);
        }

        return result;
    }

    public async Task<RecoveryProofOutcome> VerifyAuthenticatedAsync(
        RecoveryEmailVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.RecoveryEmailEnabled)
        {
            return RecoveryProofOutcome.Unavailable;
        }

        if (!await _authorizer.IsSelfServiceAuthorizedAsync(request.LocalAccountId, cancellationToken))
        {
            return RecoveryProofOutcome.Unauthorized;
        }

        return await VerifyAddressAsync(request.LocalAccountId, null, request.Code, cancellationToken);
    }

    public async Task<RecoveryProofOutcome> VerifyForMigrationAsync(
        MigrationRecoveryEmailVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.RecoveryEmailEnabled || !_options.MigrationEmailOtpEnabled)
        {
            return RecoveryProofOutcome.Unavailable;
        }

        var continuation = await _store.ResolveContinuationAsync(
            request.Continuation,
            request.Context,
            cancellationToken);
        return continuation is null
            ? RecoveryProofOutcome.Missing
            : await VerifyAddressAsync(
                continuation.LocalAccountId,
                continuation.Id,
                request.Code,
                cancellationToken);
    }

    public async Task<RecoveryProofOutcome> RevokeAuthenticatedAsync(
        Guid localAccountId,
        CancellationToken cancellationToken = default)
    {
        if (!_options.RecoveryEmailEnabled)
        {
            return RecoveryProofOutcome.Unavailable;
        }

        if (!await _authorizer.IsSelfServiceAuthorizedAsync(localAccountId, cancellationToken))
        {
            return RecoveryProofOutcome.Unauthorized;
        }

        var record = await _dbContext.RecoveryEmails
            .SingleOrDefaultAsync(candidate => candidate.LocalAccountId == localAccountId, cancellationToken);
        if (record is null)
        {
            return RecoveryProofOutcome.Missing;
        }

        var user = await _dbContext.Users
            .SingleAsync(candidate => candidate.Id == localAccountId, cancellationToken);
        _dbContext.RecoveryEmails.Remove(record);
        user.RecoverySourceBootstrapRevokedAtUtc = _store.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _audit.RecordAsync(
            new RecoveryProofAuditEvent(
                Guid.NewGuid(),
                RecoveryProofAuditCategory.RecoveryAddressRevoked,
                localAccountId,
                localAccountId),
            cancellationToken);
        return RecoveryProofOutcome.Success;
    }

    internal async Task<RecoveryEmailChangeResult> BeginAdministrativeReplacementAsync(
        ResolvedMigrationContinuation continuation,
        Guid actorAccountId,
        string candidateAddress,
        string identityCheckEvidence,
        string reason,
        CancellationToken cancellationToken) =>
        await BeginChangeCoreAsync(
            continuation.LocalAccountId,
            candidateAddress,
            continuation.Id,
            actorAccountId,
            reason,
            identityCheckEvidence,
            cancellationToken);

    private async Task<RecoveryEmailChangeResult> BeginChangeCoreAsync(
        Guid localAccountId,
        string candidateAddress,
        Guid? continuationId,
        Guid? actorAccountId,
        string? reason,
        string? identityCheckEvidence,
        CancellationToken cancellationToken)
    {
        if (!RecoveryProofSecurity.TryNormalizeAddress(candidateAddress, out var address, out var normalizedAddress))
        {
            return new RecoveryEmailChangeResult(RecoveryProofOutcome.Invalid);
        }

        var user = await _dbContext.Users.SingleOrDefaultAsync(candidate => candidate.Id == localAccountId, cancellationToken);
        if (user is null)
        {
            return new RecoveryEmailChangeResult(RecoveryProofOutcome.Unavailable);
        }

        var now = _store.UtcNow;
        var nextSend = now.AddSeconds(_options.RecoveryOtpResendCooldownSeconds);
        var code = RecoveryProofSecurity.GenerateNumericCode();
        var codeHash = _passwordHasher.HashPassword(user, code);
        RecoveryProofChallenge challenge;
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var record = await _dbContext.RecoveryEmails
                .SingleOrDefaultAsync(candidate => candidate.LocalAccountId == localAccountId, cancellationToken);
            if (record is null)
            {
                record = new RecoveryEmailRecord(localAccountId, address, normalizedAddress, now);
                if (actorAccountId is null)
                {
                    record.ReserveInitialSend(nextSend);
                }
                else
                {
                    record.ReplaceAddress(
                        address,
                        normalizedAddress,
                        now,
                        nextSend,
                        actorAccountId,
                        reason,
                        identityCheckEvidence);
                }
                _dbContext.RecoveryEmails.Add(record);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                await _store.RevokeChallengesAsync(record.Id, cancellationToken);
                record.ReplaceAddress(
                    address,
                    normalizedAddress,
                    now,
                    nextSend,
                    actorAccountId,
                    reason,
                    identityCheckEvidence);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            challenge = new RecoveryProofChallenge(
                record.Id,
                localAccountId,
                RecoveryProofPurpose.RecoveryAddressVerification,
                codeHash,
                now,
                now.AddMinutes(_options.RecoveryOtpLifetimeMinutes),
                continuationId);
            _dbContext.RecoveryProofChallenges.Add(challenge);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new RecoveryEmailChangeResult(RecoveryProofOutcome.Unavailable);
        }

        try
        {
            await _emailService.SendEmailAsync(
                address,
                "Verify recovery email",
                $"Your verification code is {code}. It expires in {_options.RecoveryOtpLifetimeMinutes} minutes.",
                false,
                cancellationToken);
            return new RecoveryEmailChangeResult(RecoveryProofOutcome.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await _store.MarkChallengeRevokedAsync(challenge.Id, cancellationToken);
            return new RecoveryEmailChangeResult(RecoveryProofOutcome.Unavailable);
        }
    }

    private async Task<RecoveryProofOutcome> VerifyAddressAsync(
        Guid localAccountId,
        Guid? continuationId,
        string code,
        CancellationToken cancellationToken)
    {
        var reservation = await _store.ReserveChallengeAttemptAsync(
            localAccountId,
            RecoveryProofPurpose.RecoveryAddressVerification,
            continuationId,
            _options.RecoveryOtpMaxAttempts,
            cancellationToken);
        if (reservation.Challenge is null)
        {
            return reservation.Outcome;
        }

        var user = await _dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == localAccountId, cancellationToken);
        if (user is null ||
            _passwordHasher.VerifyHashedPassword(user, reservation.Challenge.CodeHash, code) == PasswordVerificationResult.Failed)
        {
            if (reservation.Challenge.VerificationAttempts >= _options.RecoveryOtpMaxAttempts)
            {
                await _store.MarkChallengeRevokedAsync(reservation.Challenge.Id, cancellationToken);
                return RecoveryProofOutcome.Exhausted;
            }

            return RecoveryProofOutcome.Invalid;
        }

        var outcome = await _store.CompleteAddressVerificationAsync(reservation.Challenge, cancellationToken);
        if (outcome == RecoveryProofOutcome.Success)
        {
            await _audit.RecordAsync(
                new RecoveryProofAuditEvent(
                    Guid.NewGuid(),
                    RecoveryProofAuditCategory.RecoveryAddressVerified,
                    localAccountId),
                cancellationToken);
        }

        return outcome;
    }
}
