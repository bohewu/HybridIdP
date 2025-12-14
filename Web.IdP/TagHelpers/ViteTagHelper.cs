using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Web.IdP.Services;

namespace Web.IdP.TagHelpers;

/// <summary>
/// Tag helper to render Vite scripts for both development and production.
/// Replaces Vite.AspNetCore functionality with custom, transparent logic.
/// Usage: <script type="module" vite-src="src/admin/claims/main.js"></script>
/// </summary>
[HtmlTargetElement("script", Attributes = "vite-src")]
public class ViteScriptTagHelper : TagHelper
{
    private readonly IViteManifestService _manifest;

    /// <summary>
    /// The entry path (e.g., "src/admin/claims/main.js")
    /// matches the key in manifest.json or path in dev server.
    /// </summary>
    [HtmlAttributeName("vite-src")]
    public string ViteSrc { get; set; } = string.Empty;

    public ViteScriptTagHelper(IViteManifestService manifest)
    {
        _manifest = manifest;
    }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (string.IsNullOrEmpty(ViteSrc))
        {
            return;
        }

        // Remove ~ and / prefixes to match manifest keys (e.g. "~/src/..." -> "src/...")
        string normalizedSrc = ViteSrc.TrimStart('~', '/');

        // Always remove the helper attribute
        output.Attributes.RemoveAll("vite-src");

        // Use the service to resolve the path (handles both Dev and Prod logic)
        var scriptPath = _manifest.GetScriptPath(normalizedSrc);

        if (!string.IsNullOrEmpty(scriptPath))
        {
            output.Attributes.SetAttribute("src", scriptPath);

            // In Production, we might need to inject CSS links
            if (!_manifest.IsDevelopment)
            {
                // Inject CSS links immediately before the script tag
                var cssPaths = _manifest.GetCssPaths(normalizedSrc);
                foreach (var css in cssPaths)
                {
                    output.PreElement.AppendHtml($"""<link rel="stylesheet" href="{css}" />""");
                }
            }
        }
        else
        {
            // Fallback or error logging
            output.Attributes.SetAttribute("data-vite-error", $"Entry '{normalizedSrc}' not found");
            
            // If in production and not found, maybe we shouldn't render the script tag at all?
            // For now, keeping the tag but with an error attribute is safer for debugging.
        }
    }
}
