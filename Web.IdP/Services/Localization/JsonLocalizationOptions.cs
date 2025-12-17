namespace Web.IdP.Services.Localization;

/// <summary>
/// Configuration options for JSON localization.
/// </summary>
public class JsonLocalizationOptions
{
    /// <summary>
    /// Gets or sets the relative path under application root where resources are located.
    /// Default is "Resources".
    /// </summary>
    public string ResourcesPath { get; set; } = "Resources";

    /// <summary>
    /// Gets or sets additional assembly prefixes to scan for resources.
    /// For each prefix, the factory will look for resources in "{prefix}/Resources".
    /// Example: ["Infrastructure", "Core.Application"] will scan Infrastructure/Resources, Core.Application/Resources.
    /// </summary>
    public List<string> AdditionalAssemblyPrefixes { get; set; } = new();
}
