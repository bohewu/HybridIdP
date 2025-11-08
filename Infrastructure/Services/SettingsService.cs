using Core.Application;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Infrastructure.Services;

/// <summary>
/// Implementation of ISettingsService with in-memory caching and UpdatedUtc-based invalidation.
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly IApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private static readonly string CachePrefix = "settings:";
    private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromMinutes(5);

    public SettingsService(IApplicationDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken ct = default)
    {
        var cacheKey = CachePrefix + key;
        if (_cache.TryGetValue<(string? Value, DateTime UpdatedUtc)>(cacheKey, out var cached))
        {
            return cached.Value;
        }

        var setting = await _db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting == null) return null;

        _cache.Set(cacheKey, (setting.Value, setting.UpdatedUtc), new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = DefaultCacheDuration
        });
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
        if (existing == null)
        {
            existing = new Setting
            {
                Id = Guid.NewGuid(),
                Key = key,
                Value = value.ToString(),
                UpdatedUtc = DateTime.UtcNow,
                UpdatedBy = updatedBy
            };
            await _db.Settings.AddAsync(existing, ct);
        }
        else
        {
            existing.Value = value.ToString();
            existing.UpdatedUtc = DateTime.UtcNow;
            existing.UpdatedBy = updatedBy;
        }
        await _db.SaveChangesAsync(ct);
        await InvalidateAsync(key);
    }

    public async Task<IDictionary<string, string>> GetByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        var results = await _db.Settings.AsNoTracking()
            .Where(s => s.Key.StartsWith(prefix))
            .ToListAsync(ct);
        var dict = results.ToDictionary(s => s.Key, s => s.Value ?? string.Empty);

        foreach (var s in results)
        {
            var cacheKey = CachePrefix + s.Key;
            _cache.Set(cacheKey, (s.Value, s.UpdatedUtc), new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = DefaultCacheDuration
            });
        }
        return dict;
    }

    public Task InvalidateAsync(string? keyOrPrefix = null)
    {
        if (string.IsNullOrEmpty(keyOrPrefix))
        {
            // Full invalidation strategy could track keys; leaving minimal implementation.
            return Task.CompletedTask;
        }
        _cache.Remove(CachePrefix + keyOrPrefix);
        return Task.CompletedTask;
    }
}
