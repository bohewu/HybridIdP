using IdentityModel.Client;
using System.Text.Json;

namespace TestClient.M2M;

class Program
{
    private const string Authority = "https://localhost:7035";
    private const string ClientId = "testclient-m2m";
    private const string ClientSecret = "m2m-test-secret-2024";
    private const string Scope = "api:company:read api:company:write";

    // Allow self-signed certs (development only)
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

    static async Task Main(string[] args)
    {
        string? outputPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--output" && i + 1 < args.Length)
            {
                outputPath = args[i + 1];
            }
        }

        var result = new TestResult { Success = false };

        try
        {
            Console.WriteLine($"Discovering endpoints from {Authority}...");
            var discovery = await HttpClient.GetDiscoveryDocumentAsync(Authority);
            if (discovery.IsError) throw new Exception(discovery.Error);

            Console.WriteLine("Requesting Token (Client Credentials)...");
            var tokenResponse = await HttpClient.RequestClientCredentialsTokenAsync(new ClientCredentialsTokenRequest
            {
                Address = discovery.TokenEndpoint,
                ClientId = ClientId,
                ClientSecret = ClientSecret,
                Scope = Scope
            });

            if (tokenResponse.IsError) throw new Exception($"Token Request Error: {tokenResponse.Error}");
            Console.WriteLine($"Token received: {tokenResponse.AccessToken![..10]}...");

            Console.WriteLine("Introspecting Token...");
            var introspection = await HttpClient.IntrospectTokenAsync(new TokenIntrospectionRequest
            {
                Address = discovery.IntrospectionEndpoint,
                ClientId = ClientId,
                ClientSecret = ClientSecret,
                Token = tokenResponse.AccessToken
            });

            if (introspection.IsError) throw new Exception($"Introspection Error: {introspection.Error}");
            if (!introspection.IsActive) throw new Exception("Token is not active");
            Console.WriteLine("Token is active.");

            Console.WriteLine("Revoking Token...");
            var revocation = await HttpClient.RevokeTokenAsync(new TokenRevocationRequest
            {
                Address = discovery.RevocationEndpoint,
                ClientId = ClientId,
                ClientSecret = ClientSecret,
                Token = tokenResponse.AccessToken
            });

            if (revocation.IsError) throw new Exception($"Revocation Error: {revocation.Error}");
            Console.WriteLine("Token revoked.");

            result.Success = true;
            result.Message = "All flows completed successfully.";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test Failed: {ex.Message}");
            result.Success = false;
            result.Message = ex.Message;
        }

        if (!string.IsNullOrEmpty(outputPath))
        {
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(outputPath, json);
            Console.WriteLine($"Result written to {outputPath}");
        }
        else
        {
            // If no output file, exit code indicates success/failure
            Environment.Exit(result.Success ? 0 : 1);
        }
    }
}

public class TestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
