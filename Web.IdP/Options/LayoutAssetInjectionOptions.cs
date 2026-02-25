namespace Web.IdP.Options;

public class LayoutAssetInjectionOptions
{
    public const string Section = "LayoutAssets";

    public List<string> ExternalCssLinks { get; set; } = [];
    public List<string> ExternalJsLinks { get; set; } = [];
    public List<ExternalScriptTagOptions> ExternalScriptTags { get; set; } = [];

    public IEnumerable<string> GetValidatedExternalCssLinks() => FilterHttpLinks(ExternalCssLinks);
    public IEnumerable<string> GetValidatedExternalJsLinks() => FilterHttpLinks(ExternalJsLinks);
    public IEnumerable<ValidatedExternalScriptTag> GetValidatedExternalScriptTags() => FilterExternalScriptTags(ExternalScriptTags);

    public class ExternalScriptTagOptions
    {
        public string Src { get; set; } = string.Empty;
        public bool Async { get; set; }
        public bool Defer { get; set; }
        public string? Integrity { get; set; }
        public string? CrossOrigin { get; set; }
        public string? ReferrerPolicy { get; set; }
        public List<DataAttributeOptions> DataAttributes { get; set; } = [];
    }

    public class DataAttributeOptions
    {
        public string? Name { get; set; }
        public string? Value { get; set; }
    }

    public class ValidatedExternalScriptTag
    {
        public string Src { get; init; } = string.Empty;
        public bool Async { get; init; }
        public bool Defer { get; init; }
        public string? Integrity { get; init; }
        public string? CrossOrigin { get; init; }
        public string? ReferrerPolicy { get; init; }
        public IReadOnlyDictionary<string, string> DataAttributes { get; init; } = new Dictionary<string, string>();
    }

    private static IEnumerable<string> FilterHttpLinks(IEnumerable<string>? links)
    {
        if (links is null)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
        {
            if (!TryGetValidHttpLink(link, out var trimmed))
            {
                continue;
            }

            if (seen.Add(trimmed))
            {
                yield return trimmed;
            }
        }
    }

    private static IEnumerable<ValidatedExternalScriptTag> FilterExternalScriptTags(IEnumerable<ExternalScriptTagOptions>? scriptTags)
    {
        if (scriptTags is null)
        {
            yield break;
        }

        foreach (var scriptTag in scriptTags)
        {
            if (scriptTag is null)
            {
                continue;
            }

            if (!TryGetValidHttpLink(scriptTag.Src, out var src))
            {
                continue;
            }

            yield return new ValidatedExternalScriptTag
            {
                Src = src,
                Async = scriptTag.Async,
                Defer = scriptTag.Defer,
                Integrity = NormalizeOptionalValue(scriptTag.Integrity),
                CrossOrigin = NormalizeOptionalValue(scriptTag.CrossOrigin),
                ReferrerPolicy = NormalizeOptionalValue(scriptTag.ReferrerPolicy),
                DataAttributes = NormalizeDataAttributes(scriptTag.DataAttributes)
            };
        }
    }

    private static Dictionary<string, string> NormalizeDataAttributes(IEnumerable<DataAttributeOptions>? dataAttributes)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (dataAttributes is null)
        {
            return normalized;
        }

        foreach (var dataAttribute in dataAttributes)
        {
            if (dataAttribute is null)
            {
                continue;
            }

            var name = NormalizeDataAttributeName(dataAttribute.Name);
            var value = NormalizeOptionalValue(dataAttribute.Value);
            if (name is null || value is null)
            {
                continue;
            }

            normalized[name] = value;
        }

        return normalized;
    }

    private static string? NormalizeDataAttributeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalized = name.Trim();
        if (normalized.StartsWith("data-", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[5..];
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        foreach (var c in normalized)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != ':' && c != '.')
            {
                return null;
            }
        }

        return $"data-{normalized.ToLowerInvariant()}";
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool TryGetValidHttpLink(string? link, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(link))
        {
            return false;
        }

        var trimmed = link.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalized = trimmed;
        return true;
    }
}
