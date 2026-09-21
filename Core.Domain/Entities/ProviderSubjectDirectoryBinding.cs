namespace Core.Domain.Entities;

/// <summary>
/// Immutable association between an assured provider subject and a managed directory objectGUID.
/// </summary>
public sealed class ProviderSubjectDirectoryBinding
{
    private ProviderSubjectDirectoryBinding()
    {
    }

    public ProviderSubjectDirectoryBinding(
        Guid localAccountId,
        string providerNamespace,
        string stableSubject,
        Guid directoryObjectId,
        DateTime createdAtUtc,
        string? canonicalAccountAlias = null)
    {
        if (localAccountId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(localAccountId));
        }

        if (string.IsNullOrWhiteSpace(providerNamespace))
        {
            throw new ArgumentException("Provider namespace is required.", nameof(providerNamespace));
        }

        if (string.IsNullOrWhiteSpace(stableSubject))
        {
            throw new ArgumentException("Stable subject is required.", nameof(stableSubject));
        }

        if (directoryObjectId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(directoryObjectId));
        }

        Id = Guid.NewGuid();
        LocalAccountId = localAccountId;
        ProviderNamespace = providerNamespace;
        StableSubject = stableSubject;
        DirectoryObjectId = directoryObjectId;
        CreatedAtUtc = createdAtUtc;
        NormalizedCanonicalAccountAlias = NormalizeCanonicalAccountAlias(canonicalAccountAlias);
    }

    public Guid Id { get; private set; }
    public Guid LocalAccountId { get; private set; }
    public string ProviderNamespace { get; private set; } = string.Empty;
    public string StableSubject { get; private set; } = string.Empty;
    public Guid DirectoryObjectId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string? NormalizedCanonicalAccountAlias { get; private set; }

    public bool Matches(Guid localAccountId, Guid directoryObjectId) =>
        LocalAccountId == localAccountId && DirectoryObjectId == directoryObjectId;

    public void SetCanonicalAccountAlias(string? canonicalAccountAlias)
    {
        var normalizedAlias = NormalizeCanonicalAccountAlias(canonicalAccountAlias);
        if (normalizedAlias is not null)
        {
            NormalizedCanonicalAccountAlias = normalizedAlias;
        }
    }

    public static string? NormalizeCanonicalAccountAlias(string? canonicalAccountAlias)
    {
        var trimmed = canonicalAccountAlias?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToUpperInvariant();
    }
}
