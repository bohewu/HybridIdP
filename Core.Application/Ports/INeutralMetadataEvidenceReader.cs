using Core.Domain.Entities;

namespace Core.Application.Ports;

public sealed record MetadataEmailEvidence(string SourceKey, string? QueryFingerprint,
    ProviderMetadataEvidenceState EvidenceState, string? Email, ProviderEmailTrustOrigin EmailTrustOrigin,
    DateTimeOffset? VerifiedAt, DateTimeOffset RefreshedAtUtc);
public sealed record NeutralMetadataEvidenceResult(bool Configured, IReadOnlyList<MetadataEmailEvidence> Evidence);
public interface INeutralMetadataEvidenceReader
{
    Task<NeutralMetadataEvidenceResult> ReadAsync(Guid userId, CancellationToken cancellationToken = default);
}
