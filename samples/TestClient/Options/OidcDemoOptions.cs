namespace TestClient.Options;

public sealed class OidcDemoOptions
{
    public const string Section = "Oidc";
    public string Authority { get; set; } = "https://localhost:7035";
    public string ClientId { get; set; } = "testclient-public";
    public string[] Scopes { get; set; } = [];

    public string AuthorityUrl(string path) => $"{Authority.TrimEnd('/')}/{path.TrimStart('/')}";
}
