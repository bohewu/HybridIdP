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
public sealed class NativeFieldPolicyException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public interface INativeFieldPolicyService
{
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
