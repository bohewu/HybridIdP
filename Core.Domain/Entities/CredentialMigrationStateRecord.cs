namespace Core.Domain.Entities;

/// <summary>
/// The durable, ordered migration state for one local account and immutable directory binding.
/// </summary>
public sealed class CredentialMigrationStateRecord
{
    private CredentialMigrationStateRecord()
    {
    }

    public CredentialMigrationStateRecord(
        Guid localAccountId,
        Guid providerSubjectDirectoryBindingId,
        DateTimeOffset createdAtUtc)
    {
        if (localAccountId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(localAccountId));
        }

        if (providerSubjectDirectoryBindingId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(providerSubjectDirectoryBindingId));
        }

        Id = Guid.NewGuid();
        LocalAccountId = localAccountId;
        ProviderSubjectDirectoryBindingId = providerSubjectDirectoryBindingId;
        State = CredentialMigrationState.Required;
        EffectiveEmailOtpRequirement = EffectiveEmailOtpRequirement.Unspecified;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid LocalAccountId { get; private set; }
    public Guid ProviderSubjectDirectoryBindingId { get; private set; }
    public CredentialMigrationState State { get; private set; }
    public EffectiveEmailOtpRequirement EffectiveEmailOtpRequirement { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public long Version { get; private set; }

    public void Advance(CredentialMigrationState nextState, DateTimeOffset updatedAtUtc)
    {
        Advance(nextState, null, updatedAtUtc);
    }

    public void Advance(
        CredentialMigrationState nextState,
        EffectiveEmailOtpRequirement? effectiveEmailOtpRequirement,
        DateTimeOffset updatedAtUtc)
    {
        if (!IsLegalTransition(State, nextState))
        {
            throw new InvalidOperationException($"Cannot advance migration state from {State} to {nextState}.");
        }

        if (State == CredentialMigrationState.Required)
        {
            if (nextState != CredentialMigrationState.ProofValidated ||
                EffectiveEmailOtpRequirement != EffectiveEmailOtpRequirement.Unspecified ||
                !IsPersistableRequirement(effectiveEmailOtpRequirement))
            {
                throw new InvalidOperationException("Proof validation requires one immutable effective email OTP requirement.");
            }

            EffectiveEmailOtpRequirement = effectiveEmailOtpRequirement!.Value;
        }
        else if (effectiveEmailOtpRequirement is not null ||
                 !IsPersistableRequirement(EffectiveEmailOtpRequirement))
        {
            throw new InvalidOperationException("Later migration transitions require the persisted email OTP requirement.");
        }

        State = nextState;
        UpdatedAtUtc = updatedAtUtc;
        Version++;
    }

    public static bool IsLegalTransition(CredentialMigrationState currentState, CredentialMigrationState nextState) =>
        (currentState, nextState) switch
        {
            (CredentialMigrationState.Required, CredentialMigrationState.ProofValidated) => true,
            (CredentialMigrationState.ProofValidated, CredentialMigrationState.DirectoryCredentialCommitted) => true,
            (CredentialMigrationState.DirectoryCredentialCommitted, CredentialMigrationState.LocalFinalized) => true,
            _ => false
        };

    public static bool IsPersistableRequirement(EffectiveEmailOtpRequirement? requirement) =>
        requirement is EffectiveEmailOtpRequirement.NotRequired or EffectiveEmailOtpRequirement.Required;
}

public enum CredentialMigrationState
{
    Required,
    ProofValidated,
    DirectoryCredentialCommitted,
    LocalFinalized
}

public enum EffectiveEmailOtpRequirement
{
    Unspecified = 0,
    NotRequired = 1,
    Required = 2
}
