using System.Security.Cryptography;
using System.Text;
using Core.Application.Ports;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

/// <summary>
/// Stores only hash material for one opaque, context-bound migration continuation.
/// </summary>
public sealed class MigrationContinuationStore : IMigrationContinuationStore
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(10);

    private readonly ApplicationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public MigrationContinuationStore(ApplicationDbContext dbContext, TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<MigrationContinuation> CreateAsync(
        MigrationContinuationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var now = _timeProvider.GetUtcNow();
        var expiresAt = request.ExpiresAt ?? now.Add(DefaultLifetime);
        if (expiresAt <= now || expiresAt > now.Add(DefaultLifetime))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Continuation expiry must be within ten minutes.");
        }

        var state = await _dbContext.CredentialMigrationStateRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.LocalAccountId == request.LocalAccountId, cancellationToken);
        if (state?.State != CredentialMigrationState.ProofValidated ||
            !CredentialMigrationStateRecord.IsPersistableRequirement(state.EffectiveEmailOtpRequirement))
        {
            throw new InvalidOperationException("A continuation requires durable proof validation.");
        }

        var binding = await _dbContext.ProviderSubjectDirectoryBindings
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.Id == state.ProviderSubjectDirectoryBindingId, cancellationToken);
        if (binding is null || !Matches(binding, request.LocalAccountId, request.Binding))
        {
            throw new InvalidOperationException("A continuation requires the exact immutable directory binding.");
        }

        var opaqueValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        _dbContext.CredentialMigrationContinuations.Add(new CredentialMigrationContinuationRecord(
            state.Id,
            Hash(opaqueValue),
            request.Context.ContextHash,
            request.Context.CsrfHash,
            now,
            expiresAt));
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new MigrationContinuation(opaqueValue, expiresAt);
    }

    public async Task<MigrationContinuationConsumption> ConsumeAsync(
        string protectedValue,
        MigrationContinuationContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(protectedValue) || !IsHash(context.ContextHash) || !IsHash(context.CsrfHash))
        {
            return new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.Unavailable);
        }

        var tokenHash = Hash(protectedValue);
        var now = _timeProvider.GetUtcNow();
        var candidate = await _dbContext.CredentialMigrationContinuations
            .SingleOrDefaultAsync(record => record.TokenHash == tokenHash, cancellationToken);
        if (candidate is null)
        {
            return new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.Unavailable);
        }

        if (candidate.ConsumedAtUtc is not null)
        {
            return new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.AlreadyUsed);
        }

        if (candidate.ExpiresAtUtc <= now)
        {
            return new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.Expired);
        }

        if (!HashesEqual(candidate.ContextHash, context.ContextHash) ||
            !HashesEqual(candidate.CsrfHash, context.CsrfHash))
        {
            return new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.ContextMismatch);
        }

        if (!candidate.TryConsume(now))
        {
            return new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.AlreadyUsed);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new MigrationContinuationConsumption(
                MigrationContinuationConsumptionOutcome.Consumed,
                await GetRecordAsync(candidate.CredentialMigrationStateRecordId, cancellationToken),
                candidate.Id,
                candidate.Version);
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            var current = await _dbContext.CredentialMigrationContinuations
                .AsNoTracking()
                .SingleAsync(record => record.Id == candidate.Id, cancellationToken);
            return current.ConsumedAtUtc is not null
                ? new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.AlreadyUsed)
                : current.ExpiresAtUtc <= now
                    ? new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.Expired)
                    : new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.ContextMismatch);
        }
    }

    public async Task<MigrationContinuationConsumption> InspectAsync(
        string protectedValue,
        MigrationContinuationContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(protectedValue) || !IsHash(context.ContextHash) || !IsHash(context.CsrfHash))
        {
            return new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.Unavailable);
        }

        var now = _timeProvider.GetUtcNow();
        var candidate = await _dbContext.CredentialMigrationContinuations
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.TokenHash == Hash(protectedValue), cancellationToken);
        if (candidate is null)
        {
            return new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.Unavailable);
        }

        if (candidate.ConsumedAtUtc is not null)
        {
            return new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.AlreadyUsed);
        }

        if (candidate.ExpiresAtUtc <= now)
        {
            return new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.Expired);
        }

        if (!HashesEqual(candidate.ContextHash, context.ContextHash) ||
            !HashesEqual(candidate.CsrfHash, context.CsrfHash))
        {
            return new MigrationContinuationConsumption(MigrationContinuationConsumptionOutcome.ContextMismatch);
        }

        return new MigrationContinuationConsumption(
            MigrationContinuationConsumptionOutcome.Consumed,
            await GetRecordAsync(candidate.CredentialMigrationStateRecordId, cancellationToken));
    }

    private async Task<CredentialMigrationRecord> GetRecordAsync(
        Guid stateRecordId,
        CancellationToken cancellationToken)
    {
        var state = await _dbContext.CredentialMigrationStateRecords
            .AsNoTracking()
            .SingleAsync(record => record.Id == stateRecordId, cancellationToken);
        var binding = await _dbContext.ProviderSubjectDirectoryBindings
            .AsNoTracking()
            .SingleAsync(record => record.Id == state.ProviderSubjectDirectoryBindingId, cancellationToken);
        return new CredentialMigrationRecord(
            state.LocalAccountId,
            new DirectoryObjectBinding(binding.ProviderNamespace, binding.StableSubject, binding.DirectoryObjectId),
            state.State,
            state.EffectiveEmailOtpRequirement);
    }

    private static void ValidateRequest(MigrationContinuationRequest request)
    {
        if (request.LocalAccountId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Binding.ProviderNamespace) ||
            string.IsNullOrWhiteSpace(request.Binding.StableSubject) ||
            request.Binding.DirectoryObjectId == Guid.Empty ||
            !IsHash(request.Context.ContextHash) ||
            !IsHash(request.Context.CsrfHash))
        {
            throw new ArgumentException("A complete bound continuation request is required.", nameof(request));
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

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool IsHash(string? value) =>
        value is { Length: 64 } && value.All(character =>
            (character >= '0' && character <= '9') ||
            (character >= 'A' && character <= 'F') ||
            (character >= 'a' && character <= 'f'));

    private static bool HashesEqual(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));
}
