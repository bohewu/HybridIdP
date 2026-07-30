using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Core.Application.DTOs;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;

namespace Tests.SystemTests;

[Collection(IsolatedClientAdminHostCollection.Name)]
public sealed class ClientOwnershipAuthorizationSystemTests : IAsyncLifetime
{
    private const string TestPrefix = "t2_ownership_";
    private const string ApplicationManagerEmail = "appmanager@hybridauth.local";
    private const string ApplicationManagerPassword = "AppManager@123";
    private const string TrustedAutomationClientId = "testclient-admin";
    private const string TrustedAutomationClientSecret = "admin-test-secret-2024";
    private const string CompanyReadScope = "api:company:read";
    private const string CompanyWriteScope = "api:company:write";
    private const string InventoryReadScope = "api:inventory:read";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly WebIdPServerFixture _serverFixture;
    private readonly List<CreatedClient> _createdClients = [];
    private HttpClient? _anonymousClient;
    private HttpClient? _administrationAutomationClient;
    private HttpClient? _applicationManagerClient;
    private HttpClient? _adminPersonClient;

    public ClientOwnershipAuthorizationSystemTests(WebIdPServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
    }

    public async Task InitializeAsync()
    {
        Assert.True(_serverFixture.IsRunning);

        _anonymousClient = CreateHttpClient();
        await RefreshAdministrationAutomationClientAsync();

        await CleanupByPrefixAsync();

        _applicationManagerClient = await CreateCookieAuthenticatedClientAsync(
            ApplicationManagerEmail,
            ApplicationManagerPassword);
        _adminPersonClient = await CreateCookieAuthenticatedClientAsync(
            AuthConstants.DefaultAdmin.Email,
            AuthConstants.DefaultAdmin.Password);
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (_administrationAutomationClient != null)
            {
                await RefreshAdministrationAutomationClientAsync();
                await CleanupTrackedClientsAsync();
                await CleanupByPrefixAsync();
                await AssertNoDisposableClientsRemainAsync();
            }
        }
        finally
        {
            _adminPersonClient?.Dispose();
            _applicationManagerClient?.Dispose();
            _administrationAutomationClient?.Dispose();
            _anonymousClient?.Dispose();
        }
    }

    [Fact]
    public async Task SameOwnerApplicationManager_AllClientMutations_ReturnOkAndPersistEffects()
    {
        var originalSecret = CreateSecret();
        var target = await CreateDisposableClientAsync(
            ApplicationManagerClient,
            "same_owner",
            originalSecret);

        await AssertAuthorizedMutationMatrixAsync(
            ApplicationManagerClient,
            target,
            originalSecret);
    }

    [Fact]
    public async Task AdminRolePerson_AllClientMutations_BypassDifferentOwnerAndPersistEffects()
    {
        var originalSecret = CreateSecret();
        var applicationManagerOwnedTarget = await CreateDisposableClientAsync(
            ApplicationManagerClient,
            "admin_bypass",
            originalSecret);

        await AssertAuthorizedMutationMatrixAsync(
            AdminPersonClient,
            applicationManagerOwnedTarget,
            originalSecret);
    }

    [Fact]
    public async Task RecognizedAdministrationAutomation_AllClientMutations_ReturnOkAndPersistEffects()
    {
        var originalSecret = CreateSecret();
        var unownedTarget = await CreateDisposableClientAsync(
            AdministrationAutomationClient,
            "trusted_automation",
            originalSecret);

        await AssertAuthorizedMutationMatrixAsync(
            AdministrationAutomationClient,
            unownedTarget,
            originalSecret);
    }

    [Fact]
    public async Task SameSubjectWithFixtureTrustDisabled_AllClientMutations_ReturnForbiddenWithoutEffects()
    {
        var originalSecret = CreateSecret();
        var target = await CreateDisposableClientAsync(
            AdministrationAutomationClient,
            "same_subject_untrusted",
            originalSecret);
        await SetInitialRequiredScopeAsync(target);

        await _serverFixture.RunIsolatedClientAdminHostAsync(
            enablePrivilegedTestAdminBootstrap: false,
            disableClientWriteEndpoints: false,
            async () =>
            {
                using var anonymousClient = CreateHttpClient();
                var token = await GetClientCredentialsTokenAsync(
                    anonymousClient,
                    TrustedAutomationClientId,
                    TrustedAutomationClientSecret,
                    ["clients.read", "clients.update", "clients.delete"]);
                using var sameSubjectClient = CreateHttpClient();
                sameSubjectClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                await AssertDeniedMutationMatrixAsync(
                    sameSubjectClient,
                    target,
                    originalSecret,
                    sameSubjectClient);
            });
    }

    [Fact]
    public async Task ClientWriteHardening_AllClientMutations_ReturnLockedBeforeLookupAndWithoutEffects()
    {
        var originalSecret = CreateSecret();
        var target = await CreateDisposableClientAsync(
            AdministrationAutomationClient,
            "hardening_locked",
            originalSecret);
        await SetInitialRequiredScopeAsync(target);

        await _serverFixture.RunIsolatedClientAdminHostAsync(
            enablePrivilegedTestAdminBootstrap: true,
            disableClientWriteEndpoints: true,
            async () =>
            {
                using var anonymousClient = CreateHttpClient();
                var token = await GetClientCredentialsTokenAsync(
                    anonymousClient,
                    TrustedAutomationClientId,
                    TrustedAutomationClientSecret,
                    ["clients.read", "clients.update", "clients.delete"]);
                using var hardenedClient = CreateHttpClient();
                hardenedClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                await AssertLockedMutationMatrixAsync(
                    hardenedClient,
                    target,
                    originalSecret);
                await AssertHardeningPrecedesTargetLookupAsync(hardenedClient);
            });
    }

    [Fact]
    public async Task RestrictedCallers_AllClientMutations_ReturnForbiddenWithoutStateOrCredentialChanges()
    {
        var ownedTargetSecret = CreateSecret();
        var adminOwnedTarget = await CreateDisposableClientAsync(
            AdminPersonClient,
            "cross_owner",
            ownedTargetSecret);
        await SetInitialRequiredScopeAsync(adminOwnedTarget);

        var unownedTargetSecret = CreateSecret();
        var unownedTarget = await CreateDisposableClientAsync(
            AdministrationAutomationClient,
            "unowned",
            unownedTargetSecret);
        await SetInitialRequiredScopeAsync(unownedTarget);

        var servicePrincipalSecret = CreateSecret();
        var servicePrincipal = await CreateDisposableServicePrincipalAsync(servicePrincipalSecret);
        var servicePrincipalToken = await GetClientCredentialsTokenAsync(
            AnonymousClient,
            servicePrincipal.ClientId,
            servicePrincipalSecret,
            ["clients.update", "clients.delete"]);
        using var servicePrincipalClient = CreateHttpClient();
        servicePrincipalClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", servicePrincipalToken);

        var applicationManagerCrossOwnerShape = await AssertDeniedMutationMatrixAsync(
            ApplicationManagerClient,
            adminOwnedTarget,
            ownedTargetSecret);
        var applicationManagerUnownedShape = await AssertDeniedMutationMatrixAsync(
            ApplicationManagerClient,
            unownedTarget,
            unownedTargetSecret);
        Assert.Equal(applicationManagerCrossOwnerShape, applicationManagerUnownedShape);

        var servicePrincipalOwnedShape = await AssertDeniedMutationMatrixAsync(
            servicePrincipalClient,
            adminOwnedTarget,
            ownedTargetSecret);
        var servicePrincipalUnownedShape = await AssertDeniedMutationMatrixAsync(
            servicePrincipalClient,
            unownedTarget,
            unownedTargetSecret);
        Assert.Equal(servicePrincipalOwnedShape, servicePrincipalUnownedShape);
    }

    [Fact]
    public async Task ClientMutationErrors_MalformedAndMissingTargetsRemainBadRequestAndNotFound()
    {
        var malformedUpdate = CreateUpdateRequest(
            $"{TestPrefix}malformed",
            CreateSecret(),
            "Malformed");
        using (var response = await ApplicationManagerClient.PutAsJsonAsync(
                   "/api/admin/clients/not-a-guid",
                   malformedUpdate))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var missingId = Guid.NewGuid();
        var missingUpdate = CreateUpdateRequest(
            $"{TestPrefix}missing",
            CreateSecret(),
            "Missing");

        using (var response = await ApplicationManagerClient.PutAsJsonAsync(
                   $"/api/admin/clients/{missingId}",
                   missingUpdate))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using (var response = await ApplicationManagerClient.PostAsync(
                   $"/api/admin/clients/{missingId}/regenerate-secret",
                   null))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using (var response = await ApplicationManagerClient.PutAsJsonAsync(
                   $"/api/admin/clients/{missingId}/scopes",
                   new { scopes = new[] { CompanyReadScope } }))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using (var response = await ApplicationManagerClient.PutAsJsonAsync(
                   $"/api/admin/clients/{missingId}/required-scopes",
                   new { scopes = new[] { CompanyReadScope } }))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using (var response = await ApplicationManagerClient.DeleteAsync(
                   $"/api/admin/clients/{missingId}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    private async Task AssertAuthorizedMutationMatrixAsync(
        HttpClient actor,
        CreatedClient target,
        string originalSecret)
    {
        Assert.True(await CanAuthenticateClientAsync(target.ClientId, originalSecret, CompanyReadScope));

        var replacementSecret = CreateSecret();
        var updatedDisplayName = $"{target.DisplayName} Updated";
        var updateRequest = CreateUpdateRequest(
            target.ClientId,
            replacementSecret,
            updatedDisplayName);

        using (var updateResponse = await actor.PutAsJsonAsync(
                   $"/api/admin/clients/{target.Id}",
                   updateRequest))
        {
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            var body = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(target.Id, body.GetProperty("id").GetString());
            Assert.True(body.TryGetProperty("message", out _));
        }

        var afterUpdate = await GetClientStateAsync(target);
        Assert.Equal(updatedDisplayName, afterUpdate.DisplayName);
        Assert.Contains(
            $"{OpenIddictConstants.Permissions.Prefixes.Scope}{InventoryReadScope}",
            afterUpdate.Permissions);
        Assert.True(afterUpdate.DisableExternalProviders);
        Assert.True(afterUpdate.EnableTurnstile);
        Assert.False(await CanAuthenticateClientAsync(target.ClientId, originalSecret, CompanyReadScope));
        Assert.True(await CanAuthenticateClientAsync(target.ClientId, replacementSecret, CompanyReadScope));

        using (var allowedScopesResponse = await actor.PutAsJsonAsync(
                   $"/api/admin/clients/{target.Id}/scopes",
                   new { scopes = new[] { CompanyReadScope, CompanyWriteScope } }))
        {
            Assert.Equal(HttpStatusCode.OK, allowedScopesResponse.StatusCode);
            var body = await allowedScopesResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(body.TryGetProperty("message", out _));
        }

        var afterAllowedScopes = await GetClientStateAsync(target);
        Assert.Equal(
            [CompanyReadScope, CompanyWriteScope],
            afterAllowedScopes.AllowedScopes);

        using (var requiredScopesResponse = await actor.PutAsJsonAsync(
                   $"/api/admin/clients/{target.Id}/required-scopes",
                   new { scopes = new[] { CompanyWriteScope } }))
        {
            Assert.Equal(HttpStatusCode.OK, requiredScopesResponse.StatusCode);
            var body = await requiredScopesResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(body.TryGetProperty("message", out _));
        }

        var afterRequiredScopes = await GetClientStateAsync(target);
        Assert.Equal([CompanyWriteScope], afterRequiredScopes.RequiredScopes);

        string regeneratedSecret;
        using (var regenerationResponse = await actor.PostAsync(
                   $"/api/admin/clients/{target.Id}/regenerate-secret",
                   null))
        {
            Assert.Equal(HttpStatusCode.OK, regenerationResponse.StatusCode);
            var body = await regenerationResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(body.TryGetProperty("message", out _));
            Assert.True(body.TryGetProperty("clientSecret", out var secretElement));
            regeneratedSecret = secretElement.GetString() ?? string.Empty;
            Assert.False(string.IsNullOrWhiteSpace(regeneratedSecret));
        }

        Assert.False(await CanAuthenticateClientAsync(
            target.ClientId,
            replacementSecret,
            CompanyReadScope));
        Assert.True(await CanAuthenticateClientAsync(
            target.ClientId,
            regeneratedSecret,
            CompanyReadScope));

        using var subsequentRead = await AdministrationAutomationClient.GetAsync(
            $"/api/admin/clients/{target.Id}");
        Assert.Equal(HttpStatusCode.OK, subsequentRead.StatusCode);
        var subsequentBody = await subsequentRead.Content.ReadAsStringAsync();
        Assert.False(subsequentBody.Contains(regeneratedSecret, StringComparison.Ordinal));
        using var subsequentJson = JsonDocument.Parse(subsequentBody);
        Assert.False(subsequentJson.RootElement.TryGetProperty("clientSecret", out _));

        using var deleteResponse = await actor.DeleteAsync(
            $"/api/admin/clients/{target.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        using var deletedClientResponse = await AdministrationAutomationClient.GetAsync(
            $"/api/admin/clients/{target.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deletedClientResponse.StatusCode);
    }

    private async Task<ForbiddenResponseShape> AssertDeniedMutationMatrixAsync(
        HttpClient actor,
        CreatedClient target,
        string currentSecret,
        HttpClient? stateReader = null)
    {
        var before = await GetClientStateAsync(target, stateReader);
        Assert.True(await CanAuthenticateClientAsync(target.ClientId, currentSecret, CompanyReadScope));

        var attemptedReplacementSecret = CreateSecret();
        ForbiddenResponseShape updateShape;
        using (var updateResponse = await actor.PutAsJsonAsync(
                   $"/api/admin/clients/{target.Id}",
                   CreateUpdateRequest(
                       target.ClientId,
                       attemptedReplacementSecret,
                       $"{target.DisplayName} Denied")))
        {
            updateShape = await AssertForbiddenWithoutSensitiveOutputAsync(
                updateResponse,
                attemptedReplacementSecret);
        }

        using (var regenerationResponse = await actor.PostAsync(
                   $"/api/admin/clients/{target.Id}/regenerate-secret",
                   null))
        {
            await AssertForbiddenWithoutSensitiveOutputAsync(
                regenerationResponse,
                attemptedReplacementSecret);
        }

        using (var allowedScopesResponse = await actor.PutAsJsonAsync(
                   $"/api/admin/clients/{target.Id}/scopes",
                   new { scopes = new[] { InventoryReadScope } }))
        {
            await AssertForbiddenWithoutSensitiveOutputAsync(
                allowedScopesResponse,
                attemptedReplacementSecret);
        }

        using (var requiredScopesResponse = await actor.PutAsJsonAsync(
                   $"/api/admin/clients/{target.Id}/required-scopes",
                   new { scopes = new[] { CompanyWriteScope } }))
        {
            await AssertForbiddenWithoutSensitiveOutputAsync(
                requiredScopesResponse,
                attemptedReplacementSecret);
        }

        using (var deleteResponse = await actor.DeleteAsync(
                   $"/api/admin/clients/{target.Id}"))
        {
            await AssertForbiddenWithoutSensitiveOutputAsync(
                deleteResponse,
                attemptedReplacementSecret);
        }

        var after = await GetClientStateAsync(target, stateReader);
        AssertClientStateEqual(before, after);
        Assert.True(await CanAuthenticateClientAsync(target.ClientId, currentSecret, CompanyReadScope));
        Assert.False(await CanAuthenticateClientAsync(
            target.ClientId,
            attemptedReplacementSecret,
            CompanyReadScope));

        return updateShape;
    }

    private async Task AssertLockedMutationMatrixAsync(
        HttpClient actor,
        CreatedClient target,
        string currentSecret)
    {
        var before = await GetClientStateAsync(target, actor);
        Assert.True(await CanAuthenticateClientAsync(target.ClientId, currentSecret, CompanyReadScope));

        var attemptedReplacementSecret = CreateSecret();
        using (var updateResponse = await actor.PutAsJsonAsync(
                   $"/api/admin/clients/{target.Id}",
                   CreateUpdateRequest(
                       target.ClientId,
                       attemptedReplacementSecret,
                       $"{target.DisplayName} Locked")))
        {
            await AssertLockedWithoutSensitiveOutputAsync(
                updateResponse,
                attemptedReplacementSecret);
        }

        using (var regenerationResponse = await actor.PostAsync(
                   $"/api/admin/clients/{target.Id}/regenerate-secret",
                   null))
        {
            await AssertLockedWithoutSensitiveOutputAsync(
                regenerationResponse,
                attemptedReplacementSecret);
        }

        using (var allowedScopesResponse = await actor.PutAsJsonAsync(
                   $"/api/admin/clients/{target.Id}/scopes",
                   new { scopes = new[] { InventoryReadScope } }))
        {
            await AssertLockedWithoutSensitiveOutputAsync(
                allowedScopesResponse,
                attemptedReplacementSecret);
        }

        using (var requiredScopesResponse = await actor.PutAsJsonAsync(
                   $"/api/admin/clients/{target.Id}/required-scopes",
                   new { scopes = new[] { CompanyWriteScope } }))
        {
            await AssertLockedWithoutSensitiveOutputAsync(
                requiredScopesResponse,
                attemptedReplacementSecret);
        }

        using (var deleteResponse = await actor.DeleteAsync(
                   $"/api/admin/clients/{target.Id}"))
        {
            await AssertLockedWithoutSensitiveOutputAsync(
                deleteResponse,
                attemptedReplacementSecret);
        }

        var after = await GetClientStateAsync(target, actor);
        AssertClientStateEqual(before, after);
        Assert.True(await CanAuthenticateClientAsync(target.ClientId, currentSecret, CompanyReadScope));
        Assert.False(await CanAuthenticateClientAsync(
            target.ClientId,
            attemptedReplacementSecret,
            CompanyReadScope));
    }

    private static async Task AssertHardeningPrecedesTargetLookupAsync(HttpClient actor)
    {
        var missingId = Guid.NewGuid();
        var attemptedSecret = CreateSecret();
        using (var updateResponse = await actor.PutAsJsonAsync(
                   $"/api/admin/clients/{missingId}",
                   CreateUpdateRequest(
                       "missing-client",
                       attemptedSecret,
                       "Missing client")))
        {
            await AssertLockedWithoutSensitiveOutputAsync(
                updateResponse,
                attemptedSecret);
        }

        using (var regenerationResponse = await actor.PostAsync(
                   $"/api/admin/clients/{missingId}/regenerate-secret",
                   null))
        {
            await AssertLockedWithoutSensitiveOutputAsync(
                regenerationResponse,
                attemptedSecret);
        }

        using (var allowedScopesResponse = await actor.PutAsJsonAsync(
                   $"/api/admin/clients/{missingId}/scopes",
                   new { scopes = new[] { CompanyReadScope } }))
        {
            await AssertLockedWithoutSensitiveOutputAsync(
                allowedScopesResponse,
                attemptedSecret);
        }

        using (var requiredScopesResponse = await actor.PutAsJsonAsync(
                   $"/api/admin/clients/{missingId}/required-scopes",
                   new { scopes = new[] { CompanyReadScope } }))
        {
            await AssertLockedWithoutSensitiveOutputAsync(
                requiredScopesResponse,
                attemptedSecret);
        }

        using (var deleteResponse = await actor.DeleteAsync(
                   $"/api/admin/clients/{missingId}"))
        {
            await AssertLockedWithoutSensitiveOutputAsync(
                deleteResponse,
                attemptedSecret);
        }
    }

    private static async Task<ForbiddenResponseShape> AssertForbiddenWithoutSensitiveOutputAsync(
        HttpResponseMessage response,
        string attemptedSecret)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.False(body.Contains("clientSecret", StringComparison.OrdinalIgnoreCase));
        Assert.False(body.Contains(attemptedSecret, StringComparison.Ordinal));

        return new ForbiddenResponseShape(
            response.StatusCode,
            response.Content.Headers.ContentType?.MediaType,
            body);
    }

    private static async Task AssertLockedWithoutSensitiveOutputAsync(
        HttpResponseMessage response,
        string attemptedSecret)
    {
        Assert.Equal((HttpStatusCode)StatusCodes.Status423Locked, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.False(body.Contains("clientSecret", StringComparison.OrdinalIgnoreCase));
        Assert.False(body.Contains(attemptedSecret, StringComparison.Ordinal));
    }

    private async Task SetInitialRequiredScopeAsync(CreatedClient target)
    {
        using var response = await AdministrationAutomationClient.PutAsJsonAsync(
            $"/api/admin/clients/{target.Id}/required-scopes",
            new { scopes = new[] { CompanyReadScope } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<CreatedClient> CreateDisposableServicePrincipalAsync(string secret)
    {
        var clientId = $"{TestPrefix}service_principal_{Guid.NewGuid():N}";
        var request = new CreateClientRequest(
            ClientId: clientId,
            ClientSecret: secret,
            DisplayName: "T2 Disposable Service Principal",
            ApplicationType: OpenIddictConstants.ApplicationTypes.Web,
            Type: OpenIddictConstants.ClientTypes.Confidential,
            ConsentType: OpenIddictConstants.ConsentTypes.Implicit,
            RedirectUris: [],
            PostLogoutRedirectUris: [],
            Permissions:
            [
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                $"{OpenIddictConstants.Permissions.Prefixes.Scope}clients.update",
                $"{OpenIddictConstants.Permissions.Prefixes.Scope}clients.delete"
            ],
            SupportedRoles: null);

        return await CreateDisposableClientAsync(
            AdministrationAutomationClient,
            request);
    }

    private async Task<CreatedClient> CreateDisposableClientAsync(
        HttpClient actor,
        string suffix,
        string secret)
    {
        var clientId = $"{TestPrefix}{suffix}_{Guid.NewGuid():N}";
        var request = new CreateClientRequest(
            ClientId: clientId,
            ClientSecret: secret,
            DisplayName: $"T2 {suffix}",
            ApplicationType: OpenIddictConstants.ApplicationTypes.Web,
            Type: OpenIddictConstants.ClientTypes.Confidential,
            ConsentType: OpenIddictConstants.ConsentTypes.Implicit,
            RedirectUris: [],
            PostLogoutRedirectUris: [],
            Permissions:
            [
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                $"{OpenIddictConstants.Permissions.Prefixes.Scope}{CompanyReadScope}",
                $"{OpenIddictConstants.Permissions.Prefixes.Scope}{CompanyWriteScope}"
            ],
            SupportedRoles: null);

        return await CreateDisposableClientAsync(actor, request);
    }

    private async Task<CreatedClient> CreateDisposableClientAsync(
        HttpClient actor,
        CreateClientRequest request)
    {
        using var response = await actor.PostAsJsonAsync("/api/admin/clients", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var created = new CreatedClient(
            body.GetProperty("id").GetString() ?? string.Empty,
            request.ClientId,
            request.DisplayName ?? request.ClientId);
        Assert.False(string.IsNullOrWhiteSpace(created.Id));
        _createdClients.Add(created);
        return created;
    }

    private static UpdateClientRequest CreateUpdateRequest(
        string clientId,
        string replacementSecret,
        string displayName)
    {
        return new UpdateClientRequest(
            ClientId: clientId,
            ClientSecret: replacementSecret,
            DisplayName: displayName,
            Type: OpenIddictConstants.ClientTypes.Confidential,
            ConsentType: OpenIddictConstants.ConsentTypes.Implicit,
            RedirectUris: [],
            PostLogoutRedirectUris: [],
            Permissions:
            [
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                $"{OpenIddictConstants.Permissions.Prefixes.Scope}{CompanyReadScope}",
                $"{OpenIddictConstants.Permissions.Prefixes.Scope}{InventoryReadScope}"
            ],
            SupportedRoles: ["Operator"])
        {
            RequirePkce = false,
            DisableExternalProviders = true,
            EnableTurnstile = true
        };
    }

    private async Task<ClientState> GetClientStateAsync(
        CreatedClient target,
        HttpClient? stateReader = null)
    {
        stateReader ??= AdministrationAutomationClient;

        using var clientResponse = await stateReader.GetAsync(
            $"/api/admin/clients/{target.Id}");
        Assert.Equal(HttpStatusCode.OK, clientResponse.StatusCode);
        var detail = await clientResponse.Content.ReadFromJsonAsync<ClientDetail>(JsonOptions);
        Assert.NotNull(detail);

        using var allowedScopesResponse = await stateReader.GetAsync(
            $"/api/admin/clients/{target.Id}/scopes");
        Assert.Equal(HttpStatusCode.OK, allowedScopesResponse.StatusCode);
        var allowedScopes = await ReadScopesAsync(allowedScopesResponse);

        using var requiredScopesResponse = await stateReader.GetAsync(
            $"/api/admin/clients/{target.Id}/required-scopes");
        Assert.Equal(HttpStatusCode.OK, requiredScopesResponse.StatusCode);
        var requiredScopes = await ReadScopesAsync(requiredScopesResponse);

        return new ClientState(
            detail.ClientId,
            detail.DisplayName,
            detail.Type,
            detail.ApplicationType,
            detail.ConsentType,
            Sorted(detail.RedirectUris),
            Sorted(detail.PostLogoutRedirectUris),
            Sorted(detail.Permissions),
            Sorted(detail.SupportedRoles),
            detail.RequirePkce,
            detail.DisableExternalProviders,
            detail.EnableTurnstile,
            allowedScopes,
            requiredScopes);
    }

    private static async Task<string[]> ReadScopesAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("scopes")
            .EnumerateArray()
            .Select(scope => scope.GetString() ?? string.Empty)
            .OrderBy(scope => scope, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AssertClientStateEqual(ClientState expected, ClientState actual)
    {
        Assert.Equal(expected.ClientId, actual.ClientId);
        Assert.Equal(expected.DisplayName, actual.DisplayName);
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.ApplicationType, actual.ApplicationType);
        Assert.Equal(expected.ConsentType, actual.ConsentType);
        Assert.Equal(expected.RedirectUris, actual.RedirectUris);
        Assert.Equal(expected.PostLogoutRedirectUris, actual.PostLogoutRedirectUris);
        Assert.Equal(expected.Permissions, actual.Permissions);
        Assert.Equal(expected.SupportedRoles, actual.SupportedRoles);
        Assert.Equal(expected.RequirePkce, actual.RequirePkce);
        Assert.Equal(expected.DisableExternalProviders, actual.DisableExternalProviders);
        Assert.Equal(expected.EnableTurnstile, actual.EnableTurnstile);
        Assert.Equal(expected.AllowedScopes, actual.AllowedScopes);
        Assert.Equal(expected.RequiredScopes, actual.RequiredScopes);
    }

    private async Task<bool> CanAuthenticateClientAsync(
        string clientId,
        string clientSecret,
        string scope)
    {
        using var request = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["scope"] = scope
            });
        using var response = await AnonymousClient.PostAsync("/connect/token", request);
        return response.IsSuccessStatusCode;
    }

    private async Task<string> GetClientCredentialsTokenAsync(
        HttpClient client,
        string clientId,
        string clientSecret,
        IReadOnlyCollection<string> scopes)
    {
        using var request = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["scope"] = string.Join(" ", scopes)
            });
        using var response = await client.PostAsync("/connect/token", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("access_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        return token!;
    }

    private async Task<HttpClient> CreateCookieAuthenticatedClientAsync(
        string login,
        string password)
    {
        var client = CreateHttpClient(useCookies: true);
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
            var csrfToken = ExtractCsrfToken(profileHtml);
            client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", csrfToken);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private HttpClient CreateHttpClient(bool useCookies = false)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = useCookies,
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        if (useCookies)
        {
            handler.CookieContainer = new CookieContainer();
        }

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

    private async Task CleanupTrackedClientsAsync()
    {
        List<Exception> failures = [];
        foreach (var created in _createdClients.AsEnumerable().Reverse())
        {
            try
            {
                await DeleteAndVerifyMissingAsync(created);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        _createdClients.Clear();
        if (failures.Count > 0)
        {
            throw new AggregateException("Disposable client cleanup failed.", failures);
        }
    }

    private async Task RefreshAdministrationAutomationClientAsync()
    {
        var anonymousClient = _anonymousClient ??
            throw new InvalidOperationException("The anonymous HTTP client has not been initialized.");
        var replacement = CreateHttpClient();

        try
        {
            var administrationToken = await GetClientCredentialsTokenAsync(
                anonymousClient,
                TrustedAutomationClientId,
                TrustedAutomationClientSecret,
                [
                    "clients.read",
                    "clients.create",
                    "clients.update",
                    "clients.delete"
                ]);
            replacement.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", administrationToken);
        }
        catch
        {
            replacement.Dispose();
            throw;
        }

        var previous = _administrationAutomationClient;
        _administrationAutomationClient = replacement;
        previous?.Dispose();
    }

    private async Task CleanupByPrefixAsync()
    {
        using var listResponse = await AdministrationAutomationClient.GetAsync(
            $"/api/admin/clients?skip=0&take=100&search={Uri.EscapeDataString(TestPrefix)}");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var body = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var item in body.GetProperty("items").EnumerateArray())
        {
            var clientId = item.GetProperty("clientId").GetString();
            var id = item.GetProperty("id").GetString();
            if (clientId?.StartsWith(TestPrefix, StringComparison.Ordinal) == true &&
                !string.IsNullOrWhiteSpace(id))
            {
                await DeleteAndVerifyMissingAsync(
                    new CreatedClient(id, clientId, clientId));
            }
        }
    }

    private async Task DeleteAndVerifyMissingAsync(CreatedClient created)
    {
        using var deleteResponse = await AdministrationAutomationClient.DeleteAsync(
            $"/api/admin/clients/{created.Id}");
        Assert.True(
            deleteResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound);

        using var getResponse = await AdministrationAutomationClient.GetAsync(
            $"/api/admin/clients/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    private async Task AssertNoDisposableClientsRemainAsync()
    {
        using var response = await AdministrationAutomationClient.GetAsync(
            $"/api/admin/clients?skip=0&take=100&search={Uri.EscapeDataString(TestPrefix)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var remaining = body.GetProperty("items")
            .EnumerateArray()
            .Count(item =>
                item.GetProperty("clientId").GetString()?
                    .StartsWith(TestPrefix, StringComparison.Ordinal) == true);
        Assert.Equal(0, remaining);
    }

    private static string[] Sorted(IEnumerable<string> values)
    {
        return values.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static string CreateSecret()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    private HttpClient AnonymousClient =>
        _anonymousClient ?? throw new InvalidOperationException("Test client is not initialized.");

    private HttpClient AdministrationAutomationClient =>
        _administrationAutomationClient ??
        throw new InvalidOperationException("Administration client is not initialized.");

    private HttpClient ApplicationManagerClient =>
        _applicationManagerClient ??
        throw new InvalidOperationException("ApplicationManager client is not initialized.");

    private HttpClient AdminPersonClient =>
        _adminPersonClient ?? throw new InvalidOperationException("Admin client is not initialized.");

    private sealed record CreatedClient(string Id, string ClientId, string DisplayName);

    private sealed record ForbiddenResponseShape(
        HttpStatusCode StatusCode,
        string? MediaType,
        string Body);

    private sealed record ClientState(
        string ClientId,
        string? DisplayName,
        string Type,
        string ApplicationType,
        string ConsentType,
        string[] RedirectUris,
        string[] PostLogoutRedirectUris,
        string[] Permissions,
        string[] SupportedRoles,
        bool RequirePkce,
        bool DisableExternalProviders,
        bool EnableTurnstile,
        string[] AllowedScopes,
        string[] RequiredScopes);
}
