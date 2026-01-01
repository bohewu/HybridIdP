using Core.Application;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace Infrastructure.Services;

/// <summary>
/// Service for retrieving localized strings from the Resource table.
/// </summary>
public class LocalizationService : ILocalizationService
{
    private readonly IApplicationDbContext _db;

    public LocalizationService(IApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Gets the localized string for the given key and culture.
    /// Falls back to default culture ("en-US") if not found.
    /// Returns null if no resource found or resource is disabled.
    /// </summary>
    public async Task<string?> GetLocalizedStringAsync(string key, string culture)
    {
        // Try exact culture match (only enabled resources)
        var resource = await _db.Resources
            .OrderBy(r => r.Id)
            .FirstOrDefaultAsync(r => r.Key == key && r.Culture == culture && r.IsEnabled);

        if (resource != null)
        {
            return resource.Value;
        }

        // Fallback to default culture
        if (culture != "en-US")
        {
            resource = await _db.Resources
                .OrderBy(r => r.Id)
                .FirstOrDefaultAsync(r => r.Key == key && r.Culture == "en-US" && r.IsEnabled);

            if (resource != null)
            {
                return resource.Value;
            }
        }

        // No resource found
        return null;
    }
}