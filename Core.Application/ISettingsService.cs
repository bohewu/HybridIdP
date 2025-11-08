using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Application;

public interface ISettingsService
{
    Task<string?> GetValueAsync(string key, CancellationToken ct = default);
    Task<T?> GetValueAsync<T>(string key, CancellationToken ct = default);
    Task SetValueAsync(string key, object value, string? updatedBy = null, CancellationToken ct = default);
    Task<IDictionary<string, string>> GetByPrefixAsync(string prefix, CancellationToken ct = default);
    /// <summary>
    /// Invalidate cache for a specific key or all keys with a given prefix; when null, invalidates all tracked keys.
    /// </summary>
    Task InvalidateAsync(string? keyOrPrefix = null);
}