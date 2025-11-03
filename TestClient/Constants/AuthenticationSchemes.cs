namespace TestClient.Constants;

/// <summary>
/// Authentication scheme constants for TestClient.
/// </summary>
public static class AuthenticationSchemes
{
    /// <summary>
    /// Cookie authentication scheme name.
    /// Used for maintaining user session after OIDC authentication.
    /// </summary>
    public const string Cookies = "Cookies";

    /// <summary>
    /// OpenID Connect authentication scheme name.
    /// Used for challenging users to authenticate with the IdP.
    /// </summary>
    public const string OpenIdConnect = "oidc";
}
