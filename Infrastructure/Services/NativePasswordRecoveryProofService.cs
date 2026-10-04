using Core.Application;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class NativePasswordRecoveryProofService : INativePasswordRecoveryProofService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEmailService _emailService;
    private readonly IPasswordHasher<ApplicationUser> _passwordHasher;
    private readonly ILookupNormalizer _normalizer;
    private readonly IRecoveryVerificationPolicyEvaluator _policyEvaluator;
    private readonly IForgotPasswordRoutingEvaluator _routingEvaluator;
    private readonly ForgotPasswordRecoveryOptions _options;
    private readonly RecoveryProofStore _store;

    public NativePasswordRecoveryProofService(
        ApplicationDbContext dbContext,
        IEmailService emailService,
        IPasswordHasher<ApplicationUser> passwordHasher,
        ILookupNormalizer normalizer,
        IRecoveryVerificationPolicyEvaluator policyEvaluator,
        IForgotPasswordRoutingEvaluator routingEvaluator,
        IOptions<ForgotPasswordRecoveryOptions> options,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _passwordHasher = passwordHasher;
        _normalizer = normalizer;
        _policyEvaluator = policyEvaluator;
        _routingEvaluator = routingEvaluator;
        _options = options.Value;
        _store = new RecoveryProofStore(dbContext, timeProvider);
    }

    public async Task<NativeRecoveryStartResult> StartAsync(
        NativeRecoveryStartRequest request,
        CancellationToken cancellationToken = default)
    {
        var publicRequestId = Guid.NewGuid();
        if (!IsNativeRecoveryPermitted() || !IsValidIdentifier(request.Identifier) || !IsValidContext(request.Context))
        {
            return new NativeRecoveryStartResult(publicRequestId);
        }

        try
        {
            if (!await IsRuntimeNativeAsync(cancellationToken))
            {
                return new NativeRecoveryStartResult(publicRequestId);
            }

            var user = await ResolveEligibleAccountAsync(request.Identifier, cancellationToken);
            if (user is null)
            {
                return new NativeRecoveryStartResult(publicRequestId);
            }

            var authority = await ResolveAuthorityAsync(user, cancellationToken);
            if (authority is null)
            {
                return new NativeRecoveryStartResult(publicRequestId);
            }

            var decision = await _policyEvaluator.EvaluateAsync(user.Id, cancellationToken);
            var sourceBootstrap = IsSourceBootstrapDestination(decision);
            if (!IsVerifiedLocalDestination(decision) && !sourceBootstrap)
            {
                return new NativeRecoveryStartResult(publicRequestId);
            }

            RecoveryProofChallenge challenge;
            string address;
            string code;
            await using (var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken))
            {
                var recoveryEmail = await _dbContext.RecoveryEmails
                    .SingleOrDefaultAsync(candidate => candidate.LocalAccountId == user.Id, cancellationToken);
                var now = _store.UtcNow;
                var sourceEmailCreated = false;
                if (recoveryEmail is null && sourceBootstrap &&
                    RecoveryProofSecurity.TryNormalizeAddress(
                        decision.RecoveryEmail.Address ?? string.Empty,
                        out var sourceAddress,
                        out var normalizedSourceAddress))
                {
                    recoveryEmail = new RecoveryEmailRecord(
                        user.Id,
                        sourceAddress,
                        normalizedSourceAddress,
                        now);
                    recoveryEmail.ReserveInitialSend(now.AddSeconds(_options.NativeOtpResendCooldownSeconds));
                    _dbContext.RecoveryEmails.Add(recoveryEmail);
                    sourceEmailCreated = true;
                }
                else if (recoveryEmail is null ||
                    !string.Equals(recoveryEmail.Address, decision.RecoveryEmail.Address, StringComparison.OrdinalIgnoreCase))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new NativeRecoveryStartResult(publicRequestId);
                }

                if (recoveryEmail.VerifiedAtUtc is not null)
                {
                    if (!recoveryEmail.TryReserveSend(now, now.AddSeconds(_options.NativeOtpResendCooldownSeconds)))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return new NativeRecoveryStartResult(publicRequestId);
                    }
                }
                else if (!sourceBootstrap ||
                    !sourceEmailCreated && recoveryEmail.NextSendAllowedAtUtc > now)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new NativeRecoveryStartResult(publicRequestId);
                }
                else if (!sourceEmailCreated)
                {
                    recoveryEmail.ReserveInitialSend(now.AddSeconds(_options.NativeOtpResendCooldownSeconds));
                }

                code = RecoveryProofSecurity.GenerateNumericCode();
                var emailBinding = CreateRecoveryEmailBinding(recoveryEmail);
                var codeHash = _passwordHasher.HashPassword(
                    user,
                    RecoveryProofSecurity.BindToContext(
                        code,
                        request.Context.ContextHash,
                        request.Context.CsrfHash,
                        emailBinding));
                challenge = new RecoveryProofChallenge(
                    recoveryEmail.Id,
                    user.Id,
                    RecoveryProofPurpose.NativePasswordRecovery,
                    codeHash,
                    now,
                    now.AddMinutes(_options.NativeOtpLifetimeMinutes));
                if (!string.IsNullOrWhiteSpace(user.SecurityStamp))
                {
                    challenge.BindNativeAssistance(
                        request.Context.ContextHash,
                        request.Context.CsrfHash,
                        authority.IsDirectory,
                        authority.IsDirectory ? authority.DirectoryObjectId : null,
                        recoveryEmail.Version,
                        user.SecurityStamp);
                }

                await _store.RevokeChallengesAsync(
                    recoveryEmail.Id,
                    RecoveryProofPurpose.NativePasswordRecovery,
                    cancellationToken);
                _dbContext.RecoveryProofChallenges.Add(challenge);
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                address = recoveryEmail.Address;
            }

            try
            {
                await _emailService.SendEmailAsync(
                    address,
                    "Password recovery verification",
                    $"Your verification code is {code}. It expires in {_options.NativeOtpLifetimeMinutes} minutes.",
                    false,
                    cancellationToken);
                return new NativeRecoveryStartResult(challenge.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                await _store.MarkChallengeRevokedAsync(challenge.Id, cancellationToken);
                return new NativeRecoveryStartResult(publicRequestId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new NativeRecoveryStartResult(publicRequestId);
        }
    }

    public async Task<NativeRecoveryVerificationResult> VerifyAsync(
        NativeRecoveryVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsNativeRecoveryPermitted() || request.RequestId == Guid.Empty ||
            request.Code.Length != 6 || request.Code.Any(character => !char.IsAsciiDigit(character)) ||
            !IsValidContext(request.Context))
        {
            return Denied();
        }

        try
        {
            if (!await IsRuntimeNativeAsync(cancellationToken))
            {
                return Denied();
            }

            var reservation = await _store.ReserveChallengeAttemptAsync(
                request.RequestId,
                RecoveryProofPurpose.NativePasswordRecovery,
                _options.NativeOtpMaxAttempts,
                cancellationToken);
            if (reservation.Challenge is null)
            {
                return Denied();
            }

            var challenge = reservation.Challenge;
            var user = await _dbContext.Users.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == challenge.LocalAccountId, cancellationToken);
            var recoveryEmail = await _dbContext.RecoveryEmails.AsNoTracking()
                .SingleOrDefaultAsync(candidate =>
                    candidate.Id == challenge.RecoveryEmailId &&
                    candidate.LocalAccountId == challenge.LocalAccountId,
                    cancellationToken);
            if (user is null || recoveryEmail is null ||
                !await IsStillEligibleAccountAsync(user, cancellationToken))
            {
                await _store.MarkChallengeRevokedAsync(challenge.Id, cancellationToken);
                return Denied();
            }

            var decision = await _policyEvaluator.EvaluateAsync(user.Id, cancellationToken);
            var promoteSourceEmail = recoveryEmail.VerifiedAtUtc is null;
            if (!(promoteSourceEmail
                    ? IsSourceBootstrapDestination(decision)
                    : IsVerifiedLocalDestination(decision)) ||
                !string.Equals(recoveryEmail.Address, decision.RecoveryEmail.Address, StringComparison.OrdinalIgnoreCase))
            {
                await _store.MarkChallengeRevokedAsync(challenge.Id, cancellationToken);
                return Denied();
            }

            var emailBinding = CreateRecoveryEmailBinding(recoveryEmail);
            var boundCode = RecoveryProofSecurity.BindToContext(
                request.Code,
                request.Context.ContextHash,
                request.Context.CsrfHash,
                emailBinding);
            if (_passwordHasher.VerifyHashedPassword(user, challenge.CodeHash, boundCode) == PasswordVerificationResult.Failed)
            {
                if (challenge.VerificationAttempts >= _options.NativeOtpMaxAttempts)
                {
                    await _store.MarkChallengeRevokedAsync(challenge.Id, cancellationToken);
                }

                return Denied();
            }

            var proofBinding = RecoveryProofSecurity.BindToContext(
                string.Empty,
                request.Context.ContextHash,
                request.Context.CsrfHash,
                emailBinding);
            return await _store.CompleteNativeRecoveryVerificationAsync(
                challenge,
                proofBinding,
                request.Context.ContextHash,
                request.Context.CsrfHash,
                promoteSourceEmail,
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
    }

    private async Task<ApplicationUser?> ResolveEligibleAccountAsync(
        string identifier,
        CancellationToken cancellationToken)
    {
        var normalizedName = _normalizer.NormalizeName(identifier.Trim());
        var normalizedEmail = _normalizer.NormalizeEmail(identifier.Trim());
        var users = await _dbContext.Users.AsNoTracking()
            .Where(candidate =>
                candidate.NormalizedUserName == normalizedName ||
                candidate.NormalizedEmail == normalizedEmail)
            .Take(2)
            .ToListAsync(cancellationToken);
        return users.Count == 1 && await IsStillEligibleAccountAsync(users[0], cancellationToken)
            ? users[0]
            : null;
    }

    private async Task<bool> IsStillEligibleAccountAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
        => await ResolveAuthorityAsync(user, cancellationToken) is not null;

    private async Task<RecoveryAuthority?> ResolveAuthorityAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        if (!user.IsActive || user.IsDeleted ||
            user.LockoutEnabled && user.LockoutEnd is { } lockoutEnd && lockoutEnd > _store.UtcNow)
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
        var isLocal = binding is null && migration is null && !string.IsNullOrWhiteSpace(user.PasswordHash);
        var isCompletedDirectory = _options.NativeDirectoryRecoveryEnabled &&
            binding is not null && migration is not null &&
            migration.State == CredentialMigrationState.LocalFinalized &&
            migration.ProviderSubjectDirectoryBindingId == binding.Id &&
            binding.LocalAccountId == user.Id;
        RecoveryAuthority? authority = isLocal
            ? new RecoveryAuthority(false, Guid.Empty)
            : isCompletedDirectory
                ? new RecoveryAuthority(true, binding!.DirectoryObjectId)
                : null;
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

    private static bool IsVerifiedLocalDestination(RecoveryVerificationPolicyDecision decision) =>
        decision.Enabled &&
        decision.RecoveryEmail.AddressSource == RecoveryEmailAddressSource.LocalRecord &&
        decision.RecoveryEmail.TrustOrigin == RecoveryEmailPolicyTrustOrigin.LocallyVerified &&
        decision.RecoveryEmail.CanReceiveRecoveryOtp &&
        !string.IsNullOrWhiteSpace(decision.RecoveryEmail.Address);

    private static bool IsSourceBootstrapDestination(RecoveryVerificationPolicyDecision decision) =>
        decision.Enabled &&
        decision.BootstrapActive &&
        (decision.RecoveryEmail.AddressSource is RecoveryEmailAddressSource.ProviderSnapshot or
            RecoveryEmailAddressSource.LocalRecord) &&
        (decision.RecoveryEmail.TrustOrigin is RecoveryEmailPolicyTrustOrigin.SourceVerified or
            RecoveryEmailPolicyTrustOrigin.PolicyTrusted) &&
        decision.RecoveryEmail.CanReceiveRecoveryOtp &&
        decision.RecoveryEmail.HasAcceptedSourceTrustForAddress &&
        !decision.RecoveryEmail.HasSourceConflict &&
        !string.IsNullOrWhiteSpace(decision.RecoveryEmail.Address);

    private bool IsNativeRecoveryPermitted() =>
        _options.NativeRecoveryEnabled && _options.DeploymentCeiling == ForgotPasswordMode.Native;

    private async Task<bool> IsRuntimeNativeAsync(CancellationToken cancellationToken)
    {
        var runtimeMode = await _dbContext.SecurityPolicies.AsNoTracking()
            .OrderBy(policy => policy.Id)
            .Select(policy => policy.ForgotPasswordMode)
            .FirstOrDefaultAsync(cancellationToken);
        var routing = _routingEvaluator.Evaluate(runtimeMode, null);
        return routing.PermittedMode == ForgotPasswordMode.Native;
    }

    private static string CreateRecoveryEmailBinding(RecoveryEmailRecord recoveryEmail) =>
        $"{recoveryEmail.Id:N}:{recoveryEmail.LocalAccountId:N}:{recoveryEmail.Version}:{recoveryEmail.NormalizedAddress}";

    private static bool IsValidIdentifier(string identifier) =>
        !string.IsNullOrWhiteSpace(identifier) && identifier.Length <= 320;

    private static bool IsValidContext(NativeRecoveryContext context) =>
        !string.IsNullOrWhiteSpace(context.ContextHash) && context.ContextHash.Length <= 256 &&
        !string.IsNullOrWhiteSpace(context.CsrfHash) && context.CsrfHash.Length <= 256;

    private static NativeRecoveryVerificationResult Denied() =>
        new(NativeRecoveryVerificationOutcome.Denied);

    private sealed record RecoveryAuthority(bool IsDirectory, Guid DirectoryObjectId);
}
