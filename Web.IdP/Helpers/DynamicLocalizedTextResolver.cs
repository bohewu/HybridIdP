using Core.Application;

namespace Web.IdP.Helpers;

public static class DynamicLocalizedTextResolver
{
    public static async Task<string?> ResolveAsync(
        string? configuredValue,
        string culture,
        ILocalizationService localizationService)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return null;
        }

        if (!configuredValue.StartsWith('@'))
        {
            return configuredValue;
        }

        var key = configuredValue[1..].Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var localizedValue = await localizationService.GetLocalizedStringAsync(key, culture);
        return string.IsNullOrWhiteSpace(localizedValue) ? null : localizedValue;
    }
}
