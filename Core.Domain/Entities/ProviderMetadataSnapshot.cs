namespace Core.Domain.Entities;

public sealed class ProviderMetadataSnapshot
{
    private ProviderMetadataSnapshot()
    {
    }

    public ProviderMetadataSnapshot(Guid providerSubjectDirectoryBindingId, DateTimeOffset refreshedAtUtc)
    {
        if (providerSubjectDirectoryBindingId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(providerSubjectDirectoryBindingId));
        }

        Id = Guid.NewGuid();
        ProviderSubjectDirectoryBindingId = providerSubjectDirectoryBindingId;
        Invalidate(refreshedAtUtc);
    }

    public Guid Id { get; private set; }
    public Guid ProviderSubjectDirectoryBindingId { get; private set; }
    public ProviderMetadataEvidenceState EvidenceState { get; private set; }
    public string? Email { get; private set; }
    public ProviderEmailTrustOrigin EmailTrustOrigin { get; private set; }
    public DateTimeOffset? VerifiedAt { get; private set; }
    public DateTimeOffset RefreshedAtUtc { get; private set; }

    public void Refresh(
        string? email,
        ProviderEmailTrustOrigin emailTrustOrigin,
        DateTimeOffset? verifiedAt,
        DateTimeOffset refreshedAtUtc)
    {
        if (!Enum.IsDefined(emailTrustOrigin) || emailTrustOrigin == ProviderEmailTrustOrigin.Unknown ||
            string.IsNullOrWhiteSpace(email) || verifiedAt > refreshedAtUtc)
        {
            Invalidate(refreshedAtUtc, ProviderMetadataEvidenceState.Untrusted);
            return;
        }

        EvidenceState = ProviderMetadataEvidenceState.Available;
        Email = email;
        EmailTrustOrigin = emailTrustOrigin;
        VerifiedAt = verifiedAt;
        RefreshedAtUtc = refreshedAtUtc;
    }

    public void Invalidate(
        DateTimeOffset refreshedAtUtc,
        ProviderMetadataEvidenceState evidenceState = ProviderMetadataEvidenceState.Missing)
    {
        if (!Enum.IsDefined(evidenceState) || evidenceState == ProviderMetadataEvidenceState.Available)
        {
            throw new ArgumentOutOfRangeException(nameof(evidenceState));
        }

        EvidenceState = evidenceState;
        Email = null;
        EmailTrustOrigin = ProviderEmailTrustOrigin.Unknown;
        VerifiedAt = null;
        RefreshedAtUtc = refreshedAtUtc;
    }
}

public enum ProviderMetadataEvidenceState
{
    Missing,
    Available,
    Malformed,
    Unsupported,
    Stale,
    Untrusted,
    AuthenticationFailed,
    TimedOut,
    Unavailable
}

public enum ProviderEmailTrustOrigin
{
    Unknown,
    SourceVerified,
    PolicyTrusted
}
