using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Core.Domain;
using Core.Domain.Entities;

namespace Core.Application.Ports;

[JsonConverter(typeof(JsonStringEnumConverter<NativeField>))]
public enum NativeField { Nickname, FirstName, LastName, Department, JobTitle, EmployeeId }
[JsonConverter(typeof(JsonStringEnumConverter<NativeFieldTarget>))]
public enum NativeFieldTarget { User, Person }
[JsonConverter(typeof(JsonStringEnumConverter<NativeFieldSource>))]
public enum NativeFieldSource { Proof, Stage1 }
[JsonConverter(typeof(JsonStringEnumConverter<NativeFieldOperation>))]
public enum NativeFieldOperation { AssignSource, Manual, Unlock }
[JsonConverter(typeof(JsonStringEnumConverter<ProofUserNameStrategy>))]
public enum ProofUserNameStrategy { CanonicalAccount, Email }
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProofUserNamePolicyDto([property: JsonRequired] NativeFieldSource Source,
    [property: JsonRequired][Required, MaxLength(200)] string ProviderNamespace,
    [property: JsonRequired] ProofUserNameStrategy Strategy);

public sealed record NativeFieldSourceIdentity(
    NativeFieldSource Source, Guid LocalAccountId,
    [Required, MaxLength(200)] string ProviderNamespace,
    [Required, MaxLength(256)] string StableSubject,
    Guid? DirectoryObjectId = null);

public sealed record NativeFieldSourceStatusDto(NativeFieldSource Source, bool Enabled, bool Paused,
    string? UnavailableReason, IReadOnlyList<NativeField> SupportedFields, bool SupportsClear,
    string ClearUnavailableReason, Guid Revision, string? ProviderNamespace = null,
    bool FirstNameDisplayNameFallback = false);

public sealed record NativeFieldStateDto(NativeField Field, string? Value, bool ManualFixed,
    bool Attributed, NativeFieldSourceIdentity? Owner, bool AllowOverwrite);
public sealed record NativeFieldTargetDto(NativeFieldTarget Target, Guid TargetId, Guid Revision,
    IReadOnlyList<NativeFieldStateDto> Fields, IReadOnlyList<NativeFieldSourceIdentity> AvailableBindings);

public sealed class NativeFieldUpdateRequest
{
    public NativeFieldOperation Operation { get; set; }
    public Guid ExpectedRevision { get; set; }
    public NativeFieldSourceIdentity? SourceIdentity { get; set; }
    public bool AllowOverwrite { get; set; }
    public bool AllowSourceClear { get; set; }
    [MaxLength(256)] public string? ManualValue { get; set; }
}

public sealed record NativeFieldPauseRequest(bool Paused, Guid ExpectedRevision);
public sealed record NativeFieldFirstNameFallbackRequest(
    [Required, MaxLength(200)] string ProviderNamespace, bool Enabled, Guid ExpectedRevision);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NativeFieldMappingDto([property: JsonRequired] NativeField Field, [property: JsonRequired] NativeFieldSource Source,
    [property: JsonRequired][Required, MaxLength(200)] string ProviderNamespace,
    [property: JsonRequired][Required] string SourceProperty);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NativeFieldMappingFallbackDto([property: JsonRequired] string? ProviderNamespace, [property: JsonRequired] bool Enabled);
public sealed record NativeFieldMappingSourceDto(NativeFieldSource Source, string ProviderNamespace, bool Enabled, bool Paused,
    bool UserNamePolicyAvailable = false);
public sealed record NativeFieldMappingsDto(Guid Revision, IReadOnlyList<NativeFieldMappingDto> Mappings,
    NativeFieldMappingFallbackDto FirstNameFallback, IReadOnlyList<NativeFieldMappingSourceDto> Sources,
    IReadOnlyList<string> SourceProperties, IReadOnlyList<ProofUserNamePolicyDto>? UserNamePolicies = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NativeFieldMappingsRequest(Guid ExpectedRevision,
    [Required] IReadOnlyList<NativeFieldMappingDto> Mappings, [Required] NativeFieldMappingFallbackDto FirstNameFallback,
    IReadOnlyList<ProofUserNamePolicyDto>? UserNamePolicies = null);
public sealed class NativeFieldPolicyException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public interface INativeFieldPolicyService
{
    Task<ProofUserNameStrategy> GetProofUserNameStrategyAsync(string providerNamespace, CancellationToken cancellationToken = default);
    Task<NativeFieldMappingsDto> GetMappingsAsync(CancellationToken cancellationToken = default);
    Task<NativeFieldMappingsDto> SetMappingsAsync(NativeFieldMappingsRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NativeFieldSourceStatusDto>> GetSourcesAsync(CancellationToken cancellationToken = default);
    Task SetPausedAsync(NativeFieldSource source, NativeFieldPauseRequest request, CancellationToken cancellationToken = default);
    Task SetFirstNameFallbackAsync(NativeFieldSource source, NativeFieldFirstNameFallbackRequest request,
        CancellationToken cancellationToken = default);
    Task<NativeFieldTargetDto> GetTargetAsync(NativeFieldTarget target, Guid targetId, CancellationToken cancellationToken = default);
    Task UpdateAsync(NativeFieldTarget target, Guid targetId, NativeField field, NativeFieldUpdateRequest request,
        CancellationToken cancellationToken = default);

    // Prepare only: caller must save the tracked entity. Value and protection share its EF concurrency token.
    // A failed save must discard/reload the entity before any later save; never retry stale mutations.
    Task<bool> ApplySourceAsync(ApplicationUser user, Person? person, NativeFieldSourceIdentity identity,
        IReadOnlyDictionary<NativeField, string?> values, CancellationToken cancellationToken = default);
    // Prepare only, including for detached preview entities. Always preserve populated fields, even with overwrite enabled.
    Task<IReadOnlyList<ProfileSyncChangeDto>> ApplyMissingSourceAsync(ApplicationUser user, Person person,
        NativeFieldSourceIdentity identity, IReadOnlyDictionary<NativeField, string?> values,
        CancellationToken cancellationToken = default);
    void ApplyManual(ApplicationUser user, IReadOnlyDictionary<NativeField, string?> values);
    void ApplyManual(Person person, IReadOnlyDictionary<NativeField, string?> values);
    // Existing unadvertised browser providers may fill empty unowned fields, never fixed or attributed fields.
    void ApplyUnattributed(ApplicationUser user, Person? person, IReadOnlyDictionary<NativeField, string?> values);
}
