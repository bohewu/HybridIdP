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
    private readonly IWebHostEnvironment _env;
    private readonly IViteManifestService _manifest;

    /// <summary>
    /// The entry path (e.g., "src/admin/claims/main.js")
    /// matches the key in manifest.json or path in dev server.
    /// </summary>
    [HtmlAttributeName("vite-src")]
    public string ViteSrc { get; set; } = string.Empty;

    public ViteScriptTagHelper(IWebHostEnvironment env, IViteManifestService manifest)
    {
        _env = env;
        _manifest = manifest;
    }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (string.IsNullOrEmpty(ViteSrc))
        {
            return;
        }

        // Remove ~ and / prefixes to match manifest keys (e.g. "~/src/..." -> "src/...")
        ViteSrc = ViteSrc.TrimStart('~', '/');

        // Always remove the helper attribute
        output.Attributes.RemoveAll("vite-src");

        if (_env.IsDevelopment())
        {
            // Development mode: use Vite dev server
            // Assuming default port 5173
            var devServerUrl = "http://localhost:5173";
            output.Attributes.SetAttribute("src", $"{devServerUrl}/{ViteSrc}");
        }
        else
        {
            // Production mode: use manifest to get correct paths
            var scriptPath = _manifest.GetScriptPath(ViteSrc);
            
            if (!string.IsNullOrEmpty(scriptPath))
            {
                output.Attributes.SetAttribute("src", scriptPath);

                // Inject CSS links immediately before the script tag
                // Note: ideally CSS should be in <head>, but standard Vite injects them alongside JS often works, 
                // or we can just rely on the script importing them (which Vite does via module import).
                // However, for initial paint, link tags are better.
                // Since this is a TagHelper on <script>, we can only append/prepend to it or surround it.
                // We will use PreContent to inject CSS.
                
                var cssPaths = _manifest.GetCssPaths(ViteSrc);
                foreach (var css in cssPaths)
                {
                    output.PreElement.AppendHtml($"""<link rel="stylesheet" href="{css}" />""");
                }
                
                // We can also handle imports for preloading if desired, but keeping it simple for now.
            }
            else
            {
                // Fallback or error logging? For now, leave src empty or warn
                output.Attributes.SetAttribute("data-vite-error", $"Entry '{ViteSrc}' not found in manifest");
            }
        }
    }
}
