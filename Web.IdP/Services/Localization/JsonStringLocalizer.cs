using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Localization;

namespace Web.IdP.Services.Localization;

/// <summary>
/// Custom JSON-based string localizer that reliably handles cross-project resource loading.
/// Replaces the unreliable My.Extensions.Localization.Json package.
/// </summary>
public class JsonStringLocalizer : IStringLocalizer
{
    private readonly string _baseName;
    private readonly string[] _searchPaths;
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _resourceCache;
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public JsonStringLocalizer(string baseName, string[] searchPaths)
    {
        _baseName = baseName;
        _searchPaths = searchPaths;
        _resourceCache = new ConcurrentDictionary<string, Dictionary<string, string>>();
    }

    public LocalizedString this[string name]
    {
        get
        {
            var value = GetString(name);
            return new LocalizedString(name, value ?? name, resourceNotFound: value == null);
        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var value = GetString(name);
            var formatted = value != null ? string.Format(value, arguments) : name;
            return new LocalizedString(name, formatted, resourceNotFound: value == null);
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        var culture = CultureInfo.CurrentUICulture;
        var resources = LoadResources(culture.Name);

        foreach (var kvp in resources)
        {
            yield return new LocalizedString(kvp.Key, kvp.Value, resourceNotFound: false);
        }

        if (includeParentCultures && culture.Parent != CultureInfo.InvariantCulture)
        {
            var parentResources = LoadResources(culture.Parent.Name);
            foreach (var kvp in parentResources)
            {
                if (!resources.ContainsKey(kvp.Key))
                {
                    yield return new LocalizedString(kvp.Key, kvp.Value, resourceNotFound: false);
                }
            }
        }
    }

    private string? GetString(string name)
    {
        var culture = CultureInfo.CurrentUICulture;
        
        // Try exact culture first (e.g., "zh-TW")
        var resources = LoadResources(culture.Name);
        if (resources.TryGetValue(name, out var value))
        {
            return value;
        }

        // Try parent culture (e.g., "zh")
        if (culture.Parent != CultureInfo.InvariantCulture)
        {
            resources = LoadResources(culture.Parent.Name);
            if (resources.TryGetValue(name, out value))
            {
                return value;
            }
        }

        // Fall back to default (no culture suffix)
        resources = LoadResources(null);
        if (resources.TryGetValue(name, out value))
        {
            return value;
        }

        return null;
    }

    private Dictionary<string, string> LoadResources(string? cultureName)
    {
        var cacheKey = $"{_baseName}_{cultureName ?? "default"}";
        
        return _resourceCache.GetOrAdd(cacheKey, _ =>
        {
            // Extract directory and file parts from baseName
            // baseName could be "Authorize" (simple) or "Views/Authorization/Authorize" (path-based)
            var directory = Path.GetDirectoryName(_baseName) ?? string.Empty;
            var fileBaseName = Path.GetFileName(_baseName);
            
            var fileName = string.IsNullOrEmpty(cultureName)
                ? $"{fileBaseName}.json"
                : $"{fileBaseName}.{cultureName}.json";

            foreach (var searchPath in _searchPaths)
            {
                // Combine searchPath with directory part of baseName
                var resourceDir = string.IsNullOrEmpty(directory)
                    ? searchPath
                    : Path.Combine(searchPath, directory);
                    
                var filePath = Path.Combine(resourceDir, fileName);
                if (File.Exists(filePath))
                {
                    try
                    {
                        var json = File.ReadAllText(filePath);
                        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, _jsonOptions)
                               ?? new Dictionary<string, string>();
                    }
                    catch (JsonException)
                    {
                        // Continue to next path on JSON parse error
                    }
                }
            }

            return new Dictionary<string, string>();
        });
    }
}

/// <summary>
/// Generic version of JsonStringLocalizer for dependency injection.
/// </summary>
/// <typeparam name="T">The marker type for the resource.</typeparam>
public class JsonStringLocalizer<T> : JsonStringLocalizer, IStringLocalizer<T>
{
    public JsonStringLocalizer(string[] searchPaths)
        : base(typeof(T).Name, searchPaths)
    {
    }
}
