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

public sealed class NativeRecoveryAssistanceService : INativeRecoveryAssistanceService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IRecoveryProofAuthorizer _authorizer;
    private readonly IRecoveryVerificationPolicyEvaluator _policyEvaluator;
    private readonly IForgotPasswordRoutingEvaluator _routingEvaluator;
    private readonly IEmailService _emailService;
    private readonly IPasswordHasher<ApplicationUser> _passwordHasher;
    private readonly IRecoveryProofAudit _audit;
    private readonly ForgotPasswordRecoveryOptions _options;
    private readonly RecoveryProofStore _store;

    public NativeRecoveryAssistanceService(
        ApplicationDbContext dbContext,
        IRecoveryProofAuthorizer authorizer,
        IRecoveryVerificationPolicyEvaluator policyEvaluator,
        IForgotPasswordRoutingEvaluator routingEvaluator,
        IEmailService emailService,
        IPasswordHasher<ApplicationUser> passwordHasher,
        IRecoveryProofAudit audit,
        IOptions<ForgotPasswordRecoveryOptions> options,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _authorizer = authorizer;
        _policyEvaluator = policyEvaluator;
        _routingEvaluator = routingEvaluator;
        _emailService = emailService;
        _passwordHasher = passwordHasher;
        _audit = audit;
        _options = options.Value;
        _store = new RecoveryProofStore(dbContext, timeProvider);
    }

    public async Task<NativeRecoveryAssistanceResult> ResendAsync(
        AdminNativeRecoveryResendRequest request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(request.ActorAccountId, cancellationToken);
        if (authorization != RecoveryProofOutcome.Success)
        {
            return new NativeRecoveryAssistanceResult(authorization);
        }

        RecoveryProofChallenge? challenge = null;
        string? address = null;
        string? code = null;
        try
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var resolved = await ResolveActiveChallengeAsync(request.TargetAccountId, true, cancellationToken);
            if (resolved is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Unavailable();
            }

            var now = _store.UtcNow;
            if (!resolved.Email.TryReserveSend(now, now.AddSeconds(_options.NativeOtpResendCooldownSeconds)))
            {
                await transaction.RollbackAsync(cancellationToken);
                var retryAfter = Math.Max(1, (int)Math.Ceiling(
                    ((resolved.Email.NextSendAllowedAtUtc ?? now) - now).TotalSeconds));
                return new NativeRecoveryAssistanceResult(RecoveryProofOutcome.Cooldown, retryAfter);
            }

            code = RecoveryProofSecurity.GenerateNumericCode();
            var emailBinding = CreateEmailBinding(resolved.Email);
            var codeHash = _passwordHasher.HashPassword(
                resolved.User,
                RecoveryProofSecurity.BindToContext(
                    code,
                    resolved.Challenge.NativeContextHash!,
                    resolved.Challenge.NativeCsrfHash!,
                    emailBinding));
            resolved.Challenge.BindNativeAssistance(
                resolved.Challenge.NativeContextHash!,
                resolved.Challenge.NativeCsrfHash!,
                resolved.Authority.IsDirectory,
                resolved.Authority.IsDirectory ? resolved.Authority.DirectoryObjectId : null,
                resolved.Email.Version,
                resolved.User.SecurityStamp!);
            if (!resolved.Challenge.TrySupersedeCode(codeHash, now))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Unavailable();
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            challenge = resolved.Challenge;
            address = resolved.Email.Address;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            return Unavailable();
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            return Unavailable();
        }

        try
        {
            await _emailService.SendEmailAsync(
                address!,
                "Password recovery verification",
                "Your password recovery verification code is " + code + ". It expires with your active recovery request.",
                false,
                cancellationToken);
            await RecordAuditAsync(
                RecoveryProofAuditCategory.AdminNativeRecoveryOtpResent,
                request.TargetAccountId,
                request.ActorAccountId,
                cancellationToken);
            return new NativeRecoveryAssistanceResult(
                RecoveryProofOutcome.Success,
                _options.NativeOtpResendCooldownSeconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await _store.MarkChallengeRevokedAsync(challenge!.Id, cancellationToken);
            return Unavailable();
        }
    }

    public async Task<RecoveryEmailChangeResult> ReplaceEmailAsync(
        AdminNativeRecoveryEmailReplacementRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HasRequiredEvidence(request.IdentityCheckEvidence, request.Reason) ||
            !RecoveryProofSecurity.TryNormalizeAddress(
                request.CandidateAddress,
                out var address,
                out var normalizedAddress))
        {
            return new RecoveryEmailChangeResult(RecoveryProofOutcome.Invalid);
        }

        var authorization = await AuthorizeAsync(request.ActorAccountId, cancellationToken);
        if (authorization != RecoveryProofOutcome.Success)
        {
            return new RecoveryEmailChangeResult(authorization);
        }

        RecoveryProofChallenge? verificationChallenge = null;
        string? code = null;
        try
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var resolved = await ResolveActiveChallengeAsync(request.TargetAccountId, true, cancellationToken);
            if (resolved is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new RecoveryEmailChangeResult(RecoveryProofOutcome.Unavailable);
            }

            var now = _store.UtcNow;
            var approvals = await _dbContext.NativeRecoveryResetApprovals
                .Where(candidate => candidate.RecoveryProofChallengeId == resolved.Challenge.Id &&
                    candidate.RevokedAtUtc == null && candidate.ConsumedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var approval in approvals)
            {
                approval.Revoke(now);
            }

            resolved.Challenge.Revoke(now);
            resolved.Email.ReplaceAddress(
                address,
                normalizedAddress,
                now,
                now.AddSeconds(_options.NativeOtpResendCooldownSeconds),
                request.ActorAccountId,
                request.Reason.Trim(),
                request.IdentityCheckEvidence.Trim());

            code = RecoveryProofSecurity.GenerateNumericCode();
            var emailBinding = CreateEmailBinding(resolved.Email);
            var codeHash = _passwordHasher.HashPassword(
                resolved.User,
                RecoveryProofSecurity.BindToContext(
                    code,
                    resolved.Challenge.NativeContextHash!,
                    resolved.Challenge.NativeCsrfHash!,
                    emailBinding));
            verificationChallenge = new RecoveryProofChallenge(
                resolved.Email.Id,
                resolved.User.Id,
                RecoveryProofPurpose.RecoveryAddressVerification,
                codeHash,
                now,
                now.AddMinutes(_options.NativeOtpLifetimeMinutes));
            verificationChallenge.BindNativeAssistance(
                resolved.Challenge.NativeContextHash!,
                resolved.Challenge.NativeCsrfHash!,
                resolved.Authority.IsDirectory,
                resolved.Authority.IsDirectory ? resolved.Authority.DirectoryObjectId : null,
                resolved.Email.Version,
                resolved.User.SecurityStamp!,
                resolved.Challenge.Id);
            _dbContext.RecoveryProofChallenges.Add(verificationChallenge);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            return new RecoveryEmailChangeResult(RecoveryProofOutcome.Unavailable);
        }

        try
        {
            await _emailService.SendEmailAsync(
                address,
                "Verify recovery email",
                $"Your verification code is {code}. It expires in {_options.NativeOtpLifetimeMinutes} minutes.",
                false,
                cancellationToken);
            await RecordAuditAsync(
                RecoveryProofAuditCategory.AdminNativeRecoveryAddressReplaced,
                request.TargetAccountId,
                request.ActorAccountId,
                cancellationToken);
            return new RecoveryEmailChangeResult(RecoveryProofOutcome.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await _store.MarkChallengeRevokedAsync(verificationChallenge!.Id, cancellationToken);
            return new RecoveryEmailChangeResult(RecoveryProofOutcome.Unavailable);
        }
    }

    public async Task<ResetApprovalIssueResult> ApproveResetAsync(
        AdminNativeRecoveryApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HasRequiredEvidence(request.IdentityCheckEvidence, request.Reason))
        {
            return new ResetApprovalIssueResult(RecoveryProofOutcome.Invalid);
        }

        var authorization = await AuthorizeAsync(request.ActorAccountId, cancellationToken);
        if (authorization != RecoveryProofOutcome.Success)
        {
            return new ResetApprovalIssueResult(authorization);
        }

        try
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var resolved = await ResolveActiveChallengeAsync(request.TargetAccountId, true, cancellationToken);
            if (resolved is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ResetApprovalIssueResult(RecoveryProofOutcome.Unavailable);
            }

            var now = _store.UtcNow;
            var activeApprovals = await _dbContext.NativeRecoveryResetApprovals
                .Where(candidate => candidate.RecoveryProofChallengeId == resolved.Challenge.Id &&
                    candidate.RevokedAtUtc == null && candidate.ConsumedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var approval in activeApprovals)
            {
                approval.Revoke(now);
            }

            var expiry = now.AddMinutes(_options.OrdinaryRecoveryApprovalLifetimeMinutes);
            if (expiry > resolved.Challenge.ExpiresAtUtc)
            {
                expiry = resolved.Challenge.ExpiresAtUtc;
            }

            _dbContext.NativeRecoveryResetApprovals.Add(new NativeRecoveryResetApproval(
                resolved.Challenge.Id,
                resolved.User.Id,
                resolved.Email.Id,
                resolved.Email.Version,
                request.ActorAccountId,
                resolved.Challenge.NativeContextHash!,
                resolved.Challenge.NativeCsrfHash!,
                resolved.Authority.IsDirectory,
                resolved.Authority.IsDirectory ? resolved.Authority.DirectoryObjectId : null,
                resolved.User.SecurityStamp!,
                request.Reason.Trim(),
                request.IdentityCheckEvidence.Trim(),
                now,
                expiry));
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await RecordAuditAsync(
                RecoveryProofAuditCategory.AdminNativeResetApprovalIssued,
                request.TargetAccountId,
                request.ActorAccountId,
                cancellationToken);
            return new ResetApprovalIssueResult(RecoveryProofOutcome.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            return new ResetApprovalIssueResult(RecoveryProofOutcome.Unavailable);
        }
    }

    public async Task<RecoveryProofOutcome> VerifyReplacementAsync(
        NativeRecoveryReplacementVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled() || request.RequestId == Guid.Empty || request.Code.Length != 6 ||
            request.Code.Any(character => !char.IsAsciiDigit(character)) || !IsValidContext(request.Context))
        {
            return RecoveryProofOutcome.Unavailable;
        }

        try
        {
            if (!await IsRuntimeNativeAsync(cancellationToken))
            {
                return RecoveryProofOutcome.Unavailable;
            }

            var candidates = await _dbContext.RecoveryProofChallenges
                .Where(candidate => candidate.NativeRecoveryChallengeId == request.RequestId &&
                    candidate.Purpose == RecoveryProofPurpose.RecoveryAddressVerification &&
                    candidate.RevokedAtUtc == null && candidate.ConsumedAtUtc == null &&
                    candidate.VerifiedAtUtc == null)
                .Take(2)
                .ToListAsync(cancellationToken);
            if (candidates.Count != 1)
            {
                return RecoveryProofOutcome.Unavailable;
            }

            var challenge = candidates[0];
            var reservation = await _store.ReserveChallengeAttemptAsync(
                challenge.Id,
                RecoveryProofPurpose.RecoveryAddressVerification,
                _options.NativeOtpMaxAttempts,
                cancellationToken);
            if (reservation.Challenge is null)
            {
                return RecoveryProofOutcome.Unavailable;
            }

            var resolved = await ResolveBoundChallengeAsync(reservation.Challenge, false, cancellationToken);
            if (resolved is null ||
                !string.Equals(reservation.Challenge.NativeContextHash, request.Context.ContextHash, StringComparison.Ordinal) ||
                !string.Equals(reservation.Challenge.NativeCsrfHash, request.Context.CsrfHash, StringComparison.Ordinal))
            {
                await _store.MarkChallengeRevokedAsync(challenge.Id, cancellationToken);
                return RecoveryProofOutcome.Unavailable;
            }

            var boundCode = RecoveryProofSecurity.BindToContext(
                request.Code,
                request.Context.ContextHash,
                request.Context.CsrfHash,
                CreateEmailBinding(resolved.Email));
            if (_passwordHasher.VerifyHashedPassword(
                    resolved.User,
                    reservation.Challenge.CodeHash,
                    boundCode) == PasswordVerificationResult.Failed)
            {
                if (reservation.Challenge.VerificationAttempts >= _options.NativeOtpMaxAttempts)
                {
                    await _store.MarkChallengeRevokedAsync(challenge.Id, cancellationToken);
                }
                return RecoveryProofOutcome.Unavailable;
            }

            var outcome = await _store.CompleteAddressVerificationAsync(reservation.Challenge, cancellationToken);
            if (outcome == RecoveryProofOutcome.Success)
            {
                await RecordAuditAsync(
                    RecoveryProofAuditCategory.NativeRecoveryAddressVerified,
                    resolved.User.Id,
                    Guid.Empty,
                    cancellationToken);
            }
            return outcome == RecoveryProofOutcome.Success ? outcome : RecoveryProofOutcome.Unavailable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            return RecoveryProofOutcome.Unavailable;
        }
    }

    public async Task<RecoveryProofOutcome> GetApprovalStatusAsync(
        NativeRecoveryApprovalStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled() || request.RequestId == Guid.Empty || !IsValidContext(request.Context))
        {
            return RecoveryProofOutcome.Unavailable;
        }

        try
        {
            if (!await IsRuntimeNativeAsync(cancellationToken))
            {
                return RecoveryProofOutcome.Unavailable;
            }

            var challenge = await _dbContext.RecoveryProofChallenges.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == request.RequestId &&
                    candidate.Purpose == RecoveryProofPurpose.NativePasswordRecovery,
                    cancellationToken);
            if (challenge is null || await ResolveBoundChallengeAsync(challenge, true, cancellationToken) is null ||
                !string.Equals(challenge.NativeContextHash, request.Context.ContextHash, StringComparison.Ordinal) ||
                !string.Equals(challenge.NativeCsrfHash, request.Context.CsrfHash, StringComparison.Ordinal))
            {
                return RecoveryProofOutcome.Unavailable;
            }

            var approvals = await _dbContext.NativeRecoveryResetApprovals.AsNoTracking()
                .Where(candidate => candidate.RecoveryProofChallengeId == challenge.Id &&
                    candidate.RevokedAtUtc == null && candidate.ConsumedAtUtc == null)
                .Take(2)
                .ToListAsync(cancellationToken);
            return approvals.Count == 1 && IsApprovalBound(approvals[0], challenge) &&
                approvals[0].ExpiresAtUtc > _store.UtcNow
                    ? RecoveryProofOutcome.Success
                    : RecoveryProofOutcome.Unavailable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return RecoveryProofOutcome.Unavailable;
        }
    }

    private async Task<RecoveryProofOutcome> AuthorizeAsync(Guid actorAccountId, CancellationToken cancellationToken)
    {
        if (!IsEnabled() || !await IsRuntimeNativeAsync(cancellationToken))
        {
            return RecoveryProofOutcome.Unavailable;
        }

        return await _authorizer.IsAdministratorAuthorizedAsync(actorAccountId, cancellationToken)
            ? RecoveryProofOutcome.Success
            : RecoveryProofOutcome.Unauthorized;
    }

    private async Task<ResolvedNativeChallenge?> ResolveActiveChallengeAsync(
        Guid targetAccountId,
        bool requireVerifiedEmail,
        CancellationToken cancellationToken)
    {
        var now = _store.UtcNow;
        var challenges = await _dbContext.RecoveryProofChallenges
            .Where(candidate => candidate.LocalAccountId == targetAccountId &&
                candidate.Purpose == RecoveryProofPurpose.NativePasswordRecovery &&
                candidate.RevokedAtUtc == null && candidate.ConsumedAtUtc == null &&
                candidate.VerifiedAtUtc == null)
            .ToListAsync(cancellationToken);
        challenges = challenges.Where(candidate => candidate.ExpiresAtUtc > now).Take(2).ToList();
        return challenges.Count == 1
            ? await ResolveBoundChallengeAsync(challenges[0], requireVerifiedEmail, cancellationToken)
            : null;
    }

    private async Task<ResolvedNativeChallenge?> ResolveBoundChallengeAsync(
        RecoveryProofChallenge challenge,
        bool requireVerifiedEmail,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.Id == challenge.LocalAccountId,
            cancellationToken);
        var email = await _dbContext.RecoveryEmails.SingleOrDefaultAsync(
            candidate => candidate.Id == challenge.RecoveryEmailId &&
                candidate.LocalAccountId == challenge.LocalAccountId,
            cancellationToken);
        var authority = user is null ? null : await ResolveAuthorityAsync(user, cancellationToken);
        if (user is null || email is null || authority is null ||
            requireVerifiedEmail && email.VerifiedAtUtc is null ||
            !HasCurrentBinding(challenge, user, email, authority))
        {
            return null;
        }

        if (requireVerifiedEmail)
        {
            var decision = await _policyEvaluator.EvaluateAsync(user.Id, cancellationToken);
            if (!IsVerifiedLocalDestination(decision) ||
                !string.Equals(email.Address, decision.RecoveryEmail.Address, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return new ResolvedNativeChallenge(challenge, user, email, authority);
    }

    private async Task<RecoveryAuthority?> ResolveAuthorityAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var now = _store.UtcNow;
        if (!user.IsActive || user.IsDeleted || string.IsNullOrWhiteSpace(user.SecurityStamp) ||
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
                 migration.ProviderSubjectDirectoryBindingId == binding.Id && binding.LocalAccountId == user.Id)
        {
            authority = new RecoveryAuthority(true, binding.DirectoryObjectId);
        }

        if (authority is null || user.PersonId is not { } personId)
        {
            return authority;
        }

        var person = await _dbContext.Persons.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == personId, cancellationToken);
        return person?.CanAuthenticate() == true ? authority : null;
    }

    private static bool HasCurrentBinding(
        RecoveryProofChallenge challenge,
        ApplicationUser user,
        RecoveryEmailRecord email,
        RecoveryAuthority authority) =>
        challenge.NativeDirectoryAuthority == authority.IsDirectory &&
        challenge.NativeDirectoryObjectId == (authority.IsDirectory ? authority.DirectoryObjectId : null) &&
        challenge.NativeRecoveryEmailVersion == email.Version &&
        string.Equals(challenge.NativeSecurityStamp, user.SecurityStamp, StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(challenge.NativeContextHash) &&
        !string.IsNullOrWhiteSpace(challenge.NativeCsrfHash);

    private static bool IsApprovalBound(
        NativeRecoveryResetApproval approval,
        RecoveryProofChallenge challenge) =>
        approval.LocalAccountId == challenge.LocalAccountId &&
        approval.RecoveryEmailId == challenge.RecoveryEmailId &&
        approval.RecoveryEmailVersion == challenge.NativeRecoveryEmailVersion &&
        approval.DirectoryAuthority == challenge.NativeDirectoryAuthority &&
        approval.DirectoryObjectId == challenge.NativeDirectoryObjectId &&
        string.Equals(approval.ContextHash, challenge.NativeContextHash, StringComparison.Ordinal) &&
        string.Equals(approval.CsrfHash, challenge.NativeCsrfHash, StringComparison.Ordinal) &&
        string.Equals(approval.SecurityStamp, challenge.NativeSecurityStamp, StringComparison.Ordinal);

    private bool IsEnabled() =>
        _options.OrdinaryRecoveryAssistanceEnabled &&
        _options.NativeRecoveryEnabled &&
        _options.DeploymentCeiling == ForgotPasswordMode.Native;

    private async Task<bool> IsRuntimeNativeAsync(CancellationToken cancellationToken)
    {
        var runtimeMode = await _dbContext.SecurityPolicies.AsNoTracking()
            .OrderBy(policy => policy.Id)
            .Select(policy => policy.ForgotPasswordMode)
            .FirstOrDefaultAsync(cancellationToken);
        return _routingEvaluator.Evaluate(runtimeMode, null).PermittedMode == ForgotPasswordMode.Native;
    }

    private static bool IsVerifiedLocalDestination(RecoveryVerificationPolicyDecision decision) =>
        decision.Enabled &&
        decision.RecoveryEmail.AddressSource == RecoveryEmailAddressSource.LocalRecord &&
        decision.RecoveryEmail.TrustOrigin == RecoveryEmailPolicyTrustOrigin.LocallyVerified &&
        decision.RecoveryEmail.CanReceiveRecoveryOtp &&
        !string.IsNullOrWhiteSpace(decision.RecoveryEmail.Address);

    private static bool HasRequiredEvidence(string evidence, string reason) =>
        !string.IsNullOrWhiteSpace(evidence) && evidence.Trim().Length <= 500 &&
        !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 500;

    private static bool IsValidContext(NativeRecoveryContext context) =>
        !string.IsNullOrWhiteSpace(context.ContextHash) && context.ContextHash.Length <= 256 &&
        !string.IsNullOrWhiteSpace(context.CsrfHash) && context.CsrfHash.Length <= 256;

    private static string CreateEmailBinding(RecoveryEmailRecord email) =>
        $"{email.Id:N}:{email.LocalAccountId:N}:{email.Version}:{email.NormalizedAddress}";

    private static NativeRecoveryAssistanceResult Unavailable() =>
        new(RecoveryProofOutcome.Unavailable);

    private Task RecordAuditAsync(
        RecoveryProofAuditCategory category,
        Guid targetAccountId,
        Guid actorAccountId,
        CancellationToken cancellationToken) =>
        _audit.RecordAsync(
            new RecoveryProofAuditEvent(
                Guid.NewGuid(),
                category,
                targetAccountId,
                actorAccountId == Guid.Empty ? null : actorAccountId),
            cancellationToken);

    private sealed record RecoveryAuthority(bool IsDirectory, Guid DirectoryObjectId);
    private sealed record ResolvedNativeChallenge(
        RecoveryProofChallenge Challenge,
        ApplicationUser User,
        RecoveryEmailRecord Email,
        RecoveryAuthority Authority);
}
