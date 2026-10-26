namespace Core.Domain.Entities;

/// <summary>Hash-addressed state and replay marker; payloads are application-protected.</summary>
public sealed class SamlStateRecord
{
    public string KeyHash { get; set; } = string.Empty;
    public string ProtectedPayload { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}
