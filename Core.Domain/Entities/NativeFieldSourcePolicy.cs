namespace Core.Domain.Entities;

/// <summary>Native profile sync policy; unrelated to credential enablement.</summary>
public sealed class NativeFieldSourcePolicy
{
    public string Source { get; set; } = string.Empty;
    public bool Paused { get; set; }
    public Guid Revision { get; set; }
    public string? FirstNameFallbackProviderNamespace { get; set; }
    public bool FirstNameDisplayNameFallback { get; set; }
}
