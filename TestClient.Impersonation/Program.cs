using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text;

namespace TestClient.Impersonation;

class Program
{
    private const string BaseUrl = "https://localhost:7035";
    private static readonly CookieContainer CookieContainer = new();
    private static readonly HttpClientHandler Handler = new()
    {
        CookieContainer = CookieContainer,
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        AllowAutoRedirect = false // We handle redirects to capture cookies properly if needed, though HttpClient does it.
        // Actually, for login, we usually want to follow redirects to get the cookie set.
        // Let's set it to true for simplicity, but watch out for circular redirects if login fails.
    };
    private static readonly HttpClient Client = new(Handler) { BaseAddress = new Uri(BaseUrl) };

    static async Task Main(string[] args)
    {
        Console.WriteLine("Starting Impersonation System Test...");
        Handler.AllowAutoRedirect = true; 

        try
        {
            // 1. Login as Admin
            await LoginAsAdmin();
            Console.WriteLine("[PASS] Admin Logged In");

            // 2. Create Target User
            var targetUserId = await CreateTargetUser();
            Console.WriteLine($"[PASS] Target User Created: {targetUserId}");

            // 3. Verify Admin Access (Baseline)
            await VerifyAdminAccess(true);
            Console.WriteLine("[PASS] Baseline Admin Access Verified");

            // 4. Start Impersonation
            await StartImpersonation(targetUserId);
            Console.WriteLine("[PASS] Impersonation Started");

            // 5. Verify Lost Admin Access (Proof of Impersonation)
            await VerifyAdminAccess(false);
            Console.WriteLine("[PASS] Impersonation Verified (Admin Access Lost)");

            // 6. Revert Impersonation
            await RevertImpersonation();
            Console.WriteLine("[PASS] Revert Requested");

            // 7. Verify Admin Access Restored
            await VerifyAdminAccess(true);
            Console.WriteLine("[PASS] Admin Access Restored");

            Console.WriteLine("------------------------------------------");
            Console.WriteLine("SYSTEM TEST PASSED: Impersonation Flow OK");
            Console.WriteLine("------------------------------------------");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FAIL] Test Failed: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }

    private static async Task LoginAsAdmin()
    {
        // GET Login Page to fetch AntiForgeryToken
        var loginPageResponse = await Client.GetAsync("/Account/Login");
        loginPageResponse.EnsureSuccessStatusCode();
        var loginHtml = await loginPageResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(loginHtml);

        // POST Credentials
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Login", "admin@hybridauth.local"),
            new KeyValuePair<string, string>("Input.Password", "Admin@123"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        });

        var response = await Client.PostAsync("/Account/Login", content);
        
        // Should redirect to return url or root
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Login Request Failed: {response.StatusCode}");

        // Check if we are really logged in. The response content should NOT contain "Invalid login attempt".
        var responseHtml = await response.Content.ReadAsStringAsync();
        if (responseHtml.Contains("Invalid login attempt"))
            throw new Exception("Login failed: Invalid credentials or error.");
    }

    private static async Task<string> CreateTargetUser()
    {
        var timestamp = DateTime.Now.Ticks;
        var email = $"impersonate-test-{timestamp}@systemtest.local";
        var payload = new
        {
            userName = email,
            email = email,
            firstName = "Test",
            lastName = "Target",
            password = "Password123!",
            roles = new string[] { } // No roles = limited user
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/admin/users", content);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new Exception($"Create User Failed: {response.StatusCode} - {err}");
        }

        var respJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(respJson);
        if (doc.RootElement.TryGetProperty("id", out var idProp))
        {
            return idProp.GetString()!;
        }
        throw new Exception("Create User response did not contain ID");
    }

    private static async Task VerifyAdminAccess(bool expectSuccess)
    {
        var response = await Client.GetAsync("/api/admin/users?take=1");
        if (expectSuccess)
        {
            if (!response.IsSuccessStatusCode)
                throw new Exception($"Expected Admin Access (200) but got {response.StatusCode}");
        }
        else
        {
            // Expect 403 Forbidden (policy denied) or 401 Unauthorized (if cookie invalid)
            // Impersonating a new user without roles -> Accessing Admin API -> Should be 403.
            if (response.StatusCode != HttpStatusCode.Forbidden && response.StatusCode != HttpStatusCode.Unauthorized && response.StatusCode != HttpStatusCode.NotFound)
            {
               // If endpoint returns 404 for unauthorized users to hide existence, that's also valid 'no access'
               // But usually it's 403.
               // If it returns 200, logic failed.
               if (response.IsSuccessStatusCode)
                   throw new Exception("Expected Access Denied but got 200 OK (Impersonation failed?)");
            }
        }
    }

    private static async Task StartImpersonation(string userId)
    {
        var response = await Client.PostAsync($"/api/admin/users/{userId}/impersonate", null);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new Exception($"Start Impersonation Failed: {response.StatusCode} - {err}");
        }
    }

    private static async Task RevertImpersonation()
    {
        // Revert endpoint
        // NOTE: The UI uses a "Switch Back" button that calls a JS function.
        // We need to call the API endpoint: POST /api/account/impersonation/revert
        
        var response = await Client.PostAsync("/api/account/impersonation/revert", null);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new Exception($"Revert Impersonation Failed: {response.StatusCode} - {err}");
        }
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(html, @"<input name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""");
        if (match.Success)
        {
            return match.Groups[1].Value;
        }
        throw new Exception("AntiForgeryToken not found in HTML");
    }
}
