namespace Infrastructure.Options;

public class LegacyAuthOptions
{
    public const string SectionName = "LegacyAuth";
    public string LoginUrl { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
}
