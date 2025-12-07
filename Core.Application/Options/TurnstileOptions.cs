namespace Core.Application.Options;

public class TurnstileOptions
{
    public const string Section = "Turnstile";

    public bool Enabled { get; set; }
    public string SiteKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
}
