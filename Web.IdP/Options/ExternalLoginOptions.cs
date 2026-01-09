namespace Web.IdP.Options;

public class ExternalLoginOptions
{
    public const string Section = "ExternalLogin";

    public bool AutoLinkMatchingEmail { get; set; } = false;
    public ProviderOptions Google { get; set; } = new();
    public ProviderOptions Microsoft { get; set; } = new();
}

public class ProviderOptions
{
    public bool Enabled { get; set; } = false;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}
