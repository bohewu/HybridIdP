namespace Web.IdP.Options;

public class CspExtensionOptions
{
    public const string Section = "SecurityHeaders:Csp";

    public List<string> ScriptSrc { get; set; } = [];
    public List<string> ScriptSrcElem { get; set; } = [];
    public List<string> StyleSrc { get; set; } = [];
    public List<string> StyleSrcElem { get; set; } = [];
    public List<string> ConnectSrc { get; set; } = [];
    public List<string> FrameSrc { get; set; } = [];
    public List<string> AdditionalDirectives { get; set; } = [];

    public IEnumerable<string> GetValidatedScriptSrc() => FilterSources(ScriptSrc);
    public IEnumerable<string> GetValidatedScriptSrcElem() => FilterSources(ScriptSrcElem);
    public IEnumerable<string> GetValidatedStyleSrc() => FilterSources(StyleSrc);
    public IEnumerable<string> GetValidatedStyleSrcElem() => FilterSources(StyleSrcElem);
    public IEnumerable<string> GetValidatedConnectSrc() => FilterSources(ConnectSrc);
    public IEnumerable<string> GetValidatedFrameSrc() => FilterSources(FrameSrc);
    public IEnumerable<string> GetValidatedAdditionalDirectives() => FilterDirectives(AdditionalDirectives);

    private static IEnumerable<string> FilterSources(IEnumerable<string>? values)
    {
        if (values is null)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = value.Trim();
            if (trimmed.Contains(';'))
            {
                continue;
            }

            if (seen.Add(trimmed))
            {
                yield return trimmed;
            }
        }
    }

    private static IEnumerable<string> FilterDirectives(IEnumerable<string>? values)
    {
        if (values is null)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = value.Trim();
            if (trimmed.Contains(';'))
            {
                continue;
            }

            if (seen.Add(trimmed))
            {
                yield return trimmed;
            }
        }
    }
}
