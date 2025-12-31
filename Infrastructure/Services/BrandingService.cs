using Core.Application;
using Core.Domain.Constants;

namespace Infrastructure.Services;

public class BrandingService : IBrandingService
{
    private readonly ISettingsService _settings;
    private const string DefaultAppName = "HybridAuth";
    private const string DefaultProductName = "HybridAuth IdP";
    private const string DefaultCopyright = "© 2025";

    public BrandingService(ISettingsService settings)
    {
        _settings = settings;
    }

    public async Task<string> GetAppNameAsync(CancellationToken ct = default)
    {
        var fromDb = await _settings.GetValueAsync(SettingKeys.Branding.AppName, ct);
        return string.IsNullOrWhiteSpace(fromDb) ? DefaultAppName : fromDb;
    }

    public async Task<string> GetProductNameAsync(CancellationToken ct = default)
    {
        var fromDb = await _settings.GetValueAsync(SettingKeys.Branding.ProductName, ct);
        return string.IsNullOrWhiteSpace(fromDb) ? DefaultProductName : fromDb;
    }

    public async Task<string> GetCopyrightAsync(CancellationToken ct = default)
    {
        var fromDb = await _settings.GetValueAsync(SettingKeys.Branding.Copyright, ct);
        if (string.IsNullOrWhiteSpace(fromDb))
        {
            return $"© {DateTime.Now.Year}";
        }
        
        var val = fromDb.Trim();
        
        // Smart fix for common typing habits: "(c)" -> "©"
        if (val.StartsWith("(c)", StringComparison.OrdinalIgnoreCase))
        {
            val = "©" + val.Substring(3);
        }
        // Smart fix for "c 2025" -> "© 2025"
        else if (val.StartsWith("c ", StringComparison.OrdinalIgnoreCase) && val.Length > 2 && char.IsDigit(val[2]))
        {
             val = "©" + val.Substring(1);
        }
        
        return val;
    }

    public async Task<string?> GetPoweredByAsync(CancellationToken ct = default)
    {
        var fromDb = await _settings.GetValueAsync(SettingKeys.Branding.PoweredBy, ct);
        return string.IsNullOrWhiteSpace(fromDb) ? null : fromDb;
    }
}
