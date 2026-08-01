using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// Device Flow E2E test - marked as Slow due to 10s timeout and external process.
/// Run with: dotnet test --filter "Category!=Slow" to skip.
/// </summary>
[Trait("Category", "Slow")]
[Collection("Shared Server")]
public class DeviceFlowSystemTests : IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
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

    public DeviceFlowSystemTests(WebIdPServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
    }

    public async Task InitializeAsync()
    {
        await _serverFixture.EnsureServerRunningAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task DeviceFlow_EndToEnd_ReturnsSuccess()
    {
        // Arrange
        var projectDir = GetProjectDirectory();
        var testClientDir = Path.Combine(projectDir, "..", "samples", "TestClient.Device");
        var outputPath = Path.Combine(Path.GetTempPath(), $"device_results_{Guid.NewGuid()}.json");
        
        // Ensure we are logged in first
        await LoginAsync(HttpClient, Username, Password);

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
        var content = await SubmitUserCodeAsync(HttpClient, userCode);
        await ConfirmConsentAsync(HttpClient, content);

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

    [Fact]
    public async Task DeviceVerification_RequiresAntiforgeryAndIntentAndPreservesValidSubmission()
    {
        using var pollingClient = CreateHttpClient(useCookies: false, allowAutoRedirect: false);

        using var deviceRequest = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["client_id"] = "testclient-device",
                ["scope"] = "openid profile offline_access"
            });
        using var deviceResponse = await pollingClient.PostAsync("/connect/device", deviceRequest);
        Assert.Equal(HttpStatusCode.OK, deviceResponse.StatusCode);
        using var devicePayload = await JsonDocument.ParseAsync(
            await deviceResponse.Content.ReadAsStreamAsync());
        var deviceCode = devicePayload.RootElement.GetProperty("device_code").GetString();
        var userCode = devicePayload.RootElement.GetProperty("user_code").GetString();
        var pollingIntervalSeconds = devicePayload.RootElement.TryGetProperty(
            "interval",
            out var intervalElement)
            ? intervalElement.GetInt32()
            : 5;
        Assert.False(string.IsNullOrWhiteSpace(deviceCode));
        Assert.False(string.IsNullOrWhiteSpace(userCode));

        async Task<List<KeyValuePair<string, string>>> GetVerificationFieldsAsync(
            HttpClient client)
        {
            using var verificationPage = await client.GetAsync(
                $"/connect/verify?user_code={Uri.EscapeDataString(userCode!)}");
            Assert.Equal(HttpStatusCode.OK, verificationPage.StatusCode);
            var verificationHtml = await verificationPage.Content.ReadAsStringAsync();
            var fields = ExtractHiddenInputs(verificationHtml);
            Assert.Contains(fields, pair => pair.Key == "__RequestVerificationToken");
            Assert.Contains(fields, pair => pair.Key == "device_verification_intent");
            Assert.Equal(
                GetRequestVerificationToken(verificationHtml),
                fields.Single(pair => pair.Key == "__RequestVerificationToken").Value);
            fields.Add(new KeyValuePair<string, string>("user_code", userCode!));
            return fields;
        }

        using var missingAntiforgeryBrowser = CreateHttpClient(
            useCookies: true,
            allowAutoRedirect: true);
        await LoginAsync(missingAntiforgeryBrowser, "pkce@hybridauth.local", "Pkce@123");
        var withoutAntiforgery = (await GetVerificationFieldsAsync(missingAntiforgeryBrowser))
            .Where(pair => pair.Key != "__RequestVerificationToken")
            .ToList();
        using var missingAntiforgeryResponse = await missingAntiforgeryBrowser.PostAsync(
            "/connect/verify",
            new FormUrlEncodedContent(withoutAntiforgery));
        Assert.Equal(HttpStatusCode.BadRequest, missingAntiforgeryResponse.StatusCode);
        Assert.Null(missingAntiforgeryResponse.Headers.Location);

        using var invalidAntiforgeryBrowser = CreateHttpClient(
            useCookies: true,
            allowAutoRedirect: true);
        await LoginAsync(invalidAntiforgeryBrowser, "pkce@hybridauth.local", "Pkce@123");
        var invalidAntiforgery = (await GetVerificationFieldsAsync(invalidAntiforgeryBrowser))
            .Select(pair => pair.Key == "__RequestVerificationToken"
                ? new KeyValuePair<string, string>(pair.Key, "invalid-antiforgery-token")
                : pair)
            .ToList();
        using var invalidAntiforgeryResponse = await invalidAntiforgeryBrowser.PostAsync(
            "/connect/verify",
            new FormUrlEncodedContent(invalidAntiforgery));
        Assert.Equal(HttpStatusCode.BadRequest, invalidAntiforgeryResponse.StatusCode);
        Assert.Null(invalidAntiforgeryResponse.Headers.Location);

        using var missingIntentBrowser = CreateHttpClient(
            useCookies: true,
            allowAutoRedirect: true);
        await LoginAsync(missingIntentBrowser, "pkce@hybridauth.local", "Pkce@123");
        var withoutIntent = (await GetVerificationFieldsAsync(missingIntentBrowser))
            .Where(pair => pair.Key != "device_verification_intent")
            .ToList();
        using var missingIntentResponse = await missingIntentBrowser.PostAsync(
            "/connect/verify",
            new FormUrlEncodedContent(withoutIntent));
        await AssertInvalidDeviceVerificationIntentAsync(missingIntentResponse);

        using var recoveryBrowser = CreateHttpClient(
            useCookies: true,
            allowAutoRedirect: true);
        await LoginAsync(recoveryBrowser, "pkce@hybridauth.local", "Pkce@123");
        var unknownIntent = (await GetVerificationFieldsAsync(recoveryBrowser))
            .Select(pair => pair.Key == "device_verification_intent"
                ? new KeyValuePair<string, string>(pair.Key, "unknown-device-verification-intent")
                : pair)
            .ToList();
        using var unknownIntentResponse = await recoveryBrowser.PostAsync(
            "/connect/verify",
            new FormUrlEncodedContent(unknownIntent));
        await AssertInvalidDeviceVerificationIntentAsync(unknownIntentResponse);

        var validFields = ExtractHiddenInputs(
            await unknownIntentResponse.Content.ReadAsStringAsync());
        validFields.Add(new KeyValuePair<string, string>("user_code", userCode!));
        using var validResponse = await recoveryBrowser.PostAsync(
            "/connect/verify",
            new FormUrlEncodedContent(validFields));
        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);
        Assert.Equal(
            "/connect/verify/success",
            validResponse.RequestMessage?.RequestUri?.AbsolutePath);

        await Task.Delay(TimeSpan.FromSeconds(pollingIntervalSeconds));
        using var tokenRequest = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = "testclient-device",
                ["device_code"] = deviceCode!
            });
        using var tokenResponse = await pollingClient.PostAsync("/connect/token", tokenRequest);
        using var tokenPayload = await JsonDocument.ParseAsync(
            await tokenResponse.Content.ReadAsStreamAsync());
        if (tokenResponse.StatusCode != HttpStatusCode.OK)
        {
            var error = tokenPayload.RootElement.TryGetProperty("error", out var errorElement)
                ? errorElement.GetString()
                : "missing_error";
            Assert.Fail($"Device token exchange failed with OAuth error '{error}'.");
        }

        Assert.False(string.IsNullOrWhiteSpace(
            tokenPayload.RootElement.GetProperty("access_token").GetString()));

        using var replayResponse = await recoveryBrowser.PostAsync(
            "/connect/verify",
            new FormUrlEncodedContent(validFields));
        await AssertInvalidDeviceVerificationIntentAsync(replayResponse);
    }

    [Fact]
    public async Task DeviceCodeExchange_WhenUserDeactivatedAfterApproval_ReturnsInvalidGrant()
    {
        using var browserClient = CreateHttpClient(useCookies: true, allowAutoRedirect: true);
        using var adminClient = CreateHttpClient(useCookies: false, allowAutoRedirect: false);
        using var pollingClient = CreateHttpClient(useCookies: false, allowAutoRedirect: false);

        await LoginAsync(browserClient, "pkce@hybridauth.local", "Pkce@123");

        using var deviceRequest = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["client_id"] = "testclient-device",
                ["scope"] = "openid profile offline_access"
            });
        using var deviceResponse = await pollingClient.PostAsync("/connect/device", deviceRequest);
        Assert.Equal(HttpStatusCode.OK, deviceResponse.StatusCode);
        using var devicePayload = await JsonDocument.ParseAsync(
            await deviceResponse.Content.ReadAsStreamAsync());
        var deviceCode = devicePayload.RootElement.GetProperty("device_code").GetString();
        var userCode = devicePayload.RootElement.GetProperty("user_code").GetString();
        Assert.False(string.IsNullOrWhiteSpace(deviceCode));
        Assert.False(string.IsNullOrWhiteSpace(userCode));

        var verificationHtml = await SubmitUserCodeAsync(browserClient, userCode!);
        await ConfirmConsentAsync(browserClient, verificationHtml);

        var adminToken = await GetAdminTokenAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);
        var userId = await FindUserIdAsync(adminClient, "pkce@hybridauth.local");
        var userWasDeactivated = false;

        try
        {
            using var deactivateResponse =
                await adminClient.PostAsync($"/api/admin/users/{userId}/deactivate", null);
            userWasDeactivated = true;
            Assert.Equal(HttpStatusCode.NoContent, deactivateResponse.StatusCode);

            using var tokenRequest = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["client_id"] = "testclient-device",
                    ["device_code"] = deviceCode!
                });
            using var tokenResponse =
                await pollingClient.PostAsync("/connect/token", tokenRequest);

            Assert.Equal(HttpStatusCode.BadRequest, tokenResponse.StatusCode);
            Assert.Equal("application/json", tokenResponse.Content.Headers.ContentType?.MediaType);
            using var tokenPayload = await JsonDocument.ParseAsync(
                await tokenResponse.Content.ReadAsStreamAsync());
            Assert.Equal(
                "invalid_grant",
                tokenPayload.RootElement.GetProperty("error").GetString());
            Assert.False(tokenPayload.RootElement.TryGetProperty("access_token", out _));
            Assert.False(tokenPayload.RootElement.TryGetProperty("id_token", out _));
            Assert.False(tokenPayload.RootElement.TryGetProperty("refresh_token", out _));
        }
        finally
        {
            if (userWasDeactivated)
            {
                using var reactivateResponse =
                    await adminClient.PostAsync($"/api/admin/users/{userId}/reactivate", null);
                Assert.Equal(HttpStatusCode.OK, reactivateResponse.StatusCode);
            }
        }
    }

    private static async Task LoginAsync(
        HttpClient client,
        string username,
        string password)
    {
        // Get Login Page to grab AntiForgeryToken
        var response = await client.GetAsync("/Account/Login");
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var token = GetRequestVerificationToken(content);

        // Post Login
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Login", username),
            new KeyValuePair<string, string>("Input.Password", password),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        });

        var loginResponse = await client.PostAsync("/Account/Login", formData);
        loginResponse.EnsureSuccessStatusCode();
        // Check if redirected or successfully logged in (cookie should be set)
    }

    private static async Task<string> SubmitUserCodeAsync(
        HttpClient client,
        string userCode)
    {
        var response = await client.GetAsync("/connect/verify");
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var formFields = ExtractHiddenInputs(content);
        formFields.Add(new KeyValuePair<string, string>("user_code", userCode));

        var submitResponse = await client.PostAsync(
            "/connect/verify",
            new FormUrlEncodedContent(formFields));
        submitResponse.EnsureSuccessStatusCode();

        return await submitResponse.Content.ReadAsStringAsync();
    }

    private static async Task ConfirmConsentAsync(HttpClient client, string html)
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

        var response = await client.PostAsync(
            "/connect/authorize",
            new FormUrlEncodedContent(formData));
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync();
        // Should be redirected to success or back to client (which is closing window)
        // Verify success?
    }

    private static HttpClient CreateHttpClient(bool useCookies, bool allowAutoRedirect)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AllowAutoRedirect = allowAutoRedirect,
            UseCookies = useCookies
        };
        if (useCookies)
        {
            handler.CookieContainer = new CookieContainer();
        }

        return new HttpClient(handler) { BaseAddress = new Uri(Authority) };
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient adminClient)
    {
        using var request = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "testclient-admin",
                ["client_secret"] = "admin-test-secret-2024",
                ["scope"] = "users.read users.update users.delete"
            });
        using var response = await adminClient.PostAsync("/connect/token", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var payload = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        var accessToken = payload.RootElement.GetProperty("access_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        return accessToken!;
    }

    private static async Task<string> FindUserIdAsync(HttpClient adminClient, string email)
    {
        using var response = await adminClient.GetAsync(
            $"/api/admin/users?search={Uri.EscapeDataString(email)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var payload = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        var items = payload.RootElement.GetProperty("items");
        Assert.Single(items.EnumerateArray());
        var userId = items[0].GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(userId));
        return userId!;
    }

    private static string GetRequestVerificationToken(string html)
    {
        var match = Regex.Match(html, @"input name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""");
        if (match.Success) return match.Groups[1].Value;
        throw new Exception("Could not find __RequestVerificationToken");
    }

    private static List<KeyValuePair<string, string>> ExtractHiddenInputs(string html)
    {
        var inputs = new List<KeyValuePair<string, string>>();
        var inputMatches = Regex.Matches(
            html,
            @"<input\b[^>]*\btype=""hidden""[^>]*>",
            RegexOptions.IgnoreCase);

        foreach (Match inputMatch in inputMatches)
        {
            var nameMatch = Regex.Match(
                inputMatch.Value,
                @"\bname=""([^""]+)""",
                RegexOptions.IgnoreCase);
            if (!nameMatch.Success)
            {
                continue;
            }

            var valueMatch = Regex.Match(
                inputMatch.Value,
                @"\bvalue=""([^""]*)""",
                RegexOptions.IgnoreCase);
            inputs.Add(new KeyValuePair<string, string>(
                WebUtility.HtmlDecode(nameMatch.Groups[1].Value),
                valueMatch.Success
                    ? WebUtility.HtmlDecode(valueMatch.Groups[1].Value)
                    : string.Empty));
        }

        return inputs
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    private static async Task AssertInvalidDeviceVerificationIntentAsync(
        HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var html = await response.Content.ReadAsStringAsync();
        Assert.True(
            Regex.IsMatch(
                html,
                "data-test-id=\\\"device-verification-intent-error\\\"",
                RegexOptions.IgnoreCase),
            "The device verification page should render an actionable intent error.");
        Assert.True(
            ExtractHiddenInputs(html).Any(pair =>
                pair.Key == "device_verification_intent" &&
                !string.IsNullOrWhiteSpace(pair.Value)),
            "The device verification page should issue a fresh retry intent.");
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
