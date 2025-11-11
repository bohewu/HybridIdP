namespace Core.Application.DTOs;

/// <summary>
/// Summary of an OIDC client for list displays.
/// </summary>
public sealed class ClientSummary
{
    public string Id { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Type { get; set; } = string.Empty; // public | confidential
    public string ApplicationType { get; set; } = string.Empty; // web | native
    public string ConsentType { get; set; } = string.Empty;
    public int RedirectUrisCount { get; set; }
}

/// <summary>
/// Request model for creating a new OIDC client.
/// </summary>
public record CreateClientRequest(
    string ClientId,
    string? ClientSecret,
    string? DisplayName,
    string? ApplicationType,  // web, native
    string? Type,  // public, confidential
    string? ConsentType,
    List<string>? RedirectUris,
    List<string>? PostLogoutRedirectUris,
    List<string>? Permissions
);

/// <summary>
/// Request model for updating an existing OIDC client.
/// </summary>
public record UpdateClientRequest(
    string? ClientId,
    string? ClientSecret,
    string? DisplayName,
    string? Type,
    string? ConsentType,
    List<string>? RedirectUris,
    List<string>? PostLogoutRedirectUris,
    List<string>? Permissions
);

/// <summary>
/// Detailed information about an OIDC client.
/// </summary>
public sealed class ClientDetail
{
    public string Id { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Type { get; set; } = string.Empty;
    public string ConsentType { get; set; } = string.Empty;
    public string ApplicationType { get; set; } = string.Empty;
    public List<string> RedirectUris { get; set; } = new();
    public List<string> PostLogoutRedirectUris { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
}

/// <summary>
/// Response after creating a new OIDC client.
/// </summary>
public sealed class CreateClientResponse
{
    public string Id { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? ClientSecret { get; set; }  // Only returned for confidential clients
}
