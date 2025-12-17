using System.Collections.Concurrent;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Web.IdP.Services.Localization;

/// <summary>
/// Factory for creating JsonStringLocalizer instances.
/// Handles type-to-filename mapping and caches localizer instances.
/// Configured via JsonLocalizationOptions.
/// </summary>
public class JsonStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly string[] _searchPaths;
    private readonly ConcurrentDictionary<string, IStringLocalizer> _localizerCache;

    /// <summary>
    /// Creates a factory with options from DI.
    /// </summary>
    public JsonStringLocalizerFactory(IOptions<JsonLocalizationOptions> options)
    {
        _searchPaths = BuildSearchPaths(options.Value);
        _localizerCache = new ConcurrentDictionary<string, IStringLocalizer>();
    }

    /// <summary>
    /// Creates a factory with a single search path (for testing).
    /// </summary>
    public JsonStringLocalizerFactory(string resourcesPath)
    {
        _searchPaths = new[] { resourcesPath };
        _localizerCache = new ConcurrentDictionary<string, IStringLocalizer>();
    }

    /// <summary>
    /// Creates a factory with multiple search paths (for testing).
    /// </summary>
    public JsonStringLocalizerFactory(string[] searchPaths)
    {
        _searchPaths = searchPaths;
        _localizerCache = new ConcurrentDictionary<string, IStringLocalizer>();
    }

    private static string[] BuildSearchPaths(JsonLocalizationOptions options)
    {
        var searchPaths = new List<string>();
        var resourcesPath = options.ResourcesPath;
        
        // 1. Published/Bin directory - Resources folder relative to executable
        var basePath = AppContext.BaseDirectory;
        searchPaths.Add(Path.Combine(basePath, resourcesPath));
        
        // 2. Development - Resources folder relative to current directory
        var currentPath = Directory.GetCurrentDirectory();
        searchPaths.Add(Path.Combine(currentPath, resourcesPath));
        
        // 3. Scan additional assembly prefixes (e.g., Infrastructure, Core.Application)
        foreach (var prefix in options.AdditionalAssemblyPrefixes)
        {
            // Development: relative to current directory parent (solution root)
            searchPaths.Add(Path.Combine(currentPath, "..", prefix, resourcesPath));
            
            // Published: relative to base directory
            searchPaths.Add(Path.Combine(basePath, prefix, resourcesPath));
        }

        return searchPaths.ToArray();
    }

    public IStringLocalizer Create(Type resourceSource)
    {
        var baseName = resourceSource.Name;
        return GetOrCreateLocalizer(baseName);
    }

    public IStringLocalizer Create(string baseName, string location)
    {
        return GetOrCreateLocalizer(baseName);
    }

    private IStringLocalizer GetOrCreateLocalizer(string baseName)
    {
        return _localizerCache.GetOrAdd(baseName, name => new JsonStringLocalizer(name, _searchPaths));
    }
}
