using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Core.Application.DTOs;
using Xunit;

namespace Tests.SystemTests;

[Collection("Shared Server")]
public sealed class ConfidentialClientSecretRotationSystemTests : IAsyncLifetime
{
    private const string StandardUserEmail = "testuser@hybridauth.local";
    private const string StandardUserPassword = "Test@123";
    private const string AuthorizationScopes = "openid";

    private static readonly IReadOnlyList<string> AuthorizationCodePermissions =
    [
        "ept:authorization",
        "ept:token",
        "ept:end_session",
        "gt:authorization_code",
        "rst:code",
        "scp:openid"
    ];

    private static readonly IReadOnlyList<string> ClientCredentialsPermissions =
    [
        "ept:token",
        "gt:client_credentials"
    ];

    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _adminClient;
    private readonly HttpClient _browserClient;

    public ConfidentialClientSecretRotationSystemTests(WebIdPServerFixture serverFixture)
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
    public async Task SecretRotation_ShouldBeAtomicAndPreserveAuthorizationSessionUntilLogout()
    {
        var runId = Guid.NewGuid().ToString("N");
        var authorizationClientId = $"rotation-system-auth-{runId}";
        var probeClientId = $"rotation-system-probe-{runId}";
        var redirectUri = $"{_serverFixture.BaseUrl}/signin-oidc";
        var postLogoutRedirectUri = $"{_serverFixture.BaseUrl}/signout-callback-oidc";
        string? createdAuthorizationClientId = null;
        string? createdProbeClientId = null;

        try
        {
            await CreateClientAsync(
                authorizationClientId,
                [redirectUri],
                [postLogoutRedirectUri],
                AuthorizationCodePermissions,
                id => createdAuthorizationClientId = id);
            var createdProbeClient = await CreateClientAsync(
                probeClientId,
                [],
                [],
                ClientCredentialsPermissions,
                id => createdProbeClientId = id);
            var initialSecret = createdProbeClient.InitialSecret;

            await AssertAuthenticationStatusAsync(
                probeClientId,
                initialSecret,
                HttpStatusCode.OK);

            var rejectedCandidateSecret = GenerateRuntimeSecret();
            using (var rejectedUpdateResponse = await UpdateClientAsync(
                       createdProbeClientId,
                       probeClientId,
                       rejectedCandidateSecret,
                       ["not-a-valid-absolute-uri"],
                       [],
                       "Rejected secret update",
                       ClientCredentialsPermissions))
            {
                Assert.Equal(HttpStatusCode.BadRequest, rejectedUpdateResponse.StatusCode);
            }

            await AssertAuthenticationStatusAsync(
                probeClientId,
                initialSecret,
                HttpStatusCode.OK);
            await AssertAuthenticationStatusAsync(
                probeClientId,
                rejectedCandidateSecret,
                HttpStatusCode.Unauthorized);

            var replacementSecret = GenerateRuntimeSecret();
            using (var successfulUpdateResponse = await UpdateClientAsync(
                       createdProbeClientId,
                       probeClientId,
                       replacementSecret,
                       [],
                       [],
                       "Rotated confidential client",
                       ClientCredentialsPermissions))
            {
                Assert.Equal(HttpStatusCode.OK, successfulUpdateResponse.StatusCode);
            }

            await AssertAuthenticationStatusAsync(
                probeClientId,
                replacementSecret,
                HttpStatusCode.OK);
            await AssertAuthenticationStatusAsync(
                probeClientId,
                initialSecret,
                HttpStatusCode.Unauthorized);

            using (var metadataUpdateResponse = await UpdateClientAsync(
                       createdProbeClientId,
                       probeClientId,
                       null,
                       [],
                       [],
                       "Metadata-only confidential client update",
                       ClientCredentialsPermissions))
            {
                Assert.Equal(HttpStatusCode.OK, metadataUpdateResponse.StatusCode);
            }

            await AssertAuthenticationStatusAsync(
                probeClientId,
                replacementSecret,
                HttpStatusCode.OK);

            var regeneratedSecret = await RegenerateSecretAsync(createdProbeClientId);

            await AssertAuthenticationStatusAsync(
                probeClientId,
                regeneratedSecret,
                HttpStatusCode.OK);
            await AssertAuthenticationStatusAsync(
                probeClientId,
                replacementSecret,
                HttpStatusCode.Unauthorized);

            var authorizationReplacementSecret = GenerateRuntimeSecret();
            using (var authorizationUpdateResponse = await UpdateClientAsync(
                       createdAuthorizationClientId,
                       authorizationClientId,
                       authorizationReplacementSecret,
                       [redirectUri],
                       [postLogoutRedirectUri],
                       "Rotated authorization code client",
                       AuthorizationCodePermissions))
            {
                Assert.Equal(HttpStatusCode.OK, authorizationUpdateResponse.StatusCode);
            }

            await CompleteAuthorizationCodeFlowAndLogoutAsync(
                authorizationClientId,
                authorizationReplacementSecret,
                redirectUri,
                postLogoutRedirectUri);
        }
        finally
        {
            try
            {
                if (createdProbeClientId is not null)
                {
                    await DeleteClientAsync(createdProbeClientId);
                }
            }
            finally
            {
                if (createdAuthorizationClientId is not null)
                {
                    await DeleteClientAsync(createdAuthorizationClientId);
                }
            }
        }
    }

    private async Task<(string Id, string InitialSecret)> CreateClientAsync(
        string clientId,
        List<string> redirectUris,
        List<string> postLogoutRedirectUris,
        IReadOnlyList<string> permissions,
        Action<string> trackCreatedClient)
    {
        var request = new CreateClientRequest(
            ClientId: clientId,
            ClientSecret: null,
            DisplayName: "Confidential client rotation system test",
            ApplicationType: "web",
            Type: "confidential",
            ConsentType: "explicit",
            RedirectUris: redirectUris,
            PostLogoutRedirectUris: postLogoutRedirectUris,
            Permissions: permissions.ToList(),
            SupportedRoles: null)
        {
            RequirePkce = true
        };

        using var response = await _adminClient.PostAsJsonAsync("/api/admin/clients", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        Assert.True(document.RootElement.TryGetProperty("id", out var idElement));
        Assert.Equal(JsonValueKind.String, idElement.ValueKind);

        var id = idElement.GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));
        trackCreatedClient(id!);

        Assert.True(document.RootElement.TryGetProperty("clientSecret", out var secretElement));
        Assert.Equal(JsonValueKind.String, secretElement.ValueKind);
        var initialSecret = secretElement.GetString();
        Assert.False(string.IsNullOrWhiteSpace(initialSecret));

        return (id!, initialSecret!);
    }

    private Task<HttpResponseMessage> UpdateClientAsync(
        string createdClientId,
        string clientId,
        string? clientSecret,
        List<string> redirectUris,
        List<string> postLogoutRedirectUris,
        string displayName,
        IReadOnlyList<string> permissions)
    {
        var request = new UpdateClientRequest(
            ClientId: clientId,
            ClientSecret: clientSecret,
            DisplayName: displayName,
            Type: "confidential",
            ConsentType: "explicit",
            RedirectUris: redirectUris,
            PostLogoutRedirectUris: postLogoutRedirectUris,
            Permissions: permissions.ToList(),
            SupportedRoles: null)
        {
            RequirePkce = true
        };

        return _adminClient.PutAsJsonAsync(
            $"/api/admin/clients/{createdClientId}",
            request);
    }

    private async Task DeleteClientAsync(string createdClientId)
    {
        using var deleteResponse =
            await _adminClient.DeleteAsync($"/api/admin/clients/{createdClientId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
    }

    private async Task<string> RegenerateSecretAsync(string createdClientId)
    {
        using var response = await _adminClient.PostAsync(
            $"/api/admin/clients/{createdClientId}/regenerate-secret",
            null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        Assert.True(
            document.RootElement.TryGetProperty("clientSecret", out var secretElement));
        Assert.Equal(JsonValueKind.String, secretElement.ValueKind);

        var regeneratedSecret = secretElement.GetString();
        Assert.False(string.IsNullOrWhiteSpace(regeneratedSecret));
        return regeneratedSecret!;
    }

    private async Task AssertAuthenticationStatusAsync(
        string clientId,
        string clientSecret,
        HttpStatusCode expectedStatus)
    {
        var postStatus = await ProbeClientAuthenticationAsync(
            clientId,
            clientSecret,
            useBasicAuthentication: false);
        Assert.Equal(expectedStatus, postStatus);

        var basicStatus = await ProbeClientAuthenticationAsync(
            clientId,
            clientSecret,
            useBasicAuthentication: true);
        Assert.Equal(expectedStatus, basicStatus);
    }

    private async Task<HttpStatusCode> ProbeClientAuthenticationAsync(
        string clientId,
        string clientSecret,
        bool useBasicAuthentication)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token");
        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials"
        };

        if (useBasicAuthentication)
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
        else
        {
            parameters["client_id"] = clientId;
            parameters["client_secret"] = clientSecret;
        }

        request.Content = new FormUrlEncodedContent(parameters);
        using var response = await _browserClient.SendAsync(request);
        return response.StatusCode;
    }

    private async Task CompleteAuthorizationCodeFlowAndLogoutAsync(
        string clientId,
        string clientSecret,
        string redirectUri,
        string postLogoutRedirectUri)
    {
        var (codeChallenge, codeVerifier) = GeneratePkce();
        var authorizeUrl =
            $"/connect/authorize?client_id={HttpUtility.UrlEncode(clientId)}" +
            $"&redirect_uri={HttpUtility.UrlEncode(redirectUri)}" +
            "&response_type=code" +
            $"&scope={HttpUtility.UrlEncode(AuthorizationScopes)}" +
            "&prompt=consent" +
            $"&code_challenge={HttpUtility.UrlEncode(codeChallenge)}" +
            "&code_challenge_method=S256";

        using var authorizeResponse = await _browserClient.GetAsync(authorizeUrl);
        Assert.Equal(HttpStatusCode.Redirect, authorizeResponse.StatusCode);
        Assert.NotNull(authorizeResponse.Headers.Location);

        using var loginPageResponse =
            await _browserClient.GetAsync(authorizeResponse.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, loginPageResponse.StatusCode);
        var loginPageHtml = await loginPageResponse.Content.ReadAsStringAsync();
        var antiForgeryToken = ExtractAntiForgeryToken(loginPageHtml);

        using var loginContent = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Input.Login", StandardUserEmail),
            new KeyValuePair<string, string>("Input.Password", StandardUserPassword),
            new KeyValuePair<string, string>(
                "__RequestVerificationToken",
                antiForgeryToken)
        ]);
        using var loginResponse = await _browserClient.PostAsync(
            authorizeResponse.Headers.Location,
            loginContent);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.NotNull(loginResponse.Headers.Location);

        using var consentPageResponse =
            await _browserClient.GetAsync(loginResponse.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, consentPageResponse.StatusCode);
        var consentPageHtml = await consentPageResponse.Content.ReadAsStringAsync();

        var consentFields = ExtractHiddenInputs(consentPageHtml);
        consentFields.Add(new KeyValuePair<string, string>("submit", "allow"));
        consentFields.Add(
            new KeyValuePair<string, string>("granted_scopes", AuthorizationScopes));

        using var consentContent = new FormUrlEncodedContent(consentFields);
        using var consentResponse =
            await _browserClient.PostAsync(authorizeUrl, consentContent);
        Assert.Equal(HttpStatusCode.Redirect, consentResponse.StatusCode);
        Assert.NotNull(consentResponse.Headers.Location);

        var redirectLocation = consentResponse.Headers.Location;
        Assert.True(redirectLocation.IsAbsoluteUri);
        Assert.Equal(
            redirectUri,
            redirectLocation.GetLeftPart(UriPartial.Path));

        var authorizationCode =
            HttpUtility.ParseQueryString(redirectLocation.Query)["code"];
        Assert.False(string.IsNullOrWhiteSpace(authorizationCode));

        using var tokenContent = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["code"] = authorizationCode!,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = codeVerifier
            });
        using var tokenResponse =
            await _browserClient.PostAsync("/connect/token", tokenContent);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);

        using (var tokenDocument = await JsonDocument.ParseAsync(
                   await tokenResponse.Content.ReadAsStreamAsync()))
        {
            Assert.True(
                tokenDocument.RootElement.TryGetProperty(
                    "access_token",
                    out var accessTokenElement));
            Assert.Equal(JsonValueKind.String, accessTokenElement.ValueKind);
        }

        using (var authenticatedResponse = await _browserClient.GetAsync("/Dashboard"))
        {
            Assert.Equal(HttpStatusCode.OK, authenticatedResponse.StatusCode);
        }

        var logoutUrl =
            $"/connect/logout?client_id={HttpUtility.UrlEncode(clientId)}" +
            $"&post_logout_redirect_uri={HttpUtility.UrlEncode(postLogoutRedirectUri)}";
        using var logoutPageResponse = await _browserClient.GetAsync(logoutUrl);
        Assert.Equal(HttpStatusCode.OK, logoutPageResponse.StatusCode);
        var logoutPageHtml = await logoutPageResponse.Content.ReadAsStringAsync();

        using var logoutContent =
            new FormUrlEncodedContent(ExtractHiddenInputs(logoutPageHtml));
        using var logoutResponse =
            await _browserClient.PostAsync("/connect/logout", logoutContent);
        Assert.Equal(HttpStatusCode.Redirect, logoutResponse.StatusCode);

        using var signedOutResponse = await _browserClient.GetAsync("/Dashboard");
        Assert.Equal(HttpStatusCode.Redirect, signedOutResponse.StatusCode);
        Assert.NotNull(signedOutResponse.Headers.Location);
        var signedOutLocation = signedOutResponse.Headers.Location;
        var signedOutPath = signedOutLocation.IsAbsoluteUri
            ? signedOutLocation.AbsolutePath
            : new Uri(new Uri(_serverFixture.BaseUrl), signedOutLocation).AbsolutePath;
        Assert.Equal("/Account/Login", signedOutPath, ignoreCase: true);
    }

    private async Task<string> GetAdminTokenAsync()
    {
        using var tokenRequest = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "testclient-admin",
                ["client_secret"] = "admin-test-secret-2024",
                ["scope"] = "clients.read clients.create clients.update clients.delete"
            });
        using var response =
            await _adminClient.PostAsync("/connect/token", tokenRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        Assert.True(
            document.RootElement.TryGetProperty("access_token", out var tokenElement));
        Assert.Equal(JsonValueKind.String, tokenElement.ValueKind);

        var adminToken = tokenElement.GetString();
        Assert.False(string.IsNullOrWhiteSpace(adminToken));
        return adminToken!;
    }

    private static (string CodeChallenge, string CodeVerifier) GeneratePkce()
    {
        var codeVerifier = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return (Base64UrlEncode(challengeBytes), codeVerifier);
    }

    private static string GenerateRuntimeSecret()
    {
        return Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            match = Regex.Match(
                html,
                @"value=""([^""]+)""[^>]*name=""__RequestVerificationToken""",
                RegexOptions.IgnoreCase);
        }

        Assert.True(match.Success);
        return HttpUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static List<KeyValuePair<string, string>> ExtractHiddenInputs(string html)
    {
        var inputs = new List<KeyValuePair<string, string>>();
        foreach (Match inputMatch in Regex.Matches(
                     html,
                     @"<input[^>]*type=""hidden""[^>]*>",
                     RegexOptions.IgnoreCase))
        {
            var nameMatch = Regex.Match(
                inputMatch.Value,
                @"name=""([^""]+)""",
                RegexOptions.IgnoreCase);
            if (!nameMatch.Success)
            {
                continue;
            }

            var valueMatch = Regex.Match(
                inputMatch.Value,
                @"value=""([^""]*)""",
                RegexOptions.IgnoreCase);
            var value = valueMatch.Success
                ? HttpUtility.HtmlDecode(valueMatch.Groups[1].Value)
                : string.Empty;
            inputs.Add(
                new KeyValuePair<string, string>(
                    HttpUtility.HtmlDecode(nameMatch.Groups[1].Value),
                    value));
        }

        Assert.NotEmpty(inputs);
        return inputs.Distinct().ToList();
    }
}
