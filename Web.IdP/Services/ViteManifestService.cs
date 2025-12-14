using System.Text.Json;

namespace Web.IdP.Services;

/// <summary>
/// Service to read Vite manifest and resolve asset paths for production builds.
/// </summary>
public interface IViteManifestService
{
    /// <summary>
    /// Gets the production script path for an entry point.
    /// </summary>
    string? GetScriptPath(string entryName);
    
    /// <summary>
    /// Gets CSS file paths for an entry point.
    /// </summary>
    IEnumerable<string> GetCssPaths(string entryName);
    
    /// <summary>
    /// Gets all import paths for an entry point (for preloading).
    /// </summary>
    IEnumerable<string> GetImportPaths(string entryName);

    /// <summary>
    /// Checks if the environment is development.
    /// </summary>
    bool IsDevelopment { get; }
}

public class ViteManifestService : IViteManifestService
{
    private readonly Dictionary<string, ViteManifestEntry> _manifest;
    private readonly string _basePath;
    private readonly bool _isDevelopment;
    private readonly string _devServerUrl;

    public bool IsDevelopment => _isDevelopment;

    public ViteManifestService(IWebHostEnvironment env, IConfiguration configuration)
    {
        _isDevelopment = env.IsDevelopment();
        _devServerUrl = configuration["Vite:DevServerUrl"] ?? "http://localhost:5173";

        // Read configuration with defaults
        _basePath = configuration["Vite:Base"] ?? "/dist/";
        var manifestRelativePath = configuration["Vite:Manifest"] ?? "dist/.vite/manifest.json";
        
        // Manifest is generated at wwwroot/dist/.vite/manifest.json
        var manifestPath = Path.Combine(env.WebRootPath, manifestRelativePath);
        
        if (File.Exists(manifestPath))
        {
            var json = File.ReadAllText(manifestPath);
            _manifest = JsonSerializer.Deserialize<Dictionary<string, ViteManifestEntry>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) 
                ?? new Dictionary<string, ViteManifestEntry>();
        }
        else
        {
            _manifest = new Dictionary<string, ViteManifestEntry>();
        }
    }

    public string? GetScriptPath(string entryName)
    {
        if (_isDevelopment)
        {
            // In dev mode, return the path to the Vite dev server
            return $"{_devServerUrl}/{entryName}";
        }

        // Try to find by entry source path pattern (e.g. "src/admin/claims/main.js")
        // The manifest keys are usually the relative path from source root
        if (_manifest.TryGetValue(entryName, out var entry))
        {
            return _basePath + entry.File;
        }
        
        return null;
    }

    public IEnumerable<string> GetCssPaths(string entryName)
    {
        if (_isDevelopment)
        {
            // In dev mode, CSS is usually injected by the JS bundle (HMR).
            // However, if we explicitly link a CSS file, Vite can serve it too.
            // But typically for Vue/Vite apps, we rely on the JS entry to inject styles.
            // If we MUST return a CSS path (e.g. for main.css which is an entry), we can.
            if (entryName.EndsWith(".css"))
            {
                 return new[] { $"{_devServerUrl}/{entryName}" };
            }
            return Enumerable.Empty<string>();
        }

        if (_manifest.TryGetValue(entryName, out var entry) && entry.Css != null)
        {
            return entry.Css.Select(css => _basePath + css);
        }
        return Enumerable.Empty<string>();
    }

    public IEnumerable<string> GetImportPaths(string entryName)
    {
        if (_isDevelopment)
        {
            yield break;
        }

        if (_manifest.TryGetValue(entryName, out var entry) && entry.Imports != null)
        {
            foreach (var import in entry.Imports)
            {
                if (_manifest.TryGetValue(import, out var importEntry))
                {
                    yield return _basePath + importEntry.File;
                }
            }
        }
    }
}

public class ViteManifestEntry
{
    public string File { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Src { get; set; }
    public bool? IsEntry { get; set; }
    public List<string>? Imports { get; set; }
    public List<string>? Css { get; set; }
}
