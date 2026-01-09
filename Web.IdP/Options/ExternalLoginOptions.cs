namespace Web.IdP.Options;

public class ExternalLoginOptions
{
    public const string Section = "ExternalLogin";

    /// <summary>
    /// Automatically link external login to existing user if email matches.
    /// </summary>
    public bool AutoLinkMatchingEmail { get; set; } = false;

    /// <summary>
    /// Maximum number of external logins allowed per provider (e.g., max 2 Google accounts).
    /// Set to 0 for unlimited. Default is 2.
    /// </summary>
    public int MaxLoginsPerProvider { get; set; } = 2;
    public ProviderOptions Google { get; set; } = new();
    public ProviderOptions Microsoft { get; set; } = new();
}

public class ProviderOptions
{
    public bool Enabled { get; set; } = false;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    
    /// <summary>
    /// Google specific: Restrict sign-in to a specific Google Workspace domain (e.g. "company.com").
    /// Adds 'hd' parameter to authorization request.
    /// </summary>
    public string? HostedDomain { get; set; }

    /// <summary>
    /// Force the account chooser to appear on every login.
    /// Adds 'prompt=select_account' to authorization request.
    /// Recommended to prevent users from getting stuck with a wrong default account.
    /// </summary>
    public bool ForceAccountSelection { get; set; } = true;
}
