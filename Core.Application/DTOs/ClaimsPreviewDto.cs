using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Core.Application.DTOs;

public sealed class ClaimsPreviewRequest
{
    [Required, MaxLength(200)] public string ClientId { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    [Required, MaxLength(64)] public string[] Scopes { get; set; } = [];
}

public sealed record ClaimsPreviewReasonDto(string ClaimType, string Destination, string Status, string Code);
public sealed record ClaimsPreviewSourceDto(string Source, string Status, DateTimeOffset? ConfirmedAt, DateTimeOffset? ExpiresAt);
public sealed record ClaimsPreviewResponse(
    string ClientId, Guid UserId, string[] Scopes, bool HypotheticalScopes,
    Dictionary<string, JsonElement> IdToken, Dictionary<string, JsonElement> AccessToken,
    Dictionary<string, JsonElement> UserInfo, IReadOnlyList<ClaimsPreviewReasonDto> Reasons,
    IReadOnlyList<ClaimsPreviewSourceDto> Sources);
public sealed record ClaimsPreviewClientDto(string ClientId, string DisplayName, bool Supported);
public sealed record ClaimsPreviewAccountDto(Guid UserId, string DisplayName);
public sealed record ClaimsPreviewScopeDto(string Name, bool Usable);
public sealed record ClaimsPreviewClientScopesDto(string ClientId, bool Supported, IReadOnlyList<ClaimsPreviewScopeDto> Scopes);
