using Core.Application.Ports;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

internal sealed class RecoveryProofStore
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly IRecoveryDestinationResolver? _resolver;
    private readonly bool _selectionEnabled;

    public RecoveryProofStore(ApplicationDbContext dbContext, TimeProvider? timeProvider = null,
        IRecoveryDestinationResolver? resolver = null, bool selectionEnabled = false)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _resolver = resolver;
        _selectionEnabled = selectionEnabled;
    }

    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    internal Task<bool> HasSelectionIntentAsync(Guid accountId, CancellationToken ct) =>
        _dbContext.RecoveryEmailPreferences.AsNoTracking().AnyAsync(p => p.LocalAccountId == accountId, ct);

    internal async Task<bool> RequiresSelectionAsync(Guid accountId, CancellationToken ct) =>
        _selectionEnabled || await HasSelectionIntentAsync(accountId, ct);

    internal async Task<RecoveryEmailPreference?> BeginAdministrativeSelectionAsync(Guid accountId, CancellationToken ct)
    {
        var preference = await _dbContext.RecoveryEmailPreferences.SingleOrDefaultAsync(p => p.LocalAccountId == accountId, ct);
        if (preference is null) { preference = new(accountId, UtcNow); _dbContext.RecoveryEmailPreferences.Add(preference); }
        // Replacing an address is not permission to clear an administrative recovery block.
        if (!preference.TrySelect(RecoveryEmailSelectionMode.UseCustom, preference.SelectionEpoch, UtcNow)) return null;
        foreach (var grant in await _dbContext.RecoveryPrecheckGrants.Where(g => g.LocalAccountId == accountId && g.RevokedAtUtc == null).ToListAsync(ct)) grant.Revoke(UtcNow);
        foreach (var challenge in await _dbContext.RecoveryProofChallenges.Where(c => c.LocalAccountId == accountId && c.RevokedAtUtc == null && c.ConsumedAtUtc == null).ToListAsync(ct)) challenge.Revoke(UtcNow);
        foreach (var approval in await _dbContext.RecoveryResetApprovals.Where(a => a.LocalAccountId == accountId && a.RevokedAtUtc == null && a.ConsumedAtUtc == null).ToListAsync(ct)) approval.Revoke(UtcNow);
        foreach (var approval in await _dbContext.NativeRecoveryResetApprovals.Where(a => a.LocalAccountId == accountId && a.RevokedAtUtc == null && a.ConsumedAtUtc == null).ToListAsync(ct)) approval.Revoke(UtcNow);
        return preference;
    }

    internal static string ReplacementFingerprint(RecoveryEmailRecord email) => RecoveryDestinationBinding.ComputeDigest(
        "recovery-admin-replacement-v1", email.LocalAccountId.ToString("D"), email.Id.ToString("D"), email.NormalizedAddress);

    internal async Task<bool> IsCurrentReplacementAsync(RecoveryProofChallenge challenge, RecoveryEmailRecord email, CancellationToken ct)
    {
        if (challenge.Purpose != RecoveryProofPurpose.RecoveryAddressVerification || challenge.LocalAccountId != email.LocalAccountId ||
            challenge.RecoveryEmailId != email.Id) return false;
        if (challenge.SelectionEpoch is null) return !await RequiresSelectionAsync(challenge.LocalAccountId, ct);
        var preference = await _dbContext.RecoveryEmailPreferences.AsNoTracking().SingleOrDefaultAsync(p => p.LocalAccountId == email.LocalAccountId, ct);
        var user = await _dbContext.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == email.LocalAccountId, ct);
        return preference is { Mode: RecoveryEmailSelectionMode.UseCustom, AdministrativeBlockedAtUtc: null } &&
            user is { IsActive: true, IsDeleted: false } && user.SecurityStamp == challenge.NativeSecurityStamp &&
            (!user.LockoutEnabled || user.LockoutEnd is null || user.LockoutEnd <= UtcNow) &&
            challenge.MatchesSelection(preference.SelectionEpoch, RecoveryDestinationKind.Custom, ReplacementFingerprint(email), email.Version);
    }

    internal async Task<RecoveryDestination?> ResolveDestinationAsync(Guid accountId, CancellationToken ct) =>
        _resolver is null ? null : (await _resolver.ResolveAsync(accountId, ct)).Destination;

    internal async Task<string?> MigrationBindingAsync(Guid accountId, Guid continuationId,
        RecoveryDestination destination, CancellationToken ct)
    {
        var user = await _dbContext.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == accountId, ct);
        var ticket = await _dbContext.CredentialMigrationContinuations.AsNoTracking().SingleOrDefaultAsync(t => t.Id == continuationId, ct);
        var state = await _dbContext.CredentialMigrationStateRecords.AsNoTracking().SingleOrDefaultAsync(s => s.LocalAccountId == accountId, ct);
        var binding = await _dbContext.ProviderSubjectDirectoryBindings.AsNoTracking().SingleOrDefaultAsync(b => b.LocalAccountId == accountId, ct);
        if (user is null || !user.IsActive || user.IsDeleted || string.IsNullOrWhiteSpace(user.SecurityStamp) ||
            user.LockoutEnabled && user.LockoutEnd > UtcNow || destination.LocalAccountId != accountId ||
            ticket is null || ticket.ConsumedAtUtc is not null || ticket.ExpiresAtUtc <= UtcNow ||
            state is null || state.Id != ticket.CredentialMigrationStateRecordId ||
            state.State != CredentialMigrationState.ProofValidated || state.EffectiveEmailOtpRequirement != EffectiveEmailOtpRequirement.Required ||
            binding is null || state.ProviderSubjectDirectoryBindingId != binding.Id ||
            await _dbContext.NativeDirectoryRecoveryAttempts.AsNoTracking().AnyAsync(a => a.LocalAccountId == accountId &&
                (a.Status == NativeDirectoryRecoveryStatus.Reserved || a.Status == NativeDirectoryRecoveryStatus.ReconciliationRequired), ct)) return null;
        if (user.PersonId is { } personId &&
            (await _dbContext.Persons.AsNoTracking().SingleOrDefaultAsync(p => p.Id == personId, ct))?.CanAuthenticate() != true) return null;
        return RecoveryDestinationBinding.ComputeDigest("recovery-migration-proof-v1", accountId.ToString("D"),
            continuationId.ToString("D"), ticket.ContextHash, ticket.CsrfHash, user.SecurityStamp,
            RecoveryPrecheckService.BindingDigest(binding), destination.EffectivePolicyDigest, RecoveryPrecheckService.OtpBinding(destination));
    }

    internal async Task<string?> CurrentMigrationBindingAsync(RecoveryProofChallenge challenge, CancellationToken ct)
    {
        var destination = await ResolveDestinationAsync(challenge.LocalAccountId, ct);
        if (challenge.Purpose != RecoveryProofPurpose.MigrationOtp || challenge.CredentialMigrationContinuationId is not { } continuationId ||
            destination is null || destination.RecoveryEmailId != challenge.RecoveryEmailId ||
            !challenge.MatchesSelection(destination.SelectionEpoch, destination.Kind, destination.Fingerprint, destination.Version)) return null;
        return await MigrationBindingAsync(challenge.LocalAccountId, continuationId, destination, ct);
    }

    internal async Task<RecoveryCeremonyState?> ResolveNativeSelectionAsync(
        RecoveryProofChallenge challenge, NativeRecoveryContext context,
        RecoveryPrecheckService? precheck, CancellationToken ct)
    {
        if (precheck is null || challenge.Purpose != RecoveryProofPurpose.NativePasswordRecovery ||
            challenge.SelectionEpoch is null || challenge.RevokedAtUtc is not null ||
            challenge.ConsumedAtUtc is not null || challenge.ExpiresAtUtc <= UtcNow ||
            challenge.DeliveryState != RecoveryChallengeDeliveryState.Delivered) return null;
        var state = await precheck.ResolveStateAsync(challenge.LocalAccountId, ct);
        if (state is null || !challenge.MatchesSelection(state.Destination.SelectionEpoch,
                state.Destination.Kind, state.Destination.Fingerprint, state.Destination.Version) ||
            challenge.RecoveryEmailId != state.Destination.RecoveryEmailId ||
            challenge.NativeSecurityStamp != state.User.SecurityStamp ||
            challenge.NativeDirectoryAuthority != state.IsDirectory ||
            challenge.NativeDirectoryObjectId != (state.IsDirectory ? state.Binding!.DirectoryObjectId : null) ||
            challenge.NativeContextHash != context.ContextHash || challenge.NativeCsrfHash != context.CsrfHash) return null;
        var grant = await _dbContext.RecoveryPrecheckGrants.AsNoTracking()
            .SingleOrDefaultAsync(g => g.ReservedChallengeId == challenge.Id, ct);
        // The grant authorizes Send before its expiry. The sent challenge retains its own OTP lifetime.
        return grant is not null && grant.ConsumedAtUtc is { } consumed && consumed < grant.ExpiresAtUtc &&
            grant.RevokedAtUtc is null && RecoveryPrecheckService.Matches(grant, state, context) ? state : null;
    }

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
        if (email is null || !await IsCurrentReplacementAsync(challenge, email, cancellationToken) || !challenge.TryMarkAddressVerified(now))
        {
            await transaction.RollbackAsync(cancellationToken);
            return RecoveryProofOutcome.Replayed;
        }

        email.MarkVerified(now);
        if (challenge.SelectionEpoch is not null)
        {
            email.ActivateVerifiedCustom(email.Address, email.NormalizedAddress, now, RecoveryEmailProvenance.AdminAssistedVerified);
            var preference = await _dbContext.RecoveryEmailPreferences.SingleAsync(p => p.LocalAccountId == email.LocalAccountId, cancellationToken);
            preference.Advance(now);
        }
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
        var binding = challenge.SelectionEpoch is null ? null : await CurrentMigrationBindingAsync(challenge, cancellationToken);
        if (challenge.Purpose != RecoveryProofPurpose.MigrationOtp ||
            (challenge.SelectionEpoch is not null ? binding is null : await RequiresSelectionAsync(challenge.LocalAccountId, cancellationToken)))
            return new(RecoveryProofOutcome.Invalid);
        var proofHash = RecoveryProofSecurity.Hash(binding is null ? proof : RecoveryProofSecurity.BindToContext(proof, binding, string.Empty, string.Empty));
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
        var candidates = await _dbContext.RecoveryProofChallenges.Where(c => c.CredentialMigrationContinuationId == continuationId &&
            c.Purpose == RecoveryProofPurpose.MigrationOtp && c.ProofTokenHash != null).ToListAsync(cancellationToken);
        RecoveryProofChallenge? existing = null;
        foreach (var candidate in candidates)
        {
            var binding = candidate.SelectionEpoch is null ? null : await CurrentMigrationBindingAsync(candidate, cancellationToken);
            if (candidate.SelectionEpoch is not null ? binding is null : await RequiresSelectionAsync(candidate.LocalAccountId, cancellationToken)) continue;
            var expected = binding is null ? proofHash : RecoveryProofSecurity.Hash(RecoveryProofSecurity.BindToContext(proof, binding, string.Empty, string.Empty));
            if (candidate.ProofTokenHash == expected) { existing = candidate; break; }
        }
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
