namespace Core.Domain.Entities;

/// <summary>
/// A single-use, context-bound opaque migration continuation. Only hashes are persisted.
/// </summary>
public sealed class CredentialMigrationContinuationRecord
{
    private CredentialMigrationContinuationRecord()
    {
    }

    public CredentialMigrationContinuationRecord(
        Guid credentialMigrationStateRecordId,
        string tokenHash,
        string contextHash,
        string csrfHash,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (credentialMigrationStateRecordId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(credentialMigrationStateRecordId));
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("A token hash is required.", nameof(tokenHash));
        }

        if (string.IsNullOrWhiteSpace(contextHash))
        {
            throw new ArgumentException("A context hash is required.", nameof(contextHash));
        }

        if (string.IsNullOrWhiteSpace(csrfHash))
        {
            throw new ArgumentException("A CSRF hash is required.", nameof(csrfHash));
        }

        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        }

        Id = Guid.NewGuid();
        CredentialMigrationStateRecordId = credentialMigrationStateRecordId;
        TokenHash = tokenHash;
        ContextHash = contextHash;
        CsrfHash = csrfHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CredentialMigrationStateRecordId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public string ContextHash { get; private set; } = string.Empty;
    public string CsrfHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public long Version { get; private set; }

    public bool TryConsume(DateTimeOffset consumedAtUtc)
    {
        if (ConsumedAtUtc is not null || ExpiresAtUtc <= consumedAtUtc)
        {
            return false;
        }

        ConsumedAtUtc = consumedAtUtc;
        Version++;
        return true;
    }
}
