using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Core.Application.DTOs;
using Xunit;

namespace Tests.SystemTests;

[Collection("Shared Server")]
public sealed class ClientMfaPolicySystemTests : IAsyncLifetime
{
    private const string PasswordOnlyUserEmail = "amr-nomfa@hybridauth.local";
    private const string PasswordOnlyUserPassword = "Test@123";

    private static readonly IReadOnlyList<string> InteractiveClientPermissions =
    [
        "ept:authorization",
        "ept:token",
        "gt:authorization_code",
        "rst:code",
        "scp:openid"
    ];

    private static readonly IReadOnlyList<string> PasswordRefreshClientPermissions =
    [
        "ept:token",
        "gt:password",
        "gt:refresh_token",
        "scp:openid",
        "scp:offline_access"
    ];

    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _adminClient;
    private readonly HttpClient _browserClient;

    public ClientMfaPolicySystemTests(WebIdPServerFixture serverFixture)
    {
        _serverFixture = serverFixture;

        var adminHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AllowAutoRedirect = false
        };
        _adminClient = new HttpClient(adminHandler)
        {
            BaseAddress = new Uri(_serverFixture.BaseUrl)
        };

        var browserHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };
        _browserClient = new HttpClient(browserHandler)
        {
            BaseAddress = new Uri(_serverFixture.BaseUrl)
        };
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
        _adminClient.Dispose();
        _browserClient.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Authorize_WithPersistedClientRequireMfa_RequiresMfaWhileUnsetPolicyDoesNot()
    {
        await AssertGlobalMfaIsDisabledAsync();

        var runId = Guid.NewGuid().ToString("N");
        var baselineClientId = $"client-mfa-baseline-{runId}";
        var requiredClientId = $"client-mfa-required-{runId}";
        var baselineRedirectUri = $"https://localhost:7001/{baselineClientId}/callback";
        var requiredRedirectUri = $"https://localhost:7001/{requiredClientId}/callback";
        string? baselineApplicationId = null;
        string? requiredApplicationId = null;

        try
        {
            baselineApplicationId = await CreateInteractiveClientAsync(
                baselineClientId,
                baselineRedirectUri,
                requireMfa: false);
            requiredApplicationId = await CreateInteractiveClientAsync(
                requiredClientId,
                requiredRedirectUri,
                requireMfa: true);

            await AssertClientRequireMfaAsync(baselineApplicationId, expected: false);
            await AssertClientRequireMfaAsync(requiredApplicationId, expected: true);

            await SignInPasswordOnlyUserAsync();

            using var baselineResponse = await _browserClient.GetAsync(
                BuildAuthorizeUrl(baselineClientId, baselineRedirectUri));
            Assert.Equal(HttpStatusCode.OK, baselineResponse.StatusCode);
            Assert.Null(baselineResponse.Headers.Location);

            using var requiredResponse = await _browserClient.GetAsync(
                BuildAuthorizeUrl(requiredClientId, requiredRedirectUri));
            Assert.Equal(HttpStatusCode.Redirect, requiredResponse.StatusCode);
            var requiredLocation = requiredResponse.Headers.Location?.ToString();
            Assert.False(string.IsNullOrWhiteSpace(requiredLocation));
            Assert.True(
                requiredLocation!.Contains(
                    "/Account/MfaSetup",
                    StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await DeleteClientAsync(requiredApplicationId);
            await DeleteClientAsync(baselineApplicationId);
        }
    }

    [Fact]
    public async Task RefreshToken_FromPasswordOnlySession_IsRejectedAfterClientRequireMfaIsEnabled()
    {
        await AssertGlobalMfaIsDisabledAsync();

        var clientId = $"client-mfa-refresh-{Guid.NewGuid():N}";
        string? applicationId = null;

        try
        {
            var client = await CreatePasswordRefreshClientAsync(clientId);
            applicationId = client.Id;
            Assert.False(string.IsNullOrWhiteSpace(client.Secret));
            await AssertClientRequireMfaAsync(applicationId, expected: false);

            string refreshToken;
            using (var passwordTokenRequest = new FormUrlEncodedContent(
                       new Dictionary<string, string>
                       {
                           ["grant_type"] = "password",
                           ["client_id"] = clientId,
                           ["client_secret"] = client.Secret!,
                           ["username"] = PasswordOnlyUserEmail,
                           ["password"] = PasswordOnlyUserPassword,
                           ["scope"] = "openid offline_access"
                       }))
            using (var passwordTokenResponse = await _browserClient.PostAsync(
                       "/connect/token",
                       passwordTokenRequest))
            {
                Assert.Equal(HttpStatusCode.OK, passwordTokenResponse.StatusCode);
                refreshToken = await ReadRequiredStringAsync(
                    passwordTokenResponse,
                    "refresh_token");
            }

            await UpdateClientRequireMfaAsync(applicationId, requireMfa: true);
            await AssertClientRequireMfaAsync(applicationId, expected: true);

            using var refreshRequest = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = clientId,
                    ["client_secret"] = client.Secret!,
                    ["refresh_token"] = refreshToken
                });
            using var refreshResponse = await _browserClient.PostAsync(
                "/connect/token",
                refreshRequest);

            Assert.Equal(HttpStatusCode.BadRequest, refreshResponse.StatusCode);
            Assert.Equal(
                "application/json",
                refreshResponse.Content.Headers.ContentType?.MediaType);

            using var refreshPayload = await JsonDocument.ParseAsync(
                await refreshResponse.Content.ReadAsStreamAsync());
            Assert.True(refreshPayload.RootElement.TryGetProperty("error", out var error));
            Assert.Equal("invalid_grant", error.GetString());
            Assert.False(refreshPayload.RootElement.TryGetProperty("access_token", out _));
            Assert.False(refreshPayload.RootElement.TryGetProperty("refresh_token", out _));
        }
        finally
        {
            await DeleteClientAsync(applicationId);
        }
    }

    private async Task AssertGlobalMfaIsDisabledAsync()
    {
        using var response = await _adminClient.GetAsync("/api/admin/security/policies");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var policy = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        Assert.True(
            policy.RootElement.TryGetProperty(
                "enforceMandatoryMfaEnrollment",
                out var enforcement));
        Assert.False(enforcement.GetBoolean());
    }

    private async Task<string> CreateInteractiveClientAsync(
        string clientId,
        string redirectUri,
        bool requireMfa)
    {
        var client = await CreateClientAsync(
            new CreateClientRequest(
                ClientId: clientId,
                ClientSecret: null,
                DisplayName: "Client MFA system authorization test",
                ApplicationType: "web",
                Type: "public",
                ConsentType: "implicit",
                RedirectUris: [redirectUri],
                PostLogoutRedirectUris: [],
                Permissions: [.. InteractiveClientPermissions],
                SupportedRoles: null)
            {
                RequireMfa = requireMfa
            });

        Assert.True(string.IsNullOrWhiteSpace(client.Secret));
        return client.Id;
    }

    private async Task<CreatedClient> CreatePasswordRefreshClientAsync(string clientId)
    {
        return await CreateClientAsync(
            new CreateClientRequest(
                ClientId: clientId,
                ClientSecret: null,
                DisplayName: "Client MFA system refresh test",
                ApplicationType: "web",
                Type: "confidential",
                ConsentType: "implicit",
                RedirectUris: [],
                PostLogoutRedirectUris: [],
                Permissions: [.. PasswordRefreshClientPermissions],
                SupportedRoles: null)
            {
                RequireMfa = false
            });
    }

    private async Task<CreatedClient> CreateClientAsync(CreateClientRequest request)
    {
        using var response = await _adminClient.PostAsJsonAsync("/api/admin/clients", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var payload = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        Assert.True(payload.RootElement.TryGetProperty("id", out var id));
        var applicationId = id.GetString();
        Assert.False(string.IsNullOrWhiteSpace(applicationId));

        var secret = payload.RootElement.TryGetProperty("clientSecret", out var clientSecret)
            ? clientSecret.GetString()
            : null;
        return new CreatedClient(applicationId!, secret);
    }

    private async Task UpdateClientRequireMfaAsync(string applicationId, bool requireMfa)
    {
        var request = new UpdateClientRequest(
            ClientId: null,
            ClientSecret: null,
            DisplayName: null,
            Type: null,
            ConsentType: null,
            RedirectUris: null,
            PostLogoutRedirectUris: null,
            Permissions: null,
            SupportedRoles: null)
        {
            RequireMfa = requireMfa
        };

        using var response = await _adminClient.PutAsJsonAsync(
            $"/api/admin/clients/{applicationId}",
            request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task AssertClientRequireMfaAsync(string applicationId, bool expected)
    {
        using var response = await _adminClient.GetAsync(
            $"/api/admin/clients/{applicationId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var payload = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        Assert.True(payload.RootElement.TryGetProperty("requireMfa", out var requireMfa));
        Assert.Equal(expected, requireMfa.GetBoolean());
    }

    private async Task DeleteClientAsync(string? applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            return;
        }

        using var response = await _adminClient.DeleteAsync(
            $"/api/admin/clients/{applicationId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task SignInPasswordOnlyUserAsync()
    {
        using var loginPageResponse = await _browserClient.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, loginPageResponse.StatusCode);
        var loginPage = await loginPageResponse.Content.ReadAsStringAsync();

        using var loginRequest = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Input.Login", PasswordOnlyUserEmail),
            new KeyValuePair<string, string>("Input.Password", PasswordOnlyUserPassword),
            new KeyValuePair<string, string>(
                "__RequestVerificationToken",
                ExtractAntiForgeryToken(loginPage))
        ]);
        using var loginResponse = await _browserClient.PostAsync("/Account/Login", loginRequest);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
    }

    private async Task<string> GetAdminTokenAsync()
    {
        using var request = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "testclient-admin",
                ["client_secret"] = "admin-test-secret-2024",
                ["scope"] = "clients.read clients.create clients.update clients.delete settings.read"
            });
        using var response = await _adminClient.PostAsync("/connect/token", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await ReadRequiredStringAsync(response, "access_token");
    }

    private static async Task<string> ReadRequiredStringAsync(
        HttpResponseMessage response,
        string propertyName)
    {
        using var payload = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        Assert.True(payload.RootElement.TryGetProperty(propertyName, out var value));
        Assert.Equal(JsonValueKind.String, value.ValueKind);
        var result = value.GetString();
        Assert.False(string.IsNullOrWhiteSpace(result));
        return result!;
    }

    private static string BuildAuthorizeUrl(string clientId, string redirectUri)
    {
        var codeChallenge = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return "/connect/authorize" +
               $"?client_id={Uri.EscapeDataString(clientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               "&response_type=code" +
               "&scope=openid" +
               $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
               "&code_challenge_method=S256";
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private sealed record CreatedClient(string Id, string? Secret);
}
