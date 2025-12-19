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
        string? outputPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--output" && i + 1 < args.Length)
            {
                outputPath = args[i + 1];
            }
        }

        var testResult = new TestResult { Success = false };

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
                testResult.Message = $"Error requesting device code: {response.StatusCode}";
                await WriteResultAsync(outputPath, testResult);
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

            if (!args.Contains("--no-browser"))
            {
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
            }
            else
            {
                Console.WriteLine("Browser auto-open disabled.");
            }

            Console.WriteLine("Waiting for user approval...");

            // 2. Poll for Token
            await PollForTokenAsync(deviceCode!, interval, expiresIn, outputPath, testResult);

        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            testResult.Message = ex.Message;
            await WriteResultAsync(outputPath, testResult);
        }
    }

    private static async Task PollForTokenAsync(string deviceCode, int interval, int expiresIn, string? outputPath, TestResult testResult)
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
            
            if (string.IsNullOrWhiteSpace(content))
            {
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
                Console.WriteLine($"\n[DEBUG] Non-JSON response with status {response.StatusCode}: {content[..Math.Min(100, content.Length)]}");
                return;
            }
            
            using (doc)
            {
                var root = doc.RootElement;

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("");
                    Console.WriteLine("--------------------------------------------------------");
                    Console.WriteLine("Device Flow Completed Successfully!");
                    Console.WriteLine("Access Token received.");
                    
                    if (root.TryGetProperty("access_token", out var tokenProp))
                        Console.WriteLine($"Access Token: {tokenProp.GetString()![..20]}...");
                    
                    if (root.TryGetProperty("refresh_token", out var refreshProp))
                        Console.WriteLine($"Refresh Token: {refreshProp.GetString()![..20]}...");
                    
                    Console.WriteLine("--------------------------------------------------------");
                    
                    testResult.Success = true;
                    testResult.Message = "Device Flow Completed Successfully";
                    await WriteResultAsync(outputPath, testResult);
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
                        
                        testResult.Message = $"Token Error: {error}";
                        await WriteResultAsync(outputPath, testResult);
                        return;
                    }
                }
            }
        }

        Console.WriteLine("");
        Console.WriteLine("Device Flow Timeout.");
        testResult.Message = "Device Flow Timeout";
        await WriteResultAsync(outputPath, testResult);
    }

    private static async Task WriteResultAsync(string? outputPath, TestResult result)
    {
        if (!string.IsNullOrEmpty(outputPath))
        {
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(outputPath, json);
            Console.WriteLine($"Result written to {outputPath}");
        }
    }
}

public class TestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
