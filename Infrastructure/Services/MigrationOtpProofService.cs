using Core.Application;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class MigrationOtpProofService : IMigrationOtpProofService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEmailService _emailService;
    private readonly IPasswordHasher<ApplicationUser> _passwordHasher;
    private readonly CredentialMigrationOptions _options;
    private readonly RecoveryProofStore _store;

    public MigrationOtpProofService(
        ApplicationDbContext dbContext,
        IEmailService emailService,
        IPasswordHasher<ApplicationUser> passwordHasher,
        IOptions<CredentialMigrationOptions> options,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _passwordHasher = passwordHasher;
        _options = options.Value;
        _store = new RecoveryProofStore(dbContext, timeProvider);
    }

    public async Task<MigrationOtpSendResult> SendAsync(
        MigrationOtpSendRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.RecoveryEmailEnabled || !_options.MigrationEmailOtpEnabled)
        {
            return new MigrationOtpSendResult(RecoveryProofOutcome.Unavailable);
        }

        var continuation = await _store.ResolveContinuationAsync(
            request.Continuation,
            request.Context,
            cancellationToken);
        return continuation is null
            ? new MigrationOtpSendResult(RecoveryProofOutcome.Missing)
            : await SendCoreAsync(continuation, cancellationToken);
    }

    public async Task<MigrationOtpVerificationResult> VerifyAsync(
        MigrationOtpVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.RecoveryEmailEnabled || !_options.MigrationEmailOtpEnabled)
        {
            return new MigrationOtpVerificationResult(RecoveryProofOutcome.Unavailable);
        }

        var continuation = await _store.ResolveContinuationAsync(
            request.Continuation,
            request.Context,
            cancellationToken);
        if (continuation is null)
        {
            return new MigrationOtpVerificationResult(RecoveryProofOutcome.Missing);
        }

        var reservation = await _store.ReserveChallengeAttemptAsync(
            continuation.LocalAccountId,
            RecoveryProofPurpose.MigrationOtp,
            continuation.Id,
            _options.RecoveryOtpMaxAttempts,
            cancellationToken);
        if (reservation.Challenge is null)
        {
            return new MigrationOtpVerificationResult(reservation.Outcome);
        }

        var user = await _dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == continuation.LocalAccountId, cancellationToken);
        if (user is null ||
            _passwordHasher.VerifyHashedPassword(user, reservation.Challenge.CodeHash, request.Code) == PasswordVerificationResult.Failed)
        {
            if (reservation.Challenge.VerificationAttempts >= _options.RecoveryOtpMaxAttempts)
            {
                await _store.MarkChallengeRevokedAsync(reservation.Challenge.Id, cancellationToken);
                return new MigrationOtpVerificationResult(RecoveryProofOutcome.Exhausted);
            }

            return new MigrationOtpVerificationResult(RecoveryProofOutcome.Invalid);
        }

        return await _store.CompleteMigrationVerificationAsync(reservation.Challenge, cancellationToken);
    }

    public async Task<RecoveryProofOutcome> ConsumeAsync(
        MigrationOtpConsumptionRequest request,
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
            : await _store.ConsumeMigrationProofAsync(continuation.Id, request.Proof, cancellationToken);
    }

    internal async Task<MigrationOtpSendResult> SendForAdministratorAsync(
        ResolvedMigrationContinuation continuation,
        CancellationToken cancellationToken) =>
        await SendCoreAsync(continuation, cancellationToken);

    private async Task<MigrationOtpSendResult> SendCoreAsync(
        ResolvedMigrationContinuation continuation,
        CancellationToken cancellationToken)
    {
        var recoveryEmail = await _dbContext.RecoveryEmails
            .SingleOrDefaultAsync(candidate =>
                candidate.LocalAccountId == continuation.LocalAccountId &&
                candidate.VerifiedAtUtc != null,
                cancellationToken);
        var user = await _dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == continuation.LocalAccountId, cancellationToken);
        if (recoveryEmail is null || user is null)
        {
            return new MigrationOtpSendResult(RecoveryProofOutcome.Missing);
        }

        var now = _store.UtcNow;
        if (!recoveryEmail.TryReserveSend(
                now,
                now.AddSeconds(_options.RecoveryOtpResendCooldownSeconds)))
        {
            var retryAfter = Math.Max(
                1,
                (int)Math.Ceiling(((recoveryEmail.NextSendAllowedAtUtc ?? now) - now).TotalSeconds));
            return new MigrationOtpSendResult(RecoveryProofOutcome.Cooldown, retryAfter);
        }

        var code = RecoveryProofSecurity.GenerateNumericCode();
        var challenge = new RecoveryProofChallenge(
            recoveryEmail.Id,
            continuation.LocalAccountId,
            RecoveryProofPurpose.MigrationOtp,
            _passwordHasher.HashPassword(user, code),
            now,
            DateTimeOffset.Compare(
                now.AddMinutes(_options.RecoveryOtpLifetimeMinutes),
                continuation.ExpiresAtUtc) <= 0
                    ? now.AddMinutes(_options.RecoveryOtpLifetimeMinutes)
                    : continuation.ExpiresAtUtc,
            continuation.Id);

        await using (var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                await _store.RevokeChallengesAsync(recoveryEmail.Id, cancellationToken);
                _dbContext.RecoveryProofChallenges.Add(challenge);
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();
                return new MigrationOtpSendResult(RecoveryProofOutcome.Cooldown, _options.RecoveryOtpResendCooldownSeconds);
            }
        }

        try
        {
            await _emailService.SendEmailAsync(
                recoveryEmail.Address,
                "Credential migration verification",
                $"Your verification code is {code}. It expires in {_options.RecoveryOtpLifetimeMinutes} minutes.",
                false,
                cancellationToken);
            return new MigrationOtpSendResult(RecoveryProofOutcome.Success, _options.RecoveryOtpResendCooldownSeconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await _store.MarkChallengeRevokedAsync(challenge.Id, cancellationToken);
            return new MigrationOtpSendResult(RecoveryProofOutcome.Unavailable);
        }
    }
}
