using System.Diagnostics;
using System.Text.Json;

namespace TestClient.Device;

class Program
{
    private const string Authority = "https://localhost:7035";
    private const string ClientId = "testclient-device";
    
    // Allow self-signed certs (development only) and disable redirects
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        AllowAutoRedirect = false
    });

    static async Task Main(string[] args)
    {
        Console.WriteLine($"Requesting Device Code from {Authority}...");

        try
        {
            // 1. Request Device Code
            var request = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", ClientId),
                new KeyValuePair<string, string>("scope", "openid profile offline_access")
            });

            var response = await HttpClient.PostAsync($"{Authority}/connect/device", request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Error requesting device code: {response.StatusCode}");
                Console.WriteLine("Raw Response:");
                Console.WriteLine(content);
                return;
            }

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var userCode = root.GetProperty("user_code").GetString();
            var deviceCode = root.GetProperty("device_code").GetString();
            var verificationUri = root.GetProperty("verification_uri_complete").GetString();
            var expiresIn = root.GetProperty("expires_in").GetInt32();
            var interval = root.TryGetProperty("interval", out var intervalProp) ? intervalProp.GetInt32() : 5;

            Console.WriteLine("--------------------------------------------------------");
            Console.WriteLine("Device Code Request Successful!");
            Console.WriteLine($"User Code:        {userCode}");
            Console.WriteLine($"Verification URL: {verificationUri}");
            Console.WriteLine("--------------------------------------------------------");
            Console.WriteLine("Opening browser...");

            try
            {
                Process.Start(new ProcessStartInfo(verificationUri!) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not open browser automatically: {ex.Message}");
                Console.WriteLine("Please open the URL manually.");
            }

            Console.WriteLine("Waiting for user approval...");

            // 2. Poll for Token
            await PollForTokenAsync(deviceCode!, interval, expiresIn);

        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
        }
    }

    private static async Task PollForTokenAsync(string deviceCode, int interval, int expiresIn)
    {
        var startTime = DateTime.UtcNow;
        var timeout = startTime.AddSeconds(expiresIn);

        while (DateTime.UtcNow < timeout)
        {
            var request = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
                new KeyValuePair<string, string>("device_code", deviceCode),
                new KeyValuePair<string, string>("client_id", ClientId)
            });

            var response = await HttpClient.PostAsync($"{Authority}/connect/token", request);
            var content = await response.Content.ReadAsStringAsync();
            
            // Debug: Show raw response when not successful JSON
            if (string.IsNullOrWhiteSpace(content))
            {
                Console.WriteLine($"\n[DEBUG] Empty response with status {response.StatusCode}");
                Console.WriteLine($"[DEBUG] Headers: {string.Join(", ", response.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}"))}");
                await Task.Delay(interval * 1000);
                continue;
            }
            
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(content);
            }
            catch (JsonException)
            {
                Console.WriteLine($"\n[DEBUG] Non-JSON response with status {response.StatusCode}:");
                Console.WriteLine($"[DEBUG] Content: {content[..Math.Min(500, content.Length)]}");
                return;
            }
            
            using (doc)
            {
                var root = doc.RootElement;

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("");
                    Console.WriteLine("--------------------------------------------------------");
                    Console.WriteLine("Device Flow Completed Successfully!", ConsoleColor.Green);
                    Console.WriteLine("Access Token received.");
                    
                    if (root.TryGetProperty("access_token", out var tokenProp))
                        Console.WriteLine($"Access Token: {tokenProp.GetString()![..20]}...");
                    
                    if (root.TryGetProperty("refresh_token", out var refreshProp))
                        Console.WriteLine($"Refresh Token: {refreshProp.GetString()![..20]}...");
                    
                    Console.WriteLine("--------------------------------------------------------");
                    return;
                }
                else
                {
                    string error = "unknown";
                    if (root.TryGetProperty("error", out var errorProp))
                        error = errorProp.GetString()!;

                    if (error == "authorization_pending")
                    {
                        Console.Write(".");
                        await Task.Delay(interval * 1000);
                    }
                    else if (error == "slow_down")
                    {
                        Console.Write("s");
                        interval += 5;
                        await Task.Delay(interval * 1000);
                    }
                    else
                    {
                        Console.WriteLine("");
                        Console.WriteLine($"Error: {error}");
                        Console.WriteLine(content);
                        return;
                    }
                }
            }
        }

        Console.WriteLine("");
        Console.WriteLine("Device Flow Timeout.");
    }
}
