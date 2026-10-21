using System.ComponentModel.DataAnnotations;
using Core.Application.DTOs;

namespace Core.Application.Ports;

public sealed record ProfileSyncStatusDto(bool Enabled, string? Reason);
public sealed record ProfileSyncChangeDto(string Target, string Field, string? CurrentValue,
    string? SourceValue, string? ProposedValue, string Action);
public sealed record ProfileSyncPreviewDto(Guid UserId, string? UserName, Guid? PersonId, string? ProviderNamespace,
    string Status, string? Reason, IReadOnlyList<ProfileSyncChangeDto> Changes, string? PreviewToken);
public sealed record ProfileSyncApplyRequest([Required, MaxLength(8192)] string PreviewToken);
public sealed record ProfileSyncApplyResult(string Status, IReadOnlyList<string> ChangedUserFields,
    IReadOnlyList<string> ChangedPersonFields);

public sealed class ProfileSyncException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public interface IProviderNativeProfileClient
{
    Task<ProviderNativeProfileResponse> FetchAsync(ProviderNativeProfileRequest request, CancellationToken cancellationToken = default);
}

public interface IAdminProfileSyncService
{
    Task<ProfileSyncStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
    // The actor key comes exclusively from the server's administrative authorization boundary.
    Task<ProfileSyncPreviewDto> PreviewAsync(string actorKey, Guid userId, CancellationToken cancellationToken = default);
    Task<ProfileSyncApplyResult> ApplyAsync(string actorKey, Guid userId, string previewToken, CancellationToken cancellationToken = default);
}
