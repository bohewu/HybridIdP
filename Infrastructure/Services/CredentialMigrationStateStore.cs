using Core.Application.Ports;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

/// <summary>
/// Persists the immutable-binding prerequisite and ordered migration state.
/// </summary>
public sealed class CredentialMigrationStateStore : ICredentialMigrationStateStore
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public CredentialMigrationStateStore(ApplicationDbContext dbContext, TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CredentialMigrationRecord?> FindAsync(
        Guid localAccountId,
        CancellationToken cancellationToken = default)
    {
        if (localAccountId == Guid.Empty)
        {
            return null;
        }

        var state = await _dbContext.CredentialMigrationStateRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.LocalAccountId == localAccountId, cancellationToken);
        if (state is null)
        {
            return null;
        }

        var binding = await _dbContext.ProviderSubjectDirectoryBindings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == state.ProviderSubjectDirectoryBindingId,
                cancellationToken);
        if (binding is null)
        {
            throw new InvalidOperationException("Migration state is missing its immutable directory binding.");
        }

        return ToRecord(state, binding);
    }

    public async Task<CredentialMigrationRecord> EnsureRequiredAsync(
        Guid localAccountId,
        DirectoryObjectBinding binding,
        CancellationToken cancellationToken = default)
    {
        ValidateBinding(localAccountId, binding);
        var now = _timeProvider.GetUtcNow();
        var normalizedAlias = ProviderSubjectDirectoryBinding.NormalizeCanonicalAccountAlias(
            binding.CanonicalAccountAlias);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var userExists = await _dbContext.Users.AnyAsync(user => user.Id == localAccountId, cancellationToken);
            if (!userExists)
            {
                throw new InvalidOperationException("The local account does not exist.");
            }

            var matchingBindings = await _dbContext.ProviderSubjectDirectoryBindings
                .Where(candidate =>
                    candidate.LocalAccountId == localAccountId ||
                    (candidate.ProviderNamespace == binding.ProviderNamespace &&
                     candidate.StableSubject == binding.StableSubject) ||
                    candidate.DirectoryObjectId == binding.DirectoryObjectId ||
                    (normalizedAlias != null &&
                     candidate.NormalizedCanonicalAccountAlias == normalizedAlias))
                .ToListAsync(cancellationToken);

            if (matchingBindings.Any(candidate => !Matches(candidate, localAccountId, binding)))
            {
                throw new InvalidOperationException("The immutable directory binding conflicts with an existing account.");
            }

            var immutableBinding = matchingBindings.SingleOrDefault();
            if (immutableBinding is null)
            {
                immutableBinding = new ProviderSubjectDirectoryBinding(
                    localAccountId,
                    binding.ProviderNamespace,
                    binding.StableSubject,
                    binding.DirectoryObjectId,
                    now.UtcDateTime,
                    binding.CanonicalAccountAlias);
                _dbContext.ProviderSubjectDirectoryBindings.Add(immutableBinding);
            }
            else
            {
                immutableBinding.SetCanonicalAccountAlias(binding.CanonicalAccountAlias);
            }

            var existingState = await _dbContext.CredentialMigrationStateRecords
                .SingleOrDefaultAsync(candidate => candidate.LocalAccountId == localAccountId, cancellationToken);
            if (existingState is not null)
            {
                if (existingState.ProviderSubjectDirectoryBindingId != immutableBinding.Id)
                {
                    throw new InvalidOperationException("The migration state is bound to a different directory object.");
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return ToRecord(existingState, immutableBinding);
            }

            var state = new CredentialMigrationStateRecord(localAccountId, immutableBinding.Id, now);
            _dbContext.CredentialMigrationStateRecords.Add(state);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToRecord(state, immutableBinding);
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();

            var concurrentRecord = await FindAsync(localAccountId, cancellationToken);
            if (concurrentRecord is not null &&
                Matches(concurrentRecord.Binding, binding) &&
                HasCompatibleAlias(concurrentRecord.Binding.CanonicalAccountAlias, binding.CanonicalAccountAlias))
            {
                return concurrentRecord;
            }

            throw new InvalidOperationException("Unable to establish the immutable migration binding.", exception);
        }
    }

    public async Task<CredentialMigrationRecord> AdvanceAsync(
        Guid localAccountId,
        CredentialMigrationState expectedState,
        CredentialMigrationState nextState,
        CancellationToken cancellationToken = default)
    {
        return await AdvanceInternalAsync(
            localAccountId,
            expectedState,
            nextState,
            null,
            cancellationToken);
    }

    public async Task<CredentialMigrationRecord> AdvanceToProofValidatedAsync(
        Guid localAccountId,
        EffectiveEmailOtpRequirement effectiveEmailOtpRequirement,
        CancellationToken cancellationToken = default)
    {
        return await AdvanceInternalAsync(
            localAccountId,
            CredentialMigrationState.Required,
            CredentialMigrationState.ProofValidated,
            effectiveEmailOtpRequirement,
            cancellationToken);
    }

    private async Task<CredentialMigrationRecord> AdvanceInternalAsync(
        Guid localAccountId,
        CredentialMigrationState expectedState,
        CredentialMigrationState nextState,
        EffectiveEmailOtpRequirement? effectiveEmailOtpRequirement,
        CancellationToken cancellationToken)
    {
        if (localAccountId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(localAccountId));
        }

        if (!CredentialMigrationStateRecord.IsLegalTransition(expectedState, nextState))
        {
            throw new ArgumentException("The requested migration transition is not legal.", nameof(nextState));
        }

        var isProofTransition = expectedState == CredentialMigrationState.Required &&
            nextState == CredentialMigrationState.ProofValidated;
        if (isProofTransition && !CredentialMigrationStateRecord.IsPersistableRequirement(effectiveEmailOtpRequirement))
        {
            throw new ArgumentException("Proof validation requires an effective email OTP requirement.", nameof(effectiveEmailOtpRequirement));
        }

        if (!isProofTransition && effectiveEmailOtpRequirement is not null)
        {
            throw new ArgumentException("Only proof validation can persist an email OTP requirement.", nameof(effectiveEmailOtpRequirement));
        }

        var now = _timeProvider.GetUtcNow();
        var states = _dbContext.CredentialMigrationStateRecords
            .Where(record => record.LocalAccountId == localAccountId && record.State == expectedState);
        var updated = isProofTransition
            ? await states
                .Where(record => record.EffectiveEmailOtpRequirement == EffectiveEmailOtpRequirement.Unspecified)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(record => record.State, nextState)
                        .SetProperty(record => record.EffectiveEmailOtpRequirement, effectiveEmailOtpRequirement!.Value)
                        .SetProperty(record => record.UpdatedAtUtc, now)
                        .SetProperty(record => record.Version, record => record.Version + 1),
                    cancellationToken)
            : await states
                .Where(record => record.EffectiveEmailOtpRequirement != EffectiveEmailOtpRequirement.Unspecified)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(record => record.State, nextState)
                        .SetProperty(record => record.UpdatedAtUtc, now)
                        .SetProperty(record => record.Version, record => record.Version + 1),
                    cancellationToken);

        if (updated == 1)
        {
            return await FindAsync(localAccountId, cancellationToken)
                ?? throw new InvalidOperationException("Migration state disappeared after its transition.");
        }

        throw new InvalidOperationException("Migration state was not in the expected state for this transition.");
    }

    private static void ValidateBinding(Guid localAccountId, DirectoryObjectBinding binding)
    {
        if (localAccountId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(localAccountId));
        }

        if (string.IsNullOrWhiteSpace(binding.ProviderNamespace) ||
            string.IsNullOrWhiteSpace(binding.StableSubject) ||
            binding.DirectoryObjectId == Guid.Empty)
        {
            throw new ArgumentException("A complete immutable directory binding is required.", nameof(binding));
        }
    }

    private static bool Matches(
        ProviderSubjectDirectoryBinding candidate,
        Guid localAccountId,
        DirectoryObjectBinding binding) =>
        candidate.LocalAccountId == localAccountId &&
        candidate.ProviderNamespace == binding.ProviderNamespace &&
        candidate.StableSubject == binding.StableSubject &&
        candidate.DirectoryObjectId == binding.DirectoryObjectId;

    private static bool Matches(DirectoryObjectBinding candidate, DirectoryObjectBinding binding) =>
        candidate.ProviderNamespace == binding.ProviderNamespace &&
        candidate.StableSubject == binding.StableSubject &&
        candidate.DirectoryObjectId == binding.DirectoryObjectId;

    private static bool HasCompatibleAlias(string? persistedAlias, string? requestedAlias) =>
        string.IsNullOrWhiteSpace(requestedAlias) ||
        string.Equals(
            persistedAlias,
            ProviderSubjectDirectoryBinding.NormalizeCanonicalAccountAlias(requestedAlias),
            StringComparison.Ordinal);

    private static CredentialMigrationRecord ToRecord(
        CredentialMigrationStateRecord state,
        ProviderSubjectDirectoryBinding binding) =>
        new(
            state.LocalAccountId,
            new DirectoryObjectBinding(
                binding.ProviderNamespace,
                binding.StableSubject,
                binding.DirectoryObjectId,
                binding.NormalizedCanonicalAccountAlias),
            state.State,
            state.EffectiveEmailOtpRequirement);
}
