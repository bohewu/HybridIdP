using System.Text.Json.Serialization;
using Core.Application.DTOs;

namespace Core.Application.Ports;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QueryAttributeMappingDto(string Attribute, string ValueSource);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QueryConfigurationDto(string Capability, string SourceId, string WireMode,
    string ProviderNamespace, IReadOnlyList<QueryAttributeMappingDto> Mappings, string? QueryRule = null);
public sealed record QueryCandidateDto(string Attribute, string DataType, IReadOnlyList<string> ValueSources);
public sealed record QuerySourceDto(string Capability, string SourceId, string ProviderNamespace, bool Available,
    string? UnavailableReason, IReadOnlyList<QueryCandidateDto> AvailableCandidates,
    IReadOnlyList<string[]> AcceptedAttributeSets, string? QueryRule);
public sealed record QueryConfigurationStateDto(Guid Revision, IReadOnlyList<QueryConfigurationDto> Configurations,
    IReadOnlyList<QuerySourceDto> Sources);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QueryConfigurationRequest(Guid ExpectedRevision, IReadOnlyList<QueryConfigurationDto> Configurations);
public sealed record QueryApprovalStateDto(Guid Revision, bool Approved, IReadOnlyList<string> ValueSources,
    bool RequiresNationalIdAlgorithmApproval);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QueryApprovalRequest(Guid ExpectedRevision, bool Approved, bool NationalIdAlgorithmApproved = false);

// In-process evidence only. Never bind this type from a browser request. Callers supply a freshly
// validated Proof or exact-account protected directory result, with its original authority and scope.
public sealed record TrustedRuntimeQueryValue(string ValueSource, string Value, string ProviderNamespace,
    Guid LocalAccountId, string SecurityStamp, DateTimeOffset ValidatedAt, bool Assured);
public sealed record NeutralQueryTransport(Uri Endpoint, string SharedSecret, TimeSpan Timeout);
public sealed record NeutralQueryContext(Guid LocalAccountId, Guid? PersonId, string SourceId,
    NeutralQueryRequest Request, string Fingerprint, Guid ConfigurationRevision,
    NeutralQueryTransport Transport, IReadOnlyList<TrustedRuntimeQueryValue> RuntimeValues);
public sealed record NeutralQueryResolution(string Status, NeutralQueryContext? Context = null);
public sealed class NeutralQueryException(string code) : Exception(code) { public string Code { get; } = code; }

public interface INeutralQueryService
{
    Task<QueryConfigurationStateDto> GetConfigurationAsync(CancellationToken cancellationToken = default);
    Task<QueryConfigurationStateDto> SetConfigurationAsync(QueryConfigurationRequest request, CancellationToken cancellationToken = default);
    Task<QueryApprovalStateDto> GetApprovalAsync(string capability, string sourceId, Guid userId, CancellationToken cancellationToken = default);
    Task<QueryApprovalStateDto> SetApprovalAsync(string capability, string sourceId, Guid userId,
        QueryApprovalRequest request, CancellationToken cancellationToken = default);
    Task<NeutralQueryResolution> ResolveAsync(string capability, Guid userId, string? sourceId = null,
        IReadOnlyList<TrustedRuntimeQueryValue>? runtimeValues = null, CancellationToken cancellationToken = default);
    Task<bool> IsCurrentAsync(NeutralQueryContext context, CancellationToken cancellationToken = default);
}
