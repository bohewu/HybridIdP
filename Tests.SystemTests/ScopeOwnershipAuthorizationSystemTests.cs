using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Core.Domain.Constants;

namespace Tests.SystemTests;

[Collection(IsolatedClientAdminHostCollection.Name)]
public sealed class ScopeOwnershipAuthorizationSystemTests : IAsyncLifetime
{
    private const string TestPrefix = "t2_scope_ownership_";
    private const string ApplicationManagerEmail = "appmanager@hybridauth.local";
    private const string ApplicationManagerPassword = "AppManager@123";
    private const string OriginalDisplayName = "Ownership Test Scope";
    private const string UpdatedDisplayName = "Ownership Test Scope Updated";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly WebIdPServerFixture _serverFixture;
    private readonly List<CreatedScope> _createdScopes = [];
    private HttpClient? _applicationManagerClient;
    private HttpClient? _adminPersonClient;

    public ScopeOwnershipAuthorizationSystemTests(WebIdPServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
    }

    public async Task InitializeAsync()
    {
        Assert.True(_serverFixture.IsRunning);

        _applicationManagerClient = await CreateCookieAuthenticatedClientAsync(
            ApplicationManagerEmail,
            ApplicationManagerPassword);
        _adminPersonClient = await CreateCookieAuthenticatedClientAsync(
            AuthConstants.DefaultAdmin.Email,
            AuthConstants.DefaultAdmin.Password);

        await CleanupByPrefixAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            await CleanupTrackedScopesAsync();
            await CleanupByPrefixAsync();
        }
        finally
        {
            _adminPersonClient?.Dispose();
            _applicationManagerClient?.Dispose();
        }
    }

    [Theory]
    [InlineData(ScopeMutation.Update)]
    [InlineData(ScopeMutation.Delete)]
    [InlineData(ScopeMutation.UpdateClaims)]
    public async Task ApplicationManager_CrossOwnerScopeMutation_ReturnsForbiddenWithoutChangingScope(
        ScopeMutation mutation)
    {
        var target = await CreateScopeAsync(AdminPersonClient, "cross_owner");
        var claimId = await GetAnyClaimIdAsync();
        if (mutation == ScopeMutation.UpdateClaims)
        {
            await SetScopeClaimsAsync(AdminPersonClient, target.Id, [claimId]);
        }

        var before = await GetScopeStateAsync(target);
        using var response = await InvokeMutationAsync(
            ApplicationManagerClient,
            target,
            mutation,
            claimId,
            crossOwnerAttempt: true);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(target.Name, body, StringComparison.Ordinal);
        Assert.DoesNotContain(target.Id, body, StringComparison.Ordinal);

        var after = await GetScopeStateAsync(target);
        Assert.Equal(before.Exists, after.Exists);
        Assert.Equal(before.DisplayName, after.DisplayName);
        Assert.Equal(before.ClaimIds, after.ClaimIds);
    }

    [Theory]
    [InlineData(ScopeMutation.Update)]
    [InlineData(ScopeMutation.Delete)]
    [InlineData(ScopeMutation.UpdateClaims)]
    public async Task SameOwnerApplicationManager_ScopeMutation_SucceedsAndPersists(
        ScopeMutation mutation)
    {
        var target = await CreateScopeAsync(ApplicationManagerClient, "same_owner");
        var claimId = await GetAnyClaimIdAsync();

        using var response = await InvokeMutationAsync(
            ApplicationManagerClient,
            target,
            mutation,
            claimId,
            crossOwnerAttempt: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertMutationPersistedAsync(target, mutation, claimId);
    }

    [Theory]
    [InlineData(ScopeMutation.Update)]
    [InlineData(ScopeMutation.Delete)]
    [InlineData(ScopeMutation.UpdateClaims)]
    public async Task Admin_CrossOwnerScopeMutation_SucceedsAndPersists(
        ScopeMutation mutation)
    {
        var target = await CreateScopeAsync(ApplicationManagerClient, "admin_bypass");
        var claimId = await GetAnyClaimIdAsync();

        using var response = await InvokeMutationAsync(
            AdminPersonClient,
            target,
            mutation,
            claimId,
            crossOwnerAttempt: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertMutationPersistedAsync(target, mutation, claimId);
    }

    private async Task<CreatedScope> CreateScopeAsync(HttpClient actor, string suffix)
    {
        var name = $"{TestPrefix}{suffix}_{Guid.NewGuid():N}";
        using var response = await actor.PostAsJsonAsync(
            "/api/admin/scopes",
            new
            {
                name,
                displayName = OriginalDisplayName,
                description = "Disposable scope ownership regression"
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));

        var created = new CreatedScope(id!, name);
        _createdScopes.Add(created);
        return created;
    }

    private static async Task<HttpResponseMessage> InvokeMutationAsync(
        HttpClient actor,
        CreatedScope target,
        ScopeMutation mutation,
        int claimId,
        bool crossOwnerAttempt)
    {
        return mutation switch
        {
            ScopeMutation.Update => await actor.PutAsJsonAsync(
                $"/api/admin/scopes/{target.Id}",
                new { displayName = UpdatedDisplayName }),
            ScopeMutation.Delete => await actor.DeleteAsync(
                $"/api/admin/scopes/{Uri.EscapeDataString(target.Name)}"),
            ScopeMutation.UpdateClaims => await actor.PutAsJsonAsync(
                $"/api/admin/scopes/{target.Id}/claims",
                new
                {
                    claimIds = crossOwnerAttempt
                        ? Array.Empty<int>()
                        : new[] { claimId }
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
    }

    private async Task AssertMutationPersistedAsync(
        CreatedScope target,
        ScopeMutation mutation,
        int claimId)
    {
        if (mutation == ScopeMutation.Delete)
        {
            using var missingResponse = await AdminPersonClient.GetAsync(
                $"/api/admin/scopes/{target.Id}");
            Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
            return;
        }

        var state = await GetScopeStateAsync(target);
        Assert.True(state.Exists);
        if (mutation == ScopeMutation.Update)
        {
            Assert.Equal(UpdatedDisplayName, state.DisplayName);
        }
        else
        {
            Assert.Contains(claimId, state.ClaimIds);
        }
    }

    private async Task<ScopeState> GetScopeStateAsync(CreatedScope target)
    {
        using var detailResponse = await AdminPersonClient.GetAsync(
            $"/api/admin/scopes/{target.Id}");
        if (detailResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return new ScopeState(false, null, []);
        }

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();

        using var claimsResponse = await AdminPersonClient.GetAsync(
            $"/api/admin/scopes/{target.Id}/claims");
        Assert.Equal(HttpStatusCode.OK, claimsResponse.StatusCode);
        var claims = await claimsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var claimIds = claims.GetProperty("claims")
            .EnumerateArray()
            .Select(claim => claim.GetProperty("claimId").GetInt32())
            .OrderBy(id => id)
            .ToArray();

        return new ScopeState(
            true,
            detail.GetProperty("displayName").GetString(),
            claimIds);
    }

    private async Task<int> GetAnyClaimIdAsync()
    {
        using var response = await AdminPersonClient.GetAsync(
            "/api/admin/claims?skip=0&take=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items");
        Assert.NotEqual(0, items.GetArrayLength());
        return items[0].GetProperty("id").GetInt32();
    }

    private static async Task SetScopeClaimsAsync(
        HttpClient actor,
        string scopeId,
        int[] claimIds)
    {
        using var response = await actor.PutAsJsonAsync(
            $"/api/admin/scopes/{scopeId}/claims",
            new { claimIds });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpClient> CreateCookieAuthenticatedClientAsync(
        string login,
        string password)
    {
        var client = CreateHttpClient();
        try
        {
            using var loginPageResponse = await client.GetAsync("/Account/Login");
            Assert.Equal(HttpStatusCode.OK, loginPageResponse.StatusCode);
            var loginPageHtml = await loginPageResponse.Content.ReadAsStringAsync();
            var antiForgeryToken = ExtractAntiForgeryToken(loginPageHtml);

            using var loginForm = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["Input.Login"] = login,
                    ["Input.Password"] = password,
                    ["__RequestVerificationToken"] = antiForgeryToken
                });
            using var loginResponse = await client.PostAsync("/Account/Login", loginForm);
            Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);

            using var profileResponse = await client.GetAsync("/Account/Profile");
            Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
            var profileHtml = await profileResponse.Content.ReadAsStringAsync();
            client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", ExtractCsrfToken(profileHtml));
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = true,
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        return new HttpClient(handler)
        {
            BaseAddress = new Uri(_serverFixture.BaseUrl)
        };
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""");
        if (!match.Success)
        {
            match = Regex.Match(
                html,
                @"value=""([^""]+)""[^>]*name=""__RequestVerificationToken""");
        }

        Assert.True(match.Success);
        return match.Groups[1].Value;
    }

    private static string ExtractCsrfToken(string html)
    {
        var match = Regex.Match(
            html,
            @"meta\s+name=""csrf-token""\s+content=""([^""]+)""");
        if (!match.Success)
        {
            match = Regex.Match(
                html,
                @"meta\s+content=""([^""]+)""\s+name=""csrf-token""");
        }

        Assert.True(match.Success);
        return match.Groups[1].Value;
    }

    private async Task CleanupByPrefixAsync()
    {
        using var response = await AdminPersonClient.GetAsync(
            $"/api/admin/scopes?skip=0&take=100&search={Uri.EscapeDataString(TestPrefix)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var item in body.GetProperty("items").EnumerateArray())
        {
            var name = item.GetProperty("name").GetString();
            if (name?.StartsWith(TestPrefix, StringComparison.Ordinal) == true)
            {
                using var deleteResponse = await AdminPersonClient.DeleteAsync(
                    $"/api/admin/scopes/{Uri.EscapeDataString(name)}");
                Assert.True(
                    deleteResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest);
            }
        }
    }

    private async Task CleanupTrackedScopesAsync()
    {
        foreach (var target in _createdScopes.AsEnumerable().Reverse())
        {
            using var response = await AdminPersonClient.DeleteAsync(
                $"/api/admin/scopes/{Uri.EscapeDataString(target.Name)}");
            Assert.True(
                response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest);
        }

        _createdScopes.Clear();
    }

    private HttpClient ApplicationManagerClient =>
        _applicationManagerClient ??
        throw new InvalidOperationException("ApplicationManager client is not initialized.");

    private HttpClient AdminPersonClient =>
        _adminPersonClient ?? throw new InvalidOperationException("Admin client is not initialized.");

    public enum ScopeMutation
    {
        Update,
        Delete,
        UpdateClaims
    }

    private sealed record CreatedScope(string Id, string Name);

    private sealed record ScopeState(bool Exists, string? DisplayName, int[] ClaimIds);
}
