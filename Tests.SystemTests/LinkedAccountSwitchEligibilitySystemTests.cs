using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Core.Application.DTOs;

namespace Tests.SystemTests;

[Collection("Shared Server")]
public sealed class LinkedAccountSwitchEligibilitySystemTests : IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _adminClient;
    private readonly HttpClient _browserClient;

    public LinkedAccountSwitchEligibilitySystemTests(WebIdPServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
        _adminClient = CreateHttpClient(useCookies: false);
        _browserClient = CreateHttpClient(useCookies: true);
    }

    public async Task InitializeAsync()
    {
        await _serverFixture.EnsureServerRunningAsync();
        var adminToken = await GetAdminTokenAsync();
        _adminClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);
    }

    public Task DisposeAsync()
    {
        _browserClient.Dispose();
        _adminClient.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SwitchAccount_WithInactiveLinkedTarget_ReturnsForbiddenAndPreservesCurrentSession()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var password = $"T!a1{Guid.NewGuid():N}";
        UserDetailDto? currentUser = null;
        UserDetailDto? targetUser = null;
        Guid? targetOriginalPersonId = null;

        try
        {
            currentUser = await CreateUserAsync($"switch-current-{suffix}", password);
            targetUser = await CreateUserAsync($"switch-target-{suffix}", password);
            targetOriginalPersonId = targetUser.PersonId;

            Assert.NotNull(currentUser.PersonId);
            Assert.NotNull(targetOriginalPersonId);

            await UnlinkAccountAsync(targetUser.Id);
            await LinkAccountAsync(currentUser.PersonId.Value, targetUser.Id);
            await SetUserActiveStateAsync(targetUser, isActive: false);

            await MfaEnrollmentTestClient.SignInAsync(
                _browserClient,
                currentUser.Email,
                password,
                "/Account/Login");
            await MfaEnrollmentTestClient.SetCsrfTokenAsync(_browserClient);

            using var switchResponse = await _browserClient.PostAsJsonAsync(
                "/api/my/switch-account",
                new
                {
                    targetAccountId = targetUser.Id,
                    reason = "System-test inactive target"
                });

            Assert.Equal(HttpStatusCode.Forbidden, switchResponse.StatusCode);

            using var accountsResponse =
                await _browserClient.GetAsync("/api/my/accounts");
            Assert.Equal(HttpStatusCode.OK, accountsResponse.StatusCode);
            var accounts =
                await accountsResponse.Content.ReadFromJsonAsync<List<LinkedAccountDto>>();
            Assert.NotNull(accounts);
            Assert.True(accounts.Single(account => account.Id == currentUser.Id)
                .IsCurrentAccount);
            Assert.False(accounts.Single(account => account.Id == targetUser.Id)
                .IsCurrentAccount);
        }
        finally
        {
            if (targetUser is not null && targetOriginalPersonId.HasValue)
            {
                await TryRestoreOriginalPersonAsync(
                    targetUser.Id,
                    targetOriginalPersonId.Value);
            }

            await TryDeleteUserAsync(targetUser?.Id);
            await TryDeleteUserAsync(currentUser?.Id);
        }
    }

    private HttpClient CreateHttpClient(bool useCookies)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            UseCookies = useCookies
        };
        if (useCookies)
        {
            handler.CookieContainer = new CookieContainer();
        }

        return new HttpClient(handler) { BaseAddress = new Uri(_serverFixture.BaseUrl) };
    }

    private async Task<UserDetailDto> CreateUserAsync(string userName, string password)
    {
        using var response = await _adminClient.PostAsJsonAsync(
            "/api/admin/users",
            new CreateUserDto
            {
                UserName = userName,
                Email = $"{userName}@example.invalid",
                FirstName = "Linked",
                LastName = "Account",
                Password = password,
                IsActive = true,
                EmailConfirmed = true
            });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDetailDto>())!;
    }

    private async Task SetUserActiveStateAsync(UserDetailDto user, bool isActive)
    {
        using var response = await _adminClient.PutAsJsonAsync(
            $"/api/admin/users/{user.Id}",
            new UpdateUserDto
            {
                Email = user.Email,
                UserName = user.UserName,
                FirstName = user.FirstName,
                LastName = user.LastName,
                PhoneNumber = user.PhoneNumber,
                Department = user.Department,
                JobTitle = user.JobTitle,
                EmployeeId = user.EmployeeId,
                IsActive = isActive,
                EmailConfirmed = user.EmailConfirmed,
                PhoneNumberConfirmed = user.PhoneNumberConfirmed,
                Roles = user.Roles
            });
        response.EnsureSuccessStatusCode();
    }

    private async Task LinkAccountAsync(Guid personId, Guid userId)
    {
        using var response = await _adminClient.PostAsJsonAsync(
            $"/api/admin/people/{personId}/accounts",
            new LinkAccountDto { UserId = userId });
        response.EnsureSuccessStatusCode();
    }

    private async Task UnlinkAccountAsync(Guid userId)
    {
        using var response =
            await _adminClient.DeleteAsync($"/api/admin/people/accounts/{userId}");
        response.EnsureSuccessStatusCode();
    }

    private async Task TryRestoreOriginalPersonAsync(Guid userId, Guid personId)
    {
        try
        {
            using var unlinkResponse =
                await _adminClient.DeleteAsync($"/api/admin/people/accounts/{userId}");
            if (unlinkResponse.StatusCode is not HttpStatusCode.NoContent and
                not HttpStatusCode.NotFound)
            {
                return;
            }

            using var linkResponse = await _adminClient.PostAsJsonAsync(
                $"/api/admin/people/{personId}/accounts",
                new LinkAccountDto { UserId = userId });
        }
        catch (HttpRequestException)
        {
            // Best-effort cleanup only.
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
            using var response =
                await _adminClient.DeleteAsync($"/api/admin/users/{userId.Value}");
        }
        catch (HttpRequestException)
        {
            // Best-effort cleanup only.
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
                    "users.read",
                    "users.create",
                    "users.update",
                    "users.delete",
                    "persons.read",
                    "persons.update")
            });
        using var response = await _adminClient.PostAsync("/connect/token", request);
        response.EnsureSuccessStatusCode();
        using var document =
            JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("access_token").GetString()!;
    }
}
