using Core.Application;
using Core.Domain.Constants;

namespace Infrastructure.Services;

public class BrandingService : IBrandingService
{
    private readonly ISettingsService _settings;
    private const string DefaultAppName = "HybridAuth";
    private const string DefaultProductName = "HybridAuth IdP";

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
}
