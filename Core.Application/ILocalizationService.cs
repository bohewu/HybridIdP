using System.Threading.Tasks;

namespace Core.Application;

/// <summary>
/// Service for retrieving localized strings from the Resource table.
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Gets the localized string for the given key and culture.
    /// Falls back to default culture ("en-US") if not found.
    /// Returns null if no resource found.
    /// </summary>
    Task<string?> GetLocalizedStringAsync(string key, string culture);
}