using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Core.Application.DTOs;

namespace Tests.SystemTests;

[Collection("Shared Server")]
public sealed class LifecycleCookieSystemTests : IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _adminClient;

    public LifecycleCookieSystemTests(WebIdPServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
        _adminClient = CreateClient(useCookies: false);
    }

    public async Task InitializeAsync()
    {
        await _serverFixture.EnsureServerRunningAsync();
        _adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await GetAdminTokenAsync());
    }

    public Task DisposeAsync()
    {
        _adminClient.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ExistingCookie_ShouldBeRejectedAfterUserDeactivation_WhileEligibleControlRetainsAccess()
    {
        UserDetailDto? deactivatedUser = null;
        UserDetailDto? eligibleUser = null;
        using var deactivatedBrowser = CreateClient(useCookies: true);
        using var eligibleBrowser = CreateClient(useCookies: true);

        try
        {
            var password = $"T!a1{Guid.NewGuid():N}";
            deactivatedUser = await CreateUserAsync("lifecycle-deactivate", password);
            eligibleUser = await CreateUserAsync("lifecycle-eligible", password);

            await SignInAsync(deactivatedBrowser, deactivatedUser.Email, password);
            await SignInAsync(eligibleBrowser, eligibleUser.Email, password);

            using (var eligibleAccess = await eligibleBrowser.GetAsync("/Dashboard"))
            {
                Assert.Equal(HttpStatusCode.OK, eligibleAccess.StatusCode);
            }

            using (var deactivate = await _adminClient.PostAsync(
                $"/api/admin/users/{deactivatedUser.Id}/deactivate",
                content: null))
            {
                Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
            }

            using var replay = await deactivatedBrowser.GetAsync("/Dashboard");
            Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);
            Assert.Contains("/Account/Login", replay.Headers.Location?.ToString());
        }
        finally
        {
            await TryDeleteUserAsync(deactivatedUser?.Id);
            await TryDeleteUserAsync(eligibleUser?.Id);
        }
    }

    [Fact]
    public async Task ExistingCookie_ShouldBeRejectedAfterLinkedPersonStatusBecomesIneligible()
    {
        UserDetailDto? user = null;
        using var browser = CreateClient(useCookies: true);

        try
        {
            var password = $"T!a1{Guid.NewGuid():N}";
            user = await CreateUserAsync("lifecycle-person", password);
            Assert.NotNull(user.PersonId);
            await SignInAsync(browser, user.Email, password);

            using (var beforeTransition = await browser.GetAsync("/Dashboard"))
            {
                Assert.Equal(HttpStatusCode.OK, beforeTransition.StatusCode);
            }

            using (var transition = await _adminClient.PutAsJsonAsync(
                $"/api/admin/people/{user.PersonId.Value}",
                new PersonDto
                {
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Status = "Suspended"
                }))
            {
                transition.EnsureSuccessStatusCode();
            }

            using var replay = await browser.GetAsync("/Dashboard");
            Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);
            Assert.Contains("/Account/Login", replay.Headers.Location?.ToString());
        }
        finally
        {
            await TryDeleteUserAsync(user?.Id);
        }
    }

    [Fact]
    public async Task ExistingCookie_ShouldBeRejectedAfterPublicLockout_WhenSecurityStampIsUnchanged()
    {
        UserDetailDto? user = null;
        var authenticatedCookies = new CookieContainer();
        var lockoutCookies = new CookieContainer();
        using var authenticatedBrowser = CreateClient(useCookies: true, authenticatedCookies);
        using var lockoutBrowser = CreateClient(useCookies: true, lockoutCookies);

        try
        {
            var password = $"T!a1{Guid.NewGuid():N}";
            user = await CreateUserAsync("lifecycle-lockout", password);

            await SignInAsync(authenticatedBrowser, user.Email, password);
            using (var protectedAccess = await authenticatedBrowser.GetAsync("/Dashboard"))
            {
                Assert.Equal(HttpStatusCode.OK, protectedAccess.StatusCode);
            }

            var securityStampRemainedUnchanged =
                await _serverFixture.VerifyUserSecurityStampRemainsUnchangedAsync(
                    user.Id,
                    () => TriggerPublicLockoutAsync(lockoutBrowser, user.Email));
            Assert.True(securityStampRemainedUnchanged);

            using var replay = await authenticatedBrowser.GetAsync("/Dashboard");
            Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);
            Assert.Contains("/Account/Login", replay.Headers.Location?.ToString());
        }
        finally
        {
            await TryDeleteUserAsync(user?.Id);
        }
    }

    private HttpClient CreateClient(bool useCookies, CookieContainer? cookieContainer = null)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AllowAutoRedirect = false,
            UseCookies = useCookies
        };
        if (useCookies)
        {
            handler.CookieContainer = cookieContainer ?? new CookieContainer();
        }

        return new HttpClient(handler) { BaseAddress = new Uri(_serverFixture.BaseUrl) };
    }

    private async Task<UserDetailDto> CreateUserAsync(string prefix, string password)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var userName = $"{prefix}-{suffix}";
        using var response = await _adminClient.PostAsJsonAsync(
            "/api/admin/users",
            new CreateUserDto
            {
                UserName = userName,
                Email = $"{userName}@example.invalid",
                FirstName = "Lifecycle",
                LastName = "Cookie",
                Password = password,
                IsActive = true,
                EmailConfirmed = true
            });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDetailDto>())!;
    }

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        using var loginPage = await client.GetAsync("/Account/Login");
        loginPage.EnsureSuccessStatusCode();
        var page = await loginPage.Content.ReadAsStringAsync();
        var antiForgeryToken = ExtractAntiForgeryToken(page);
        using var login = await client.PostAsync(
            "/Account/Login",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["Input.Login"] = email,
                    ["Input.Password"] = password,
                    ["__RequestVerificationToken"] = antiForgeryToken
                }));

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
    }

    private static async Task TriggerPublicLockoutAsync(HttpClient client, string email)
    {
        using var loginPage = await client.GetAsync("/Account/Login");
        loginPage.EnsureSuccessStatusCode();
        var antiForgeryToken = ExtractAntiForgeryToken(await loginPage.Content.ReadAsStringAsync());

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var login = await client.PostAsync(
                "/Account/Login",
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["Input.Login"] = email,
                        ["Input.Password"] = "invalid-password",
                        ["__RequestVerificationToken"] = antiForgeryToken
                    }));

            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            antiForgeryToken = ExtractAntiForgeryToken(await login.Content.ReadAsStringAsync());
        }
    }

    private async Task TryDeleteUserAsync(Guid? userId)
    {
        if (!userId.HasValue)
        {
            return;
        }

        try
        {
            using var response = await _adminClient.DeleteAsync($"/api/admin/users/{userId.Value}");
        }
        catch (HttpRequestException)
        {
            // Best-effort cleanup for disposable system-test users.
        }
    }

    private async Task<string> GetAdminTokenAsync()
    {
        using var request = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "testclient-admin",
                ["client_secret"] = "admin-test-secret-2024",
                ["scope"] = string.Join(
                    " ",
                    "users.create",
                    "users.delete",
                    "persons.update")
            });
        using var response = await _adminClient.PostAsync("/connect/token", request);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("access_token").GetString()!;
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            @"name=""__RequestVerificationToken""\s+type=""hidden""\s+value=""([^""]+)""");
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        throw new InvalidOperationException("The login page did not provide an antiforgery token.");
    }
}
