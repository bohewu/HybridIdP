using System;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Entities;
using Core.Domain.Events;
using Infrastructure.Services;
using Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;
using System.Text.Json;
using System.Collections.Immutable;
using AuthConstants = Core.Domain.Constants.AuthConstants;

namespace Tests.Infrastructure.UnitTests;

public class ClientServiceTests
{
    private readonly Mock<IOpenIddictApplicationManager> _mockApplicationManager;
    private readonly Mock<IDomainEventPublisher> _mockEventPublisher;
    private readonly Mock<IApplicationDbContext> _mockContext;
    private readonly Mock<IOpenIddictScopeManager> _mockScopeManager;
    private readonly ClientService _service;

    public ClientServiceTests()
    {
        _mockApplicationManager = new Mock<IOpenIddictApplicationManager>();
        _mockEventPublisher = new Mock<IDomainEventPublisher>();
        _mockContext = new Mock<IApplicationDbContext>();
        _mockScopeManager = new Mock<IOpenIddictScopeManager>();

        _service = new ClientService(
            _mockApplicationManager.Object,
            _mockEventPublisher.Object,
            _mockContext.Object,
            _mockScopeManager.Object,
            Options.Create(new RedirectUriSecurityPolicyOptions())
        );
    }

    [Fact]
    public async Task CreateClient_WithNativeApp_ShouldEnforcePublicClientType()
    {
        // Arrange
        var request = new CreateClientRequest(
            ClientId: "test-native",
            ApplicationType: ApplicationTypes.Native,
            ClientSecret: null, // No secret => Public
            DisplayName: null,
            Type: null,
            ConsentType: null,

            RedirectUris: new List<string> { "https://dummy" },
            PostLogoutRedirectUris: null,
            Permissions: null,
            SupportedRoles: null
        );

        // Mock FindByClientIdAsync to return null (client doesn't exist)
        _mockApplicationManager.Setup(m => m.FindByClientIdAsync(request.ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object)null);

        // Mock CreateAsync to return a dummy application
        var dummyApp = new object();
        _mockApplicationManager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dummyApp);
        _mockApplicationManager.Setup(m => m.GetIdAsync(dummyApp, It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-id");

        // Act
        var result = await _service.CreateClientAsync(request);

        // Assert
        Assert.NotNull(result);
        _mockApplicationManager.Verify(m => m.CreateAsync(
            It.Is<OpenIddictApplicationDescriptor>(d => 
                d.ApplicationType == ApplicationTypes.Native && 
                d.ClientType == ClientTypes.Public), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateClient_WithNativeAppAndSecret_ShouldThrowException()
    {
        // Arrange
        var request = new CreateClientRequest(
            ClientId: "test-native-bad",
            ApplicationType: ApplicationTypes.Native,
            ClientSecret: "some-secret", // Secret implies Confidential, which is invalid for Native
            DisplayName: null,
            Type: null,
            ConsentType: null,
            RedirectUris: new List<string> { "https://dummy" },
            PostLogoutRedirectUris: null,
            Permissions: null,
            SupportedRoles: null
        );

        _mockApplicationManager.Setup(m => m.FindByClientIdAsync(request.ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object)null);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateClientAsync(request));
    }

    [Fact]
    public async Task CreateClient_WithNativeAppAndConfidentialType_ShouldThrowException()
    {
        // Arrange
        var request = new CreateClientRequest(
            ClientId: "test-native-bad-type",
            ApplicationType: ApplicationTypes.Native,
            Type: ClientTypes.Confidential,
            ClientSecret: "secret",
            DisplayName: null,
            ConsentType: null,
            RedirectUris: new List<string> { "https://dummy" },
            PostLogoutRedirectUris: null,
            Permissions: null,
            SupportedRoles: null
        );

        _mockApplicationManager.Setup(m => m.FindByClientIdAsync(request.ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object)null);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateClientAsync(request));
    }

    [Fact]
    public async Task UpdateClient_WithExistingNativeApp_AndAddingSecret_ShouldThrowException()
    {
        // Arrange
        var clientId = Guid.NewGuid();
        var existingApp = new object();

        // Setup existing as Native/Public initially
        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingApp);
        
        // Mock PopulateAsync to simulate fetching existing data into descriptor
        _mockApplicationManager.Setup(m => m.PopulateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), existingApp, It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, object, CancellationToken>((d, a, c) => {
                d.ApplicationType = ApplicationTypes.Native;
                d.ClientType = ClientTypes.Public;
                // No secret initially
            })
            .Returns(default(ValueTask));

        // Request to add a secret (which implies Confidential)
        var request = new UpdateClientRequest(
            ClientId: "test-native",
            ClientSecret: "new-secret", // This triggers the validation failure
            DisplayName: null,
            Type: null,
            ConsentType: null,
            RedirectUris: null,
            PostLogoutRedirectUris: null,
            Permissions: null,
            SupportedRoles: null
        );

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateClientAsync(clientId, request));
    }
    [Fact]
    public async Task CreateClient_Interactive_WithoutRedirectUri_ShouldThrowException()
    {
        // Arrange
        // Defaults to Auth Code flow if Permissions is null
        var request = new CreateClientRequest(
            ClientId: "test-auth-code-no-redirect",
            ApplicationType: ApplicationTypes.Web,
            Type: ClientTypes.Confidential,
            ClientSecret: "secret",
            DisplayName: null,

            ConsentType: null,
            RedirectUris: null, // Missing!
            PostLogoutRedirectUris: null,
            Permissions: null, // Defaults to Auth Code
            SupportedRoles: null
        );

        _mockApplicationManager.Setup(m => m.FindByClientIdAsync(request.ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object)null);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateClientAsync(request));
    }

    [Fact]
    public async Task CreateClient_WithHttpRedirect_NonLocalhost_ShouldBeBlockedWhenHttpsEnforced()
    {
        var request = new CreateClientRequest(
            ClientId: "http-non-localhost",
            ClientSecret: "secret",
            DisplayName: null,
            ApplicationType: ApplicationTypes.Web,
            Type: ClientTypes.Confidential,
            ConsentType: null,
            RedirectUris: new List<string> { "http://example.com/callback" },
            PostLogoutRedirectUris: null,
            Permissions: null,
            SupportedRoles: null
        );

        _mockApplicationManager.Setup(m => m.FindByClientIdAsync(request.ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateClientAsync(request));

        Assert.Contains("HTTP redirect URI", ex.Message);
    }

    [Fact]
    public async Task CreateClient_WithHttpLocalhostRedirect_ShouldBeAllowedWhenConfigured()
    {
        var request = new CreateClientRequest(
            ClientId: "http-localhost",
            ClientSecret: "secret",
            DisplayName: null,
            ApplicationType: ApplicationTypes.Web,
            Type: ClientTypes.Confidential,
            ConsentType: null,
            RedirectUris: new List<string> { "http://localhost/callback" },
            PostLogoutRedirectUris: null,
            Permissions: null,
            SupportedRoles: null
        );

        _mockApplicationManager.Setup(m => m.FindByClientIdAsync(request.ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        var created = new object();
        _mockApplicationManager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);
        _mockApplicationManager.Setup(m => m.GetIdAsync(created, It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-id");

        var result = await _service.CreateClientAsync(request);

        Assert.NotNull(result);
        _mockApplicationManager.Verify(m => m.CreateAsync(
            It.Is<OpenIddictApplicationDescriptor>(d => d.RedirectUris.Any(u => u.ToString() == "http://localhost/callback")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateClient_WithHostRestriction_ShouldBlockRedirectToUnlistedHost()
    {
        var serviceWithHostPolicy = new ClientService(
            _mockApplicationManager.Object,
            _mockEventPublisher.Object,
            _mockContext.Object,
            _mockScopeManager.Object,
            Options.Create(new RedirectUriSecurityPolicyOptions
            {
                EnforceHttps = true,
                AllowLocalhostHttp = true,
                AllowedHosts = ["trusted.example"]
            }));

        var request = new CreateClientRequest(
            ClientId: "host-restricted",
            ClientSecret: "secret",
            DisplayName: null,
            ApplicationType: ApplicationTypes.Web,
            Type: ClientTypes.Confidential,
            ConsentType: null,
            RedirectUris: new List<string> { "https://evil.example/callback" },
            PostLogoutRedirectUris: null,
            Permissions: null,
            SupportedRoles: null
        );

        _mockApplicationManager.Setup(m => m.FindByClientIdAsync(request.ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => serviceWithHostPolicy.CreateClientAsync(request));

        Assert.Contains("not allowed", ex.Message);
    }

    [Fact]
    public async Task CreateAndUpdate_InvalidRedirectUri_ShouldUseConsistentValidationBehavior()
    {
        var createRequest = new CreateClientRequest(
            ClientId: "create-invalid-uri",
            ClientSecret: "secret",
            DisplayName: null,
            ApplicationType: ApplicationTypes.Web,
            Type: ClientTypes.Confidential,
            ConsentType: null,
            RedirectUris: new List<string> { "not-a-uri" },
            PostLogoutRedirectUris: null,
            Permissions: null,
            SupportedRoles: null
        );

        _mockApplicationManager.Setup(m => m.FindByClientIdAsync(createRequest.ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        var createException = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateClientAsync(createRequest));
        Assert.Contains("RedirectUris", createException.Message);

        var clientId = Guid.NewGuid();
        var existingApp = new object();
        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingApp);
        _mockApplicationManager.Setup(m => m.PopulateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), existingApp, It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, object, CancellationToken>((d, _, _) =>
            {
                d.ClientId = "existing";
                d.ApplicationType = ApplicationTypes.Web;
                d.ClientType = ClientTypes.Confidential;
                d.Permissions.Add(Permissions.Endpoints.Token);
                d.Permissions.Add(Permissions.GrantTypes.ClientCredentials);
            })
            .Returns(default(ValueTask));

        var updateRequest = new UpdateClientRequest(
            ClientId: null,
            ClientSecret: null,
            DisplayName: null,
            Type: null,
            ConsentType: null,
            RedirectUris: new List<string> { "not-a-uri" },
            PostLogoutRedirectUris: null,
            Permissions: null,
            SupportedRoles: null
        );

        var updateException = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateClientAsync(clientId, updateRequest));
        Assert.Contains("RedirectUris", updateException.Message);
    }

    [Fact]
    public async Task CreateClient_WithSupportedRoles_ShouldPersistProperty()
    {
        // Arrange
        var roles = new List<string> { "Admin", "User" };
        var request = new CreateClientRequest(
            ClientId: "test-roles",
            ClientSecret: "secret",
            DisplayName: "Test Roles",
            ApplicationType: ApplicationTypes.Web,
            Type: ClientTypes.Confidential,
            ConsentType: ConsentTypes.Explicit,
            RedirectUris: new List<string> { "https://localhost" },
            PostLogoutRedirectUris: null,
            Permissions: null,
            SupportedRoles: roles
        );

        _mockApplicationManager.Setup(m => m.FindByClientIdAsync(request.ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object)null);
        
        var dummyApp = new object();
        _mockApplicationManager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dummyApp);
        _mockApplicationManager.Setup(m => m.GetIdAsync(dummyApp, It.IsAny<CancellationToken>()))
             .ReturnsAsync("new-id");

        // Act
        await _service.CreateClientAsync(request);

        // Assert
        _mockApplicationManager.Verify(m => m.CreateAsync(
            It.Is<OpenIddictApplicationDescriptor>(d => 
                d.Properties.ContainsKey(AuthConstants.Properties.SupportedRoles) &&
                d.Properties[AuthConstants.Properties.SupportedRoles].ToString().Contains("Admin")
            ), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateClient_WithEnableTurnstileTrue_ShouldPersistProperty()
    {
        var request = new CreateClientRequest(
            ClientId: "test-turnstile",
            ClientSecret: "secret",
            DisplayName: "Test Turnstile",
            ApplicationType: ApplicationTypes.Web,
            Type: ClientTypes.Confidential,
            ConsentType: ConsentTypes.Explicit,
            RedirectUris: new List<string> { "https://localhost" },
            PostLogoutRedirectUris: null,
            Permissions: null,
            SupportedRoles: null)
        {
            EnableTurnstile = true
        };

        _mockApplicationManager.Setup(m => m.FindByClientIdAsync(request.ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object)null);

        var dummyApp = new object();
        _mockApplicationManager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dummyApp);
        _mockApplicationManager.Setup(m => m.GetIdAsync(dummyApp, It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-id");

        await _service.CreateClientAsync(request);

        _mockApplicationManager.Verify(m => m.CreateAsync(
            It.Is<OpenIddictApplicationDescriptor>(d =>
                d.Properties.ContainsKey(AuthConstants.Properties.EnableTurnstile) &&
                d.Properties[AuthConstants.Properties.EnableTurnstile].ValueKind == JsonValueKind.True),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetClientById_WhenEnableTurnstilePropertyMissing_ShouldReturnFalse()
    {
        var id = Guid.NewGuid();
        var app = new object();

        _mockApplicationManager.Setup(m => m.FindByIdAsync(id.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(app);
        _mockApplicationManager.Setup(m => m.GetRedirectUrisAsync(app, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ImmutableArray<string>>(ImmutableArray<string>.Empty));
        _mockApplicationManager.Setup(m => m.GetPostLogoutRedirectUrisAsync(app, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ImmutableArray<string>>(ImmutableArray<string>.Empty));
        _mockApplicationManager.Setup(m => m.GetPermissionsAsync(app, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ImmutableArray<string>>(ImmutableArray<string>.Empty));
        _mockApplicationManager.Setup(m => m.PopulateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), app, It.IsAny<CancellationToken>()))
            .Returns(default(ValueTask));
        _mockApplicationManager.Setup(m => m.GetClientTypeAsync(app, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientTypes.Public);
        _mockApplicationManager.Setup(m => m.GetPropertiesAsync(app, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableDictionary<string, JsonElement>.Empty);
        _mockApplicationManager.Setup(m => m.GetIdAsync(app, It.IsAny<CancellationToken>()))
            .ReturnsAsync(id.ToString());
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(app, It.IsAny<CancellationToken>()))
            .ReturnsAsync("client-a");
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(app, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Client A");
        _mockApplicationManager.Setup(m => m.GetConsentTypeAsync(app, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConsentTypes.Explicit);
        _mockApplicationManager.Setup(m => m.GetApplicationTypeAsync(app, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplicationTypes.Web);

        var result = await _service.GetClientByIdAsync(id);

        Assert.NotNull(result);
        Assert.False(result.EnableTurnstile);
    }

    [Fact]
    public async Task UpdateClient_WithEnableTurnstileFalse_ShouldRemoveProperty()
    {
        var clientId = Guid.NewGuid();
        var app = new object();

        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(app);

        _mockApplicationManager.Setup(m => m.PopulateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), app, It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, object, CancellationToken>((d, _, _) =>
            {
                d.ClientId = "client-b";
                d.ApplicationType = ApplicationTypes.Web;
                d.ClientType = ClientTypes.Confidential;
                d.Permissions.Add(Permissions.Endpoints.Token);
                d.Permissions.Add(Permissions.GrantTypes.ClientCredentials);
                d.Properties[AuthConstants.Properties.EnableTurnstile] = JsonSerializer.SerializeToElement(true);
            })
            .Returns(default(ValueTask));

        _mockApplicationManager.Setup(m => m.UpdateAsync(
                app,
                It.IsAny<OpenIddictApplicationDescriptor>(),
                It.IsAny<CancellationToken>()))
            .Callback<object, OpenIddictApplicationDescriptor, CancellationToken>((_, d, _) =>
            {
                Assert.False(d.Properties.ContainsKey(AuthConstants.Properties.EnableTurnstile));
            })
            .Returns(default(ValueTask));

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
            EnableTurnstile = false
        };

        await _service.UpdateClientAsync(clientId, request);

        _mockApplicationManager.Verify(m => m.UpdateAsync(
            app,
            It.IsAny<OpenIddictApplicationDescriptor>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockApplicationManager.Verify(m => m.UpdateAsync(app, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateClient_PublicClient_ShouldAlwaysRequirePkce()
    {
        var request = new CreateClientRequest(
            ClientId: "pkce-public",
            ClientSecret: null,
            DisplayName: "Public PKCE",
            ApplicationType: ApplicationTypes.Web,
            Type: ClientTypes.Public,
            ConsentType: ConsentTypes.Explicit,
            RedirectUris: new List<string> { "https://localhost/callback" },
            PostLogoutRedirectUris: null,
            Permissions: null,
            SupportedRoles: null)
        {
            RequirePkce = false
        };

        _mockApplicationManager.Setup(m => m.FindByClientIdAsync(request.ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object)null);

        var dummyApp = new object();
        _mockApplicationManager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dummyApp);
        _mockApplicationManager.Setup(m => m.GetIdAsync(dummyApp, It.IsAny<CancellationToken>()))
             .ReturnsAsync("new-id");

        await _service.CreateClientAsync(request);

        _mockApplicationManager.Verify(m => m.CreateAsync(
            It.Is<OpenIddictApplicationDescriptor>(d =>
                d.Requirements.Contains(Requirements.Features.ProofKeyForCodeExchange)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateClient_ConfidentialClient_ShouldAllowDisablingPkce()
    {
        var request = new CreateClientRequest(
            ClientId: "pkce-confidential",
            ClientSecret: "secret",
            DisplayName: "Conf PKCE",
            ApplicationType: ApplicationTypes.Web,
            Type: ClientTypes.Confidential,
            ConsentType: ConsentTypes.Explicit,
            RedirectUris: null,
            PostLogoutRedirectUris: null,
            Permissions: new List<string> { Permissions.Endpoints.Token, Permissions.GrantTypes.ClientCredentials },
            SupportedRoles: null)
        {
            RequirePkce = false
        };

        _mockApplicationManager.Setup(m => m.FindByClientIdAsync(request.ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object)null);

        var dummyApp = new object();
        _mockApplicationManager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dummyApp);
        _mockApplicationManager.Setup(m => m.GetIdAsync(dummyApp, It.IsAny<CancellationToken>()))
             .ReturnsAsync("new-id");

        await _service.CreateClientAsync(request);

        _mockApplicationManager.Verify(m => m.CreateAsync(
            It.Is<OpenIddictApplicationDescriptor>(d =>
                !d.Requirements.Contains(Requirements.Features.ProofKeyForCodeExchange)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateClient_ConfidentialClient_ShouldRemovePkceRequirement_WhenDisabled()
    {
        var clientId = Guid.NewGuid();
        var existingApp = new object();

        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingApp);

        _mockApplicationManager.Setup(m => m.PopulateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), existingApp, It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, object, CancellationToken>((d, _, _) =>
            {
                d.ClientId = "existing-client";
                d.ClientType = ClientTypes.Confidential;
                d.ApplicationType = ApplicationTypes.Web;
                d.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
                d.Permissions.Add(Permissions.Endpoints.Token);
                d.Permissions.Add(Permissions.GrantTypes.ClientCredentials);
            })
            .Returns(default(ValueTask));

        _mockApplicationManager.Setup(m => m.UpdateAsync(
                existingApp,
                It.IsAny<OpenIddictApplicationDescriptor>(),
                It.IsAny<CancellationToken>()))
            .Callback<object, OpenIddictApplicationDescriptor, CancellationToken>((_, d, _) =>
            {
                Assert.DoesNotContain(Requirements.Features.ProofKeyForCodeExchange, d.Requirements);
            })
            .Returns(default(ValueTask));

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
            RequirePkce = false
        };

        await _service.UpdateClientAsync(clientId, request);

        _mockApplicationManager.Verify(m => m.UpdateAsync(
            existingApp,
            It.IsAny<OpenIddictApplicationDescriptor>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockApplicationManager.Verify(m => m.UpdateAsync(existingApp, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteClient_ShouldRemoveMatchingOwnershipRows()
    {
        // Arrange
        var clientKey = Guid.NewGuid();
        var clientId = "deleted-client";
        var application = new object();

        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientKey.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientId);

        var ownerships = new List<ClientOwnership>
        {
            new() { ClientId = clientId, CreatedByPersonId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow },
            new() { ClientId = "other-client", CreatedByPersonId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow }
        };

        var ownershipSet = CreateMockDbSet(ownerships);
        _mockContext.Setup(c => c.ClientOwnerships).Returns(ownershipSet.Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // Act
        await _service.DeleteClientAsync(clientKey);

        // Assert
        ownershipSet.Verify(s => s.RemoveRange(It.Is<IEnumerable<ClientOwnership>>(items =>
            items.Any(co => co.ClientId == clientId) &&
            items.All(co => co.ClientId == clientId))), Times.Once);
        _mockContext.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockApplicationManager.Verify(m => m.DeleteAsync(application, It.IsAny<CancellationToken>()), Times.Once);
        _mockEventPublisher.Verify(e => e.PublishAsync(It.IsAny<ClientDeletedEvent>()), Times.Once);
    }

    private static Mock<DbSet<T>> CreateMockDbSet<T>(List<T> sourceList) where T : class
    {
        var queryable = sourceList.AsQueryable();
        var mockDbSet = new Mock<DbSet<T>>();

        mockDbSet.As<IEnumerable<T>>()
            .Setup(m => m.GetEnumerator())
            .Returns(() => sourceList.GetEnumerator());

        mockDbSet.As<IQueryable<T>>()
            .Setup(m => m.Provider)
            .Returns(queryable.Provider);

        mockDbSet.As<IQueryable<T>>()
            .Setup(m => m.Expression)
            .Returns(queryable.Expression);

        mockDbSet.As<IQueryable<T>>()
            .Setup(m => m.ElementType)
            .Returns(queryable.ElementType);

        mockDbSet.As<IQueryable<T>>()
            .Setup(m => m.GetEnumerator())
            .Returns(() => queryable.GetEnumerator());

        return mockDbSet;
    }
}
