using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Tests.SystemTests;

[Collection("SystemTests")]
public class DeviceFlowSystemTests
{
    private const string Authority = "https://localhost:7035";
    private const string Username = "admin@hybridauth.local";
    private const string Password = "Admin@123";

    // Allow self-signed certs
    private static readonly HttpClientHandler HttpClientHandler = new()
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        AllowAutoRedirect = true,
        UseCookies = true,
        CookieContainer = new CookieContainer()
    };

    private static readonly HttpClient HttpClient = new(HttpClientHandler) { BaseAddress = new Uri(Authority) };

    [Fact]
    public async Task DeviceFlow_EndToEnd_ReturnsSuccess()
    {
        // Arrange
        var projectDir = GetProjectDirectory();
        var testClientDir = Path.Combine(projectDir, "..", "TestClient.Device");
        var outputPath = Path.Combine(Path.GetTempPath(), $"device_results_{Guid.NewGuid()}.json");
        
        // Ensure we are logged in first
        await LoginAsync();

        // Act - Start Client
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{testClientDir}\" -- --output \"{outputPath}\" --no-browser",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var output = new List<string>();
        var tcs = new TaskCompletionSource<string>();

        process.OutputDataReceived += (s, e) => 
        { 
            if (e.Data != null) 
            {
                output.Add(e.Data);
                // Look for User Code
                var match = Regex.Match(e.Data, @"User Code:\s+([A-Z0-9-]+)");
                if (match.Success)
                {
                    tcs.TrySetResult(match.Groups[1].Value);
                }
            }
        };
        process.ErrorDataReceived += (s, e) => { if (e.Data != null) output.Add(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Wait for User Code
        var userCodeTask = tcs.Task;
        if (await Task.WhenAny(userCodeTask, Task.Delay(10000)) != userCodeTask)
        {
            process.Kill();
            Assert.Fail($"Timed out waiting for User Code. Output:\n{string.Join("\n", output)}");
        }
        var userCode = await userCodeTask;
        
        // Act - Simulate User
        var content = await SubmitUserCodeAsync(userCode);
        await ConfirmConsentAsync(content);

        // Wait for process to finish
        await process.WaitForExitAsync();

        // Assert
        if (process.ExitCode != 0)
        {
            Assert.Fail($"Process failed with exit code {process.ExitCode}.\nOutput: {string.Join("\n", output)}");
        }

        Assert.True(File.Exists(outputPath), "Result file was not created.");
        var json = await File.ReadAllTextAsync(outputPath);
        
        try
        {
            var result = JsonSerializer.Deserialize<TestResult>(json);
            Assert.NotNull(result);
            Assert.True(result.Success, $"Test failed: {result.Message}");
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    private async Task LoginAsync()
    {
        // Get Login Page to grab AntiForgeryToken
        var response = await HttpClient.GetAsync("/Account/Login");
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var token = GetRequestVerificationToken(content);

        // Post Login
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Login", Username),
            new KeyValuePair<string, string>("Input.Password", Password),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        });

        var loginResponse = await HttpClient.PostAsync("/Account/Login", formData);
        loginResponse.EnsureSuccessStatusCode();
        // Check if redirected or successfully logged in (cookie should be set)
    }

    private async Task<string> SubmitUserCodeAsync(string userCode)
    {
        // Get Verify Page
        var response = await HttpClient.GetAsync("/connect/verify");
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var token = GetRequestVerificationToken(content);

        // Submit Code
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("user_code", userCode), // OpenIddict might expect lowercase user_code or Input.UserCode? 
            // DeviceVerificationViewModel likely binds to user_code. 
            // Let's check the view model or controller.
            // DeviceController Verify(string? user_code) -> matches "user_code"
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        });

        var submitResponse = await HttpClient.PostAsync("/connect/verify", formData);
        submitResponse.EnsureSuccessStatusCode();

        return await submitResponse.Content.ReadAsStringAsync();
    }

    private async Task ConfirmConsentAsync(string html)
    {
        if (!html.Contains("Authorize Application"))
        {
            // Not on consent page, maybe explicit consent is disabled or already granted?
            return;
        }

        // We are on the consent page. We need to submit "allow" and any hidden inputs (which include the query params).
        // Extract inputs
        var inputs = new Dictionary<string, string>();
        
        // Regex to find inputs
        var inputMatches = Regex.Matches(html, @"<input\s+[^>]*>");
        foreach (Match match in inputMatches)
        {
            var tag = match.Value;
            var nameMatch = Regex.Match(tag, "name=\"([^\"]+)\"");
            var valueMatch = Regex.Match(tag, "value=\"([^\"]*)\"");
            
            if (nameMatch.Success)
            {
                var name = nameMatch.Groups[1].Value;
                var value = valueMatch.Success ? valueMatch.Groups[1].Value : "";

                // Handle checkboxes: Only include if checked
                if (tag.Contains("type=\"checkbox\""))
                {
                    if (tag.Contains("checked"))
                    {
                        // Add or append (though usually unique name for checkboxes except arrays)
                        // For granted_scopes it's an array.
                        // FormUrlEncodedContent handles duplicate keys? No, Dictionary doesn't.
                        // We need List<KeyValuePair<string, string>>.
                    }
                    else
                    {
                        continue;
                    }
                }
                
                // We'll process into a list below
            }
        }

        var formData = new List<KeyValuePair<string, string>>();
        
        // Re-scan properly
        foreach (Match match in inputMatches)
        {
            var tag = match.Value;
            var nameMatch = Regex.Match(tag, "name=\"([^\"]+)\"");
            var valueMatch = Regex.Match(tag, "value=\"([^\"]*)\"");

            if (nameMatch.Success)
            {
                var name = nameMatch.Groups[1].Value;
                var val = valueMatch.Success ? valueMatch.Groups[1].Value : "";
                
                if (tag.Contains("type=\"checkbox\"") && !tag.Contains("checked"))
                    continue;

                 // Fix encoding if value is HTML encoded? Regex might grab &amp; etc.
                 // WebUtility.HtmlDecode(val);
                 formData.Add(new KeyValuePair<string, string>(name, WebUtility.HtmlDecode(val)));
            }
        }
        
        // Add submit button
        formData.Add(new KeyValuePair<string, string>("submit", "allow"));

        var response = await HttpClient.PostAsync("/connect/authorize", new FormUrlEncodedContent(formData));
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync();
        // Should be redirected to success or back to client (which is closing window)
        // Verify success?
    }

    private string GetRequestVerificationToken(string html)
    {
        var match = Regex.Match(html, @"input name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""");
        if (match.Success) return match.Groups[1].Value;
        throw new Exception("Could not find __RequestVerificationToken");
    }

    private string GetProjectDirectory()
    {
        var current = Directory.GetCurrentDirectory();
        var dir = new DirectoryInfo(current);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "HybridAuthIdP.sln")))
        {
            dir = dir.Parent;
        }
        if (dir == null) throw new Exception("Could not find solution root.");
        return Path.Combine(dir.FullName, "Tests.SystemTests");
    }
}
