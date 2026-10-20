namespace Web.IdP.Helpers;

/// <summary>
/// Product version embedded from VERSION when this application is built.
/// </summary>
public static class ProductVersionInfo
{
    public static string? Version { get; } = ReadVersion();

    private static string? ReadVersion()
    {
        using var stream = typeof(ProductVersionInfo).Assembly
            .GetManifestResourceStream("Web.IdP.ProductVersion");
        if (stream is null) return null;

        using var reader = new StreamReader(stream);
        var version = reader.ReadToEnd().Trim();
        return string.IsNullOrWhiteSpace(version) ? null : version;
    }
}
