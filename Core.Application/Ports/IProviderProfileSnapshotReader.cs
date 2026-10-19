using System.Text.Json;
using Core.Domain;

namespace Core.Application.Ports;

/// <summary>Evaluates stored confirmation without fetching, caching or saving profile data.</summary>
public interface IProviderProfileSnapshotReader
{
    Task<ProviderProfileSnapshotResult> ReadSnapshotAsync(ApplicationUser user, string source,
        CancellationToken cancellationToken = default);
}

public sealed record ProviderProfileSnapshotResult(string Status,
    IReadOnlyDictionary<string, JsonElement> Properties, DateTimeOffset? ConfirmedAt = null,
    DateTimeOffset? ExpiresAt = null);
