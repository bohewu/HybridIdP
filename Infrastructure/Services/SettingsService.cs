using System.Collections.Concurrent;
using Core.Application;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Infrastructure.Services;

/// <summary>
/// Implementation of ISettingsService with in-memory caching and CancellationToken-based invalidation for prefixes.
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly IApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private static readonly string CachePrefix = "settings:";
    private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromMinutes(5);
    
    // Use a concurrent dictionary to manage cancellation tokens for prefix-based invalidation.
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _prefixCts = new();

    public SettingsService(IApplicationDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken ct = default)
    {
        var cacheKey = CachePrefix + key;
        if (_cache.TryGetValue(cacheKey, out string? value))
        {
            return value;
        }

        var setting = await _db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting == null) return null;

        var options = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = DefaultCacheDuration };
        
        // Link entry to a prefix-based cancellation token if applicable
        var prefix = GetPrefix(key);
        if (prefix != null)
        {
            var cts = _prefixCts.GetOrAdd(prefix, _ => new CancellationTokenSource());
            options.AddExpirationToken(new CancellationChangeToken(cts.Token));
        }

        _cache.Set(cacheKey, setting.Value, options);
        return setting.Value;
    }

    public async Task<T?> GetValueAsync<T>(string key, CancellationToken ct = default)
    {
        var raw = await GetValueAsync(key, ct);
        if (raw == null) return default;
        try
        {
            object? parsed = typeof(T) switch
            {
                var t when t == typeof(string) => raw,
                var t when t == typeof(int) && int.TryParse(raw, out var i) => i,
                var t when t == typeof(bool) && bool.TryParse(raw, out var b) => b,
                _ => System.Text.Json.JsonSerializer.Deserialize<T>(raw)
            };
            return (T?)parsed;
        }
        catch
        {
            return default;
        }
    }

    public async Task SetValueAsync(string key, object value, string? updatedBy = null, CancellationToken ct = default)
    {
        var existing = await _db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        var valueStr = value is string s ? s : System.Text.Json.JsonSerializer.Serialize(value);

        if (existing == null)
        {
            existing = new Setting
            {
                Id = Guid.NewGuid(),
                Key = key,
                Value = valueStr,
                UpdatedUtc = DateTime.UtcNow,
                UpdatedBy = updatedBy
            };
            await _db.Settings.AddAsync(existing, ct);
        }
        else
        {
            existing.Value = valueStr;
            existing.UpdatedUtc = DateTime.UtcNow;
            existing.UpdatedBy = updatedBy;
        }
        await _db.SaveChangesAsync(ct);
        
        // Invalidate cache for the specific key and its prefix
        await InvalidateAsync(key);
        var prefix = GetPrefix(key);
        if (prefix != null)
        {
            await InvalidateAsync(prefix);
        }
    }

    public async Task<IDictionary<string, string>> GetByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        var results = await _db.Settings.AsNoTracking()
            .Where(s => s.Key.StartsWith(prefix))
            .ToListAsync(ct);
        var dict = results.ToDictionary(s => s.Key, s => s.Value ?? string.Empty);

        // Add all items to cache and link them to the prefix cancellation token
        var cts = _prefixCts.GetOrAdd(prefix, _ => new CancellationTokenSource());
        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(DefaultCacheDuration)
            .AddExpirationToken(new CancellationChangeToken(cts.Token));

        foreach (var s in results)
        {
            var cacheKey = CachePrefix + s.Key;
            _cache.Set(cacheKey, s.Value, options);
        }
        return dict;
    }

    public Task InvalidateAsync(string? keyOrPrefix = null)
    {
        if (string.IsNullOrEmpty(keyOrPrefix))
        {
            // To invalidate all, we'd need to cancel all tokens.
            foreach (var key in _prefixCts.Keys)
            {
                if (_prefixCts.TryRemove(key, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                }
            }
            return Task.CompletedTask;
        }

        // If it's a prefix, cancel the token for that prefix.
        if (keyOrPrefix.EndsWith("."))
        {
            if (_prefixCts.TryRemove(keyOrPrefix, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }
        else // It's a single key
        {
            _cache.Remove(CachePrefix + keyOrPrefix);
        }
        
        return Task.CompletedTask;
    }
    
    private string? GetPrefix(string key)
    {
        var parts = key.Split('.');
        return parts.Length > 1 ? $"{parts[0]}." : null;
    }
}
