namespace Web.IdP.Extensions;

public static class PublicOriginConfigurationExtensions
{
    private const string IssuerConfigurationKey = "OpenIddict:Issuer";
    private const string PublicAuthorityConfigurationKey = "PUBLIC_AUTHORITY";

    public static WebApplicationBuilder ConfigurePublicOrigin(
        this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!builder.Environment.IsProduction())
        {
            return builder;
        }

        var issuerValue = builder.Configuration[IssuerConfigurationKey];
        if (!TryParsePublicOrigin(issuerValue, out var issuer))
        {
            throw new InvalidOperationException(
                $"{IssuerConfigurationKey} must be configured in Production as an absolute " +
                "HTTPS origin with no path, query, fragment, or user information.");
        }

        var publicAuthority = builder.Configuration[PublicAuthorityConfigurationKey];
        if (publicAuthority is not null &&
            (string.IsNullOrWhiteSpace(publicAuthority) ||
             !string.Equals(publicAuthority, issuer.Authority, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"{PublicAuthorityConfigurationKey} must exactly match the authority in " +
                $"{IssuerConfigurationKey}.");
        }

        builder.Configuration[IssuerConfigurationKey] =
            issuer.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped) + "/";
        builder.Configuration["AllowedHosts"] = issuer.HostNameType == UriHostNameType.IPv6
            ? $"[{issuer.IdnHost}]"
            : issuer.IdnHost;

        return builder;
    }

    private static bool TryParsePublicOrigin(string? value, out Uri issuer)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out issuer!) ||
            !string.Equals(issuer.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(issuer.Host) ||
            issuer.HostNameType == UriHostNameType.Unknown ||
            !string.IsNullOrEmpty(issuer.UserInfo) ||
            issuer.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(issuer.Query) ||
            !string.IsNullOrEmpty(issuer.Fragment))
        {
            issuer = null!;
            return false;
        }

        return true;
    }
}
