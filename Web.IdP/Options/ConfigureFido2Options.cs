using Fido2NetLib;
using Microsoft.Extensions.Options;

namespace Web.IdP.Options;

/// <summary>
/// Configures Fido2 options with advanced logic (fallbacks, string parsing).
/// </summary>
public class ConfigureFido2Options : IPostConfigureOptions<Fido2Configuration>
{
    private readonly IConfiguration _configuration;

    public ConfigureFido2Options(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void PostConfigure(string? name, Fido2Configuration options)
    {
        // 1. ServerName Fallback: Fido2:ServerName -> Branding:AppName -> Default
        // This allows customized RP Name display in the browser passkey dialog
        if (string.IsNullOrEmpty(options.ServerName) || options.ServerName == "HybridIdP")
        {
            options.ServerName = _configuration["Fido2:ServerName"] 
                              ?? _configuration["Branding:AppName"] 
                              ?? "HybridIdP";
        }

        // 2. Origins Parsing: Handle single or comma-separated string from Env Vars
        // This allows setting Fido2__Origins="https://a.com,https://b.com" in .env
        var originsString = _configuration["Fido2:Origins"];
        if (!string.IsNullOrEmpty(originsString))
        {
            options.Origins = new HashSet<string>(
                originsString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        
        // 3. Defaults
        if (string.IsNullOrEmpty(options.ServerDomain)) options.ServerDomain = "localhost";
        
        // Default 5 mins tolerance if not set
        if (options.TimestampDriftTolerance == 0) options.TimestampDriftTolerance = 300000;
    }
}
