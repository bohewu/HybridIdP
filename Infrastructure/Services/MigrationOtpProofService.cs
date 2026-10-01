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
        TimeProvider? timeProvider = null,
        IRecoveryDestinationResolver? resolver = null,
        IOptions<RecoveryEmailSelectionOptions>? selectionOptions = null,
        IOptions<RecoveryIdentityVerificationOptions>? identityOptions = null)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _passwordHasher = passwordHasher;
        _options = options.Value;
        _store = new RecoveryProofStore(dbContext, timeProvider, resolver,
            selectionOptions?.Value.Enabled == true || identityOptions?.Value.Enabled == true);
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
        var binding = reservation.Challenge.SelectionEpoch is null ? null :
            await _store.CurrentMigrationBindingAsync(reservation.Challenge, cancellationToken);
        if (reservation.Challenge.SelectionEpoch is not null ? binding is null :
            await _store.RequiresSelectionAsync(continuation.LocalAccountId, cancellationToken))
            return new(RecoveryProofOutcome.Invalid);
        var boundCode = binding is null ? request.Code : RecoveryProofSecurity.BindToContext(request.Code, binding, string.Empty, string.Empty);
        if (user is null ||
            _passwordHasher.VerifyHashedPassword(user, reservation.Challenge.CodeHash, boundCode) == PasswordVerificationResult.Failed)
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
        var selectionRequired = await _store.RequiresSelectionAsync(continuation.LocalAccountId, cancellationToken);
        var destination = selectionRequired ? await _store.ResolveDestinationAsync(continuation.LocalAccountId, cancellationToken) : null;
        var binding = destination is null ? null : await _store.MigrationBindingAsync(continuation.LocalAccountId, continuation.Id, destination, cancellationToken);
        if (selectionRequired && (destination is null || binding is null)) return new(RecoveryProofOutcome.Unavailable);
        var recoveryEmail = await _dbContext.RecoveryEmails
            .SingleOrDefaultAsync(candidate =>
                candidate.LocalAccountId == continuation.LocalAccountId &&
                candidate.VerifiedAtUtc != null,
                cancellationToken);
        var user = await _dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == continuation.LocalAccountId, cancellationToken);
        if ((!selectionRequired && recoveryEmail is null) || user is null ||
            destination?.RecoveryEmailId is { } emailId && recoveryEmail?.Id != emailId)
        {
            return new MigrationOtpSendResult(RecoveryProofOutcome.Missing);
        }

        var now = _store.UtcNow;
        if (destination is { RecoveryEmailId: null }) recoveryEmail = null;
        if (recoveryEmail is not null && !recoveryEmail.TryReserveSend(
                now,
                now.AddSeconds(_options.RecoveryOtpResendCooldownSeconds)))
        {
            var retryAfter = Math.Max(
                1,
                (int)Math.Ceiling(((recoveryEmail.NextSendAllowedAtUtc ?? now) - now).TotalSeconds));
            return new MigrationOtpSendResult(RecoveryProofOutcome.Cooldown, retryAfter);
        }

        var code = RecoveryProofSecurity.GenerateNumericCode();
        var codeHash = _passwordHasher.HashPassword(user, binding is null ? code : RecoveryProofSecurity.BindToContext(code, binding, string.Empty, string.Empty));
        var expiry = now.AddMinutes(_options.RecoveryOtpLifetimeMinutes) <= continuation.ExpiresAtUtc
            ? now.AddMinutes(_options.RecoveryOtpLifetimeMinutes) : continuation.ExpiresAtUtc;
        var challenge = recoveryEmail is null ? RecoveryProofChallenge.CreateForDefault(continuation.LocalAccountId,
            codeHash, now, expiry, destination!.SelectionEpoch, destination.Fingerprint, destination.Version,
            RecoveryProofPurpose.MigrationOtp, continuation.Id) : new RecoveryProofChallenge(
            recoveryEmail.Id,
            continuation.LocalAccountId,
            RecoveryProofPurpose.MigrationOtp,
            codeHash,
            now,
            DateTimeOffset.Compare(
                now.AddMinutes(_options.RecoveryOtpLifetimeMinutes),
                continuation.ExpiresAtUtc) <= 0
                    ? now.AddMinutes(_options.RecoveryOtpLifetimeMinutes)
                    : continuation.ExpiresAtUtc,
            continuation.Id);
        if (recoveryEmail is not null && destination is not null)
            challenge.BindSelection(destination.SelectionEpoch, destination.Kind, destination.Fingerprint, destination.Version);

        await using (var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                if (recoveryEmail is null)
                {
                    // Serialize first sends on the existing continuation row. Keep the cooldown
                    // read and challenge insertion in the same transaction as this durable CAS.
                    var ticket = await _dbContext.CredentialMigrationContinuations.AsNoTracking()
                        .SingleAsync(t => t.Id == continuation.Id, cancellationToken);
                    if (ticket.ConsumedAtUtc is not null || ticket.ExpiresAtUtc <= now)
                        return new(RecoveryProofOutcome.Missing);
                    var reserved = await _dbContext.CredentialMigrationContinuations
                        .Where(t => t.Id == ticket.Id && t.Version == ticket.Version && t.ConsumedAtUtc == null)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.Version, t => t.Version + 1), cancellationToken);
                    if (reserved != 1)
                        return new(RecoveryProofOutcome.Cooldown, _options.RecoveryOtpResendCooldownSeconds);
                    var sendTimes = await _dbContext.RecoveryProofChallenges.AsNoTracking().Where(c =>
                        c.LocalAccountId == continuation.LocalAccountId && c.CredentialMigrationContinuationId == continuation.Id &&
                        c.Purpose == RecoveryProofPurpose.MigrationOtp).Select(c => c.SentAtUtc).ToListAsync(cancellationToken);
                    if (sendTimes.Any(sent => sent > now.AddSeconds(-_options.RecoveryOtpResendCooldownSeconds)))
                        return new(RecoveryProofOutcome.Cooldown, _options.RecoveryOtpResendCooldownSeconds);
                }
                await _dbContext.SaveChangesAsync(cancellationToken);
                var previous = await _dbContext.RecoveryProofChallenges.Where(c => c.LocalAccountId == continuation.LocalAccountId &&
                    c.Purpose == RecoveryProofPurpose.MigrationOtp && c.CredentialMigrationContinuationId == continuation.Id &&
                    c.RevokedAtUtc == null && c.ConsumedAtUtc == null).ToListAsync(cancellationToken);
                foreach (var old in previous) old.Revoke(now);
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
                destination?.Address ?? recoveryEmail!.Address,
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
