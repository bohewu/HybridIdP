using Core.Application;
using Core.Application.DTOs;
using Infrastructure.Services;
using Moq;
using OpenIddict.Abstractions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tests.Application.UnitTests;

public class ClientServiceTests
{
    private readonly Mock<IOpenIddictApplicationManager> _mockApplicationManager;
    private readonly ClientService _clientService;

    public ClientServiceTests()
    {
        _mockApplicationManager = new Mock<IOpenIddictApplicationManager>();
        _clientService = new ClientService(_mockApplicationManager.Object);
    }

    #region GetClientsAsync Tests

    [Fact]
    public async Task GetClientsAsync_ShouldReturnAllClients_WhenNoFiltersApplied()
    {
        // Arrange
        var clients = new List<object>
        {
            new { Id = Guid.NewGuid() }
        };
        
        _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(clients));
        _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-client");
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test Client");
        _mockApplicationManager.Setup(m => m.GetClientTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientTypes.Confidential);
        _mockApplicationManager.Setup(m => m.GetApplicationTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplicationTypes.Web);
        _mockApplicationManager.Setup(m => m.GetConsentTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConsentTypes.Explicit);
        _mockApplicationManager.Setup(m => m.GetRedirectUrisAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act
        var (items, totalCount) = await _clientService.GetClientsAsync(0, 25, null, null, null);

        // Assert
        Assert.Single(items);
        Assert.Equal(1, totalCount);
        var client = items.First();
        Assert.Equal("test-client", client.ClientId);
    }

    [Fact]
    public async Task GetClientsAsync_ShouldFilterBySearch_WhenSearchProvided()
    {
        // Arrange
        var clients = new List<object>
        {
            new { Id = Guid.NewGuid() },
            new { Id = Guid.NewGuid() }
        };
        
        var clientIds = new Queue<string>(new[] { "matching-client", "other-client" });
        _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(clients));
        _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => clientIds.Dequeue());
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Display");
        _mockApplicationManager.Setup(m => m.GetClientTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientTypes.Public);
        _mockApplicationManager.Setup(m => m.GetApplicationTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplicationTypes.Web);
        _mockApplicationManager.Setup(m => m.GetConsentTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConsentTypes.Explicit);
        _mockApplicationManager.Setup(m => m.GetRedirectUrisAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act
        var (items, totalCount) = await _clientService.GetClientsAsync(0, 25, "matching", null, null);

        // Assert
        Assert.Single(items);
        Assert.Equal(1, totalCount);
        Assert.Contains(items, c => c.ClientId.Contains("matching", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetClientsAsync_ShouldFilterByType_WhenTypeProvided()
    {
        // Arrange
        var clients = new List<object>
        {
            new { Id = Guid.NewGuid() },
            new { Id = Guid.NewGuid() }
        };
        
        var types = new Queue<string>(new[] { ClientTypes.Confidential, ClientTypes.Public });
        _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(clients));
        _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("client");
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Display");
        _mockApplicationManager.Setup(m => m.GetClientTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => types.Dequeue());
        _mockApplicationManager.Setup(m => m.GetApplicationTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplicationTypes.Web);
        _mockApplicationManager.Setup(m => m.GetConsentTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConsentTypes.Explicit);
        _mockApplicationManager.Setup(m => m.GetRedirectUrisAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act
        var (items, totalCount) = await _clientService.GetClientsAsync(0, 25, null, ClientTypes.Confidential, null);

        // Assert
        Assert.Single(items);
        Assert.Equal(1, totalCount);
        Assert.All(items, c => Assert.Equal(ClientTypes.Confidential, c.Type));
    }

    [Fact]
    public async Task GetClientsAsync_ShouldApplyPagination_WhenSkipAndTakeProvided()
    {
        // Arrange
        var clients = Enumerable.Range(1, 30).Select(i => new { Id = Guid.NewGuid() }).ToList();
        
        var index = 0;
        _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(clients.Cast<object>()));
        _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"client{++index}");
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Display");
        _mockApplicationManager.Setup(m => m.GetClientTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientTypes.Public);
        _mockApplicationManager.Setup(m => m.GetApplicationTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplicationTypes.Web);
        _mockApplicationManager.Setup(m => m.GetConsentTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConsentTypes.Explicit);
        _mockApplicationManager.Setup(m => m.GetRedirectUrisAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act
        var (items, totalCount) = await _clientService.GetClientsAsync(10, 5, null, null, null);

        // Assert
        Assert.Equal(5, items.Count());
        Assert.Equal(30, totalCount);
    }

    [Fact]
    public async Task GetClientsAsync_ShouldSortByClientId_WhenSortParameterIsClientIdAsc()
    {
        // Arrange
        var clients = new List<object>
        {
            new { Id = Guid.NewGuid() },
            new { Id = Guid.NewGuid() }
        };
        
        var clientIds = new Queue<string>(new[] { "zeta-client", "alpha-client" });
        _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(clients));
        _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => clientIds.Dequeue());
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Display");
        _mockApplicationManager.Setup(m => m.GetClientTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientTypes.Public);
        _mockApplicationManager.Setup(m => m.GetApplicationTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplicationTypes.Web);
        _mockApplicationManager.Setup(m => m.GetConsentTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConsentTypes.Explicit);
        _mockApplicationManager.Setup(m => m.GetRedirectUrisAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act
        var (items, totalCount) = await _clientService.GetClientsAsync(0, 25, null, null, "clientId:asc");

        // Assert
        var itemList = items.ToList();
        Assert.Equal("alpha-client", itemList[0].ClientId);
        Assert.Equal("zeta-client", itemList[1].ClientId);
    }

        [Fact]
        public async Task GetClientsAsync_ShouldReturnEmpty_WhenNoClientsExist()
        {
            // Arrange
            var clients = new List<object>();
        
            _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .Returns(CreateAsyncEnumerable(clients));

            // Act
            var (items, totalCount) = await _clientService.GetClientsAsync(0, 25, null, null, null);

            // Assert
            Assert.Empty(items);
            Assert.Equal(0, totalCount);
        }

        [Fact]
        public async Task GetClientsAsync_ShouldReturnEmpty_WhenSkipExceedsTotalCount()
        {
            // Arrange
            var clients = new List<object>
            {
                new { Id = Guid.NewGuid() },
                new { Id = Guid.NewGuid() }
            };
        
            _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .Returns(CreateAsyncEnumerable(clients));
            _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Guid.NewGuid().ToString());
            _mockApplicationManager.Setup(m => m.GetClientIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("test-client");
            _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("Test Client");
            _mockApplicationManager.Setup(m => m.GetClientTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ClientTypes.Confidential);
            _mockApplicationManager.Setup(m => m.GetApplicationTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ApplicationTypes.Web);
            _mockApplicationManager.Setup(m => m.GetConsentTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ConsentTypes.Explicit);
            _mockApplicationManager.Setup(m => m.GetRedirectUrisAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ImmutableArray<string>.Empty);

            // Act
            var (items, totalCount) = await _clientService.GetClientsAsync(100, 25, null, null, null);

            // Assert
            Assert.Empty(items);
            Assert.Equal(2, totalCount); // Total count should still be correct
        }

    #endregion

    #region GetClientByIdAsync Tests

    [Fact]
    public async Task GetClientByIdAsync_ShouldReturnClient_WhenClientExists()
    {
        // Arrange
        var clientId = Guid.NewGuid();
        var client = new { Id = clientId };
        
        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
        _mockApplicationManager.Setup(m => m.GetIdAsync(client, It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientId.ToString());
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(client, It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-client");
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(client, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test Client");
        _mockApplicationManager.Setup(m => m.GetClientTypeAsync(client, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientTypes.Confidential);
        _mockApplicationManager.Setup(m => m.GetApplicationTypeAsync(client, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplicationTypes.Web);
        _mockApplicationManager.Setup(m => m.GetConsentTypeAsync(client, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConsentTypes.Explicit);
        _mockApplicationManager.Setup(m => m.GetRedirectUrisAsync(client, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray.Create("https://localhost:7001/signin-oidc"));
        _mockApplicationManager.Setup(m => m.GetPostLogoutRedirectUrisAsync(client, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray.Create("https://localhost:7001/signout-callback-oidc"));
        _mockApplicationManager.Setup(m => m.GetPermissionsAsync(client, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray.Create(Permissions.Endpoints.Authorization));

        // Act
        var result = await _clientService.GetClientByIdAsync(clientId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(clientId.ToString(), result.Id);
        Assert.Equal("test-client", result.ClientId);
        Assert.Single(result.RedirectUris);
        Assert.Single(result.PostLogoutRedirectUris);
        Assert.Single(result.Permissions);
    }

    [Fact]
    public async Task GetClientByIdAsync_ShouldReturnNull_WhenClientDoesNotExist()
    {
        // Arrange
        var clientId = Guid.NewGuid();
        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Act
        var result = await _clientService.GetClientByIdAsync(clientId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetClientsAsync_ShouldFallbackToClientIdDesc_WhenSortFieldUnknownWithDesc()
    {
        // Arrange
        var clients = new List<object>
        {
            new { Id = Guid.NewGuid(), Cid = "alpha-client" },
            new { Id = Guid.NewGuid(), Cid = "zeta-client" },
            new { Id = Guid.NewGuid(), Cid = "beta-client" }
        };

        var queue = new Queue<string>(new[] { "alpha-client", "zeta-client", "beta-client" });
        _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(clients));
        _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Guid.NewGuid().ToString());
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => queue.Dequeue());
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test");
        _mockApplicationManager.Setup(m => m.GetClientTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenIddictConstants.ClientTypes.Confidential);
        _mockApplicationManager.Setup(m => m.GetApplicationTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenIddictConstants.ApplicationTypes.Web);
        _mockApplicationManager.Setup(m => m.GetConsentTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenIddictConstants.ConsentTypes.Explicit);
        _mockApplicationManager.Setup(m => m.GetRedirectUrisAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act
        var (items, totalCount) = await _clientService.GetClientsAsync(0, 25, null, null, "unknown:desc");

        // Assert
        Assert.Equal(3, totalCount);
        var ordered = items.Select(i => i.ClientId).ToList();
        Assert.Equal(new[] { "zeta-client", "beta-client", "alpha-client" }, ordered);
    }

    [Fact]
    public async Task GetClientsAsync_ShouldFilterByType_WhenUnknownTypeProvided_ReturnsEmpty()
    {
        // Arrange
        var clients = new List<object>
        {
            new { Id = Guid.NewGuid() },
            new { Id = Guid.NewGuid() }
        };

        _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(clients));
        _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("some-client");
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Some");
        _mockApplicationManager.Setup(m => m.GetClientTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenIddictConstants.ClientTypes.Confidential);
        _mockApplicationManager.Setup(m => m.GetApplicationTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenIddictConstants.ApplicationTypes.Web);
        _mockApplicationManager.Setup(m => m.GetConsentTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenIddictConstants.ConsentTypes.Explicit);
        _mockApplicationManager.Setup(m => m.GetRedirectUrisAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act
        var (items, totalCount) = await _clientService.GetClientsAsync(0, 25, null, "unknown-type", null);

        // Assert
        Assert.Empty(items);
        Assert.Equal(0, totalCount);
    }

    #endregion

    #region CreateClientAsync Tests

    [Fact]
    public async Task CreateClientAsync_ShouldThrowArgumentException_WhenClientIdIsEmpty()
    {
        // Arrange
        // CreateClientRequest(ClientId, ClientSecret, DisplayName, ApplicationType, Type, ConsentType, RedirectUris, PostLogoutRedirectUris, Permissions)
        var request = new CreateClientRequest("", null, "Display Name", null, null, null, null, null, null);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _clientService.CreateClientAsync(request));
    }

    [Fact]
    public async Task CreateClientAsync_ShouldThrowInvalidOperationException_WhenClientAlreadyExists()
    {
        // Arrange
        // CreateClientRequest(ClientId, ClientSecret, DisplayName, ApplicationType, Type, ConsentType, RedirectUris, PostLogoutRedirectUris, Permissions)
        var request = new CreateClientRequest("existing-client", null, "Display", null, null, null, null, null, null);
        _mockApplicationManager.Setup(m => m.FindByClientIdAsync("existing-client", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new { Id = Guid.NewGuid() });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _clientService.CreateClientAsync(request));
    }

    [Fact]
    public async Task CreateClientAsync_ShouldGenerateSecret_WhenConfidentialClientWithoutSecret()
    {
        // Arrange
        // CreateClientRequest(ClientId, ClientSecret, DisplayName, ApplicationType, Type, ConsentType, RedirectUris, PostLogoutRedirectUris, Permissions)
        var request = new CreateClientRequest("test-client", null, "Test Client", null, ClientTypes.Confidential, null, null, null, null);
        var createdClient = new { Id = Guid.NewGuid() };
        
        _mockApplicationManager.Setup(m => m.FindByClientIdAsync("test-client", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);
        _mockApplicationManager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdClient);
        _mockApplicationManager.Setup(m => m.GetIdAsync(createdClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(createdClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-client");
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(createdClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test Client");

        // Act
        var result = await _clientService.CreateClientAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.ClientSecret);
        Assert.NotEmpty(result.ClientSecret);
    }

    [Fact]
    public async Task CreateClientAsync_ShouldThrowArgumentException_WhenPublicClientHasSecret()
    {
        // Arrange
        // CreateClientRequest(ClientId, ClientSecret, DisplayName, ApplicationType, Type, ConsentType, RedirectUris, PostLogoutRedirectUris, Permissions)
        var request = new CreateClientRequest("test-client", "some-secret", "Test Client", null, ClientTypes.Public, null, null, null, null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _clientService.CreateClientAsync(request));
        Assert.Contains("Public clients should not have a ClientSecret", ex.Message);
    }

    [Fact]
    public async Task CreateClientAsync_ShouldSetDefaultPermissions_WhenPermissionsNotProvided()
    {
        // Arrange
        // CreateClientRequest(ClientId, ClientSecret, DisplayName, ApplicationType, Type, ConsentType, RedirectUris, PostLogoutRedirectUris, Permissions)
        var request = new CreateClientRequest("test-client", null, "Test Client", ApplicationTypes.Web, ClientTypes.Public, ConsentTypes.Explicit, null, null, null);
        var createdClient = new { Id = Guid.NewGuid() };
        
        _mockApplicationManager.Setup(m => m.FindByClientIdAsync("test-client", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);
        _mockApplicationManager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, CancellationToken>((descriptor, _) =>
            {
                // Verify default permissions are set
                Assert.Contains(Permissions.Endpoints.Authorization, descriptor.Permissions);
                Assert.Contains(Permissions.Endpoints.Token, descriptor.Permissions);
                Assert.Contains(Permissions.GrantTypes.AuthorizationCode, descriptor.Permissions);
            })
            .ReturnsAsync(createdClient);
        _mockApplicationManager.Setup(m => m.GetIdAsync(createdClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(createdClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-client");
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(createdClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test Client");

        // Act
        var result = await _clientService.CreateClientAsync(request);

        // Assert
        Assert.NotNull(result);
        _mockApplicationManager.Verify(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region UpdateClientAsync Tests

    [Fact]
    public async Task UpdateClientAsync_ShouldThrowKeyNotFoundException_WhenClientDoesNotExist()
    {
        // Arrange
        var clientId = Guid.NewGuid();
        // UpdateClientRequest(ClientId, ClientSecret, DisplayName, Type, ConsentType, RedirectUris, PostLogoutRedirectUris, Permissions)
        var request = new UpdateClientRequest(null, null, "Updated", null, null, null, null, null);
        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _clientService.UpdateClientAsync(clientId, request));
    }

    [Fact]
    public async Task UpdateClientAsync_ShouldUpdateOnlyProvidedFields_WhenPartialUpdateRequested()
    {
        // Arrange
        var clientId = Guid.NewGuid();
        var client = new { Id = clientId };
        // UpdateClientRequest(ClientId, ClientSecret, DisplayName, Type, ConsentType, RedirectUris, PostLogoutRedirectUris, Permissions)
        var request = new UpdateClientRequest(null, null, "Updated Display", null, null, null, null, null);
        
        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
        _mockApplicationManager.Setup(m => m.PopulateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), client, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        _mockApplicationManager.Setup(m => m.PopulateAsync(client, It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        _mockApplicationManager.Setup(m => m.UpdateAsync(client, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        // Act
        await _clientService.UpdateClientAsync(clientId, request);

        // Assert
        _mockApplicationManager.Verify(m => m.UpdateAsync(client, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateClientAsync_ShouldSetClientTypeToConfidential_WhenSecretProvided()
    {
        // Arrange
        var clientId = Guid.NewGuid();
        var client = new { Id = clientId };
        // UpdateClientRequest(ClientId, ClientSecret, DisplayName, Type, ConsentType, RedirectUris, PostLogoutRedirectUris, Permissions)
        var request = new UpdateClientRequest(null, "new-secret", null, null, null, null, null, null);
        
        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
        _mockApplicationManager.Setup(m => m.PopulateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), client, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        _mockApplicationManager.Setup(m => m.PopulateAsync(client, It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<object, OpenIddictApplicationDescriptor, CancellationToken>((_, descriptor, _) =>
            {
                Assert.Equal(ClientTypes.Confidential, descriptor.ClientType);
                Assert.Equal("new-secret", descriptor.ClientSecret);
            })
            .Returns(ValueTask.CompletedTask);
        _mockApplicationManager.Setup(m => m.UpdateAsync(client, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        // Act
        await _clientService.UpdateClientAsync(clientId, request);

        // Assert
        _mockApplicationManager.Verify(m => m.UpdateAsync(client, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DeleteClientAsync Tests

    [Fact]
    public async Task DeleteClientAsync_ShouldThrowKeyNotFoundException_WhenClientDoesNotExist()
    {
        // Arrange
        var clientId = Guid.NewGuid();
        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _clientService.DeleteClientAsync(clientId));
    }

    [Fact]
    public async Task DeleteClientAsync_ShouldDeleteClient_WhenClientExists()
    {
        // Arrange
        var clientId = Guid.NewGuid();
        var client = new { Id = clientId };
        
        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
        _mockApplicationManager.Setup(m => m.DeleteAsync(client, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        // Act
        await _clientService.DeleteClientAsync(clientId);

        // Assert
        _mockApplicationManager.Verify(m => m.DeleteAsync(client, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region RegenerateSecretAsync Tests

    [Fact]
    public async Task RegenerateSecretAsync_ShouldThrowKeyNotFoundException_WhenClientDoesNotExist()
    {
        // Arrange
        var clientId = Guid.NewGuid();
        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _clientService.RegenerateSecretAsync(clientId));
    }

    [Fact]
    public async Task RegenerateSecretAsync_ShouldThrowInvalidOperationException_WhenClientIsPublic()
    {
        // Arrange
        var clientId = Guid.NewGuid();
        var client = new { Id = clientId };
        
        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
        _mockApplicationManager.Setup(m => m.GetClientTypeAsync(client, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientTypes.Public);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _clientService.RegenerateSecretAsync(clientId));
        Assert.Contains("only available for confidential clients", ex.Message);
    }

    [Fact]
    public async Task RegenerateSecretAsync_ShouldGenerateNewSecret_WhenClientIsConfidential()
    {
        // Arrange
        var clientId = Guid.NewGuid();
        var client = new { Id = clientId };
        
        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
        _mockApplicationManager.Setup(m => m.GetClientTypeAsync(client, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientTypes.Confidential);
        _mockApplicationManager.Setup(m => m.PopulateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), client, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        _mockApplicationManager.Setup(m => m.PopulateAsync(client, It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        _mockApplicationManager.Setup(m => m.UpdateAsync(client, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        // Act
        var newSecret = await _clientService.RegenerateSecretAsync(clientId);

        // Assert
        Assert.NotNull(newSecret);
        Assert.NotEmpty(newSecret);
        _mockApplicationManager.Verify(m => m.UpdateAsync(client, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Helper Methods

    private static IAsyncEnumerable<T> CreateAsyncEnumerable<T>(IEnumerable<T> source)
    {
        return new AsyncEnumerable<T>(source);
    }

    private class AsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        private readonly IEnumerable<T> _source;

        public AsyncEnumerable(IEnumerable<T> source)
        {
            _source = source;
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new AsyncEnumerator<T>(_source.GetEnumerator());
        }
    }

    private class AsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        private readonly IEnumerator<T> _enumerator;

        public AsyncEnumerator(IEnumerator<T> enumerator)
        {
            _enumerator = enumerator;
        }

        public T Current => _enumerator.Current;

        public ValueTask DisposeAsync()
        {
            _enumerator.Dispose();
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> MoveNextAsync()
        {
            return ValueTask.FromResult(_enumerator.MoveNext());
        }
    }

    #endregion
}
