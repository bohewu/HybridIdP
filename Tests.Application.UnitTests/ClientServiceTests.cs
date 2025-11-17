using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Events;
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
    private readonly Mock<IDomainEventPublisher> _mockEventPublisher;
    private readonly ClientService _clientService;

    public ClientServiceTests()
    {
        _mockApplicationManager = new Mock<IOpenIddictApplicationManager>();
        _mockEventPublisher = new Mock<IDomainEventPublisher>();
        _clientService = new ClientService(_mockApplicationManager.Object, _mockEventPublisher.Object);
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
    public async Task GetClientsAsync_SearchShouldBeCaseInsensitive_AcrossClientIdAndDisplayName()
    {
        // Arrange shared list of applications
        var clients = new List<object>
        {
            new { Id = Guid.NewGuid() },
            new { Id = Guid.NewGuid() },
            new { Id = Guid.NewGuid() }
        };

        _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(clients));
        _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Guid.NewGuid().ToString());
        _mockApplicationManager.Setup(m => m.GetClientTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientTypes.Public);
        _mockApplicationManager.Setup(m => m.GetApplicationTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplicationTypes.Web);
        _mockApplicationManager.Setup(m => m.GetConsentTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConsentTypes.Explicit);
        _mockApplicationManager.Setup(m => m.GetRedirectUrisAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Phase 1: search by clientId (alpha-CLIENT)
        var ids1 = new Queue<string>(new[] { "alpha-CLIENT", "bravo", "charlie" });
        var names1 = new Queue<string>(new[] { "Display One", "TeSt Name", "Other" });
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ids1.Dequeue());
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => names1.Dequeue());

        var (items1, _) = await _clientService.GetClientsAsync(0, 25, "client", null, null);
        Assert.Single(items1);
        Assert.Contains("alpha-CLIENT", items1.Select(i => i.ClientId));

        // Phase 2: re-setup for displayName search (TeSt Name)
        var ids2 = new Queue<string>(new[] { "alpha-CLIENT", "bravo", "charlie" });
        var names2 = new Queue<string>(new[] { "Display One", "TeSt Name", "Other" });
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ids2.Dequeue());
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => names2.Dequeue());

        var (items2, _) = await _clientService.GetClientsAsync(0, 25, "test", null, null);
        Assert.Single(items2);
        Assert.Contains("TeSt Name", items2.Select(i => i.DisplayName));
    }

    [Fact]
    public async Task GetClientsAsync_ShouldSortByDisplayNameAsc()
    {
        // Arrange
        var clients = new List<object>
        {
            new { Id = Guid.NewGuid() },
            new { Id = Guid.NewGuid() },
            new { Id = Guid.NewGuid() }
        };

    var displayNames = new Queue<string>(new[] { "zeta app", "alpha app", "bravo app" });
        var clientIds = new Queue<string>(new[] { "c1", "c2", "c3" });

        _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(clients));
        _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Guid.NewGuid().ToString());
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => clientIds.Dequeue());
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => displayNames.Dequeue());
        _mockApplicationManager.Setup(m => m.GetClientTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientTypes.Public);
        _mockApplicationManager.Setup(m => m.GetApplicationTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplicationTypes.Web);
        _mockApplicationManager.Setup(m => m.GetConsentTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConsentTypes.Explicit);
        _mockApplicationManager.Setup(m => m.GetRedirectUrisAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act
        var (items, totalCount) = await _clientService.GetClientsAsync(0, 25, null, null, "displayName:asc");

        // Assert
        Assert.Equal(3, totalCount);
        var ordered = items.Select(i => i.DisplayName).ToList();
        Assert.Equal(new[] { "alpha app", "bravo app", "zeta app" }, ordered);
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

    [Fact]
    public async Task GetClientsAsync_ShouldSortByDisplayNameDesc()
    {
        // Arrange
        var clients = new List<object>
        {
            new { Id = Guid.NewGuid() },
            new { Id = Guid.NewGuid() },
            new { Id = Guid.NewGuid() }
        };

        var displayNames = new Queue<string>(new[] { "Alpha", "bravo", "Zulu" }); // initial unsorted sequence
        var clientIds = new Queue<string>(new[] { "c1", "c2", "c3" });

        _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(clients));
        _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Guid.NewGuid().ToString());
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => clientIds.Dequeue());
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => displayNames.Dequeue());
        _mockApplicationManager.Setup(m => m.GetClientTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientTypes.Public);
        _mockApplicationManager.Setup(m => m.GetApplicationTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplicationTypes.Web);
        _mockApplicationManager.Setup(m => m.GetConsentTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConsentTypes.Explicit);
        _mockApplicationManager.Setup(m => m.GetRedirectUrisAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act
        var (items, total) = await _clientService.GetClientsAsync(0, 25, null, null, "displayName:desc");

        // Assert
        Assert.Equal(3, total);
        var ordered = items.Select(i => i.DisplayName).ToList();
        Assert.Equal(new[] { "Zulu", "bravo", "Alpha" }, ordered); // case-insensitive descending
    }

    [Fact]
    public async Task GetClientsAsync_ShouldSortByClientIdDesc()
    {
        // Arrange
        var clients = new List<object>
        {
            new { Id = Guid.NewGuid() },
            new { Id = Guid.NewGuid() },
            new { Id = Guid.NewGuid() }
        };

        var clientIds = new Queue<string>(new[] { "client-a", "client-z", "client-m" });

        _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(clients));
        _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Guid.NewGuid().ToString());
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
        var (items, _) = await _clientService.GetClientsAsync(0, 10, null, null, "clientId:desc");

        // Assert
        var list = items.Select(i => i.ClientId).ToList();
        Assert.Equal(new[] { "client-z", "client-m", "client-a" }, list);
    }

    [Fact]
    public async Task GetClientsAsync_ShouldSortByRedirectUrisCountAsc()
    {
        // Arrange
        var clients = new List<object>
        {
            new { Id = Guid.NewGuid() }, // 2 uris
            new { Id = Guid.NewGuid() }, // 0 uris
            new { Id = Guid.NewGuid() }  // 5 uris
        };

        var redirectUrisQueue = new Queue<ImmutableArray<string>>(
            new[]
            {
                ImmutableArray.Create("https://a", "https://b"),
                ImmutableArray<string>.Empty,
                ImmutableArray.Create("https://1","https://2","https://3","https://4","https://5")
            });
        var clientIds = new Queue<string>(new[] { "cid1", "cid2", "cid3" });

        _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(clients));
        _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Guid.NewGuid().ToString());
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
            .ReturnsAsync(() => redirectUrisQueue.Dequeue());

        // Act
        var (items, total) = await _clientService.GetClientsAsync(0, 25, null, null, "redirectUrisCnt:asc");

        // Assert
        Assert.Equal(3, total);
        var counts = items.Select(i => i.RedirectUrisCount).ToList();
        Assert.Equal(new[] { 0, 2, 5 }, counts);
    }

    [Fact]
    public async Task GetClientsAsync_ShouldApplyDefaultPaging_WhenTakeNonPositiveOrSkipNegative()
    {
        // Arrange
        var clients = Enumerable.Range(1, 40).Select(_ => new { Id = Guid.NewGuid() }).ToList();
        var idx = 0;
        _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(clients));
        _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Guid.NewGuid().ToString());
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"client{++idx:000}");
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

        // Act: skip negative, take <= 0 should default to 25 & skip fixed to 0
        var (items, total) = await _clientService.GetClientsAsync(-5, 0, null, null, null);

        // Assert
        Assert.Equal(40, total);
        Assert.Equal(25, items.Count());
        Assert.Equal("client001", items.First().ClientId);
    }

    [Fact]
    public async Task GetClientsAsync_ShouldReturnAllClients_WhenSearchIsWhitespace()
    {
        // Arrange
        var clients = new List<object>
        {
            new { Id = Guid.NewGuid() },
            new { Id = Guid.NewGuid() }
        };
        var ids = new Queue<string>(new[] { "id1", "id2" });

        _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(clients));
        _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Guid.NewGuid().ToString());
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ids.Dequeue());
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Name");
        _mockApplicationManager.Setup(m => m.GetClientTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientTypes.Public);
        _mockApplicationManager.Setup(m => m.GetApplicationTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplicationTypes.Web);
        _mockApplicationManager.Setup(m => m.GetConsentTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConsentTypes.Explicit);
        _mockApplicationManager.Setup(m => m.GetRedirectUrisAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act
        var (items, total) = await _clientService.GetClientsAsync(0, 10, "   ", null, null);

        // Assert
        Assert.Equal(2, total);
        Assert.Equal(2, items.Count());
    }

    [Fact]
    public async Task GetClientsAsync_ShouldFallbackToClientIdAsc_WhenUnknownSortFieldAsc()
    {
        // Arrange
        var clients = new List<object>
        {
            new { Id = Guid.NewGuid() },
            new { Id = Guid.NewGuid() },
            new { Id = Guid.NewGuid() }
        };
        var clientIds = new Queue<string>(new[] { "zeta", "alpha", "beta" });

        _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(clients));
        _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Guid.NewGuid().ToString());
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => clientIds.Dequeue());
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Name");
        _mockApplicationManager.Setup(m => m.GetClientTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientTypes.Public);
        _mockApplicationManager.Setup(m => m.GetApplicationTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplicationTypes.Web);
        _mockApplicationManager.Setup(m => m.GetConsentTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConsentTypes.Explicit);
        _mockApplicationManager.Setup(m => m.GetRedirectUrisAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act
        var (items, _) = await _clientService.GetClientsAsync(0, 25, null, null, "unknown:asc");

        // Assert
        var ordered = items.Select(i => i.ClientId).ToList();
        Assert.Equal(new[] { "alpha", "beta", "zeta" }, ordered);
    }

    [Fact]
    public async Task GetClientsAsync_ShouldHandleLargeNumberOfClientsEfficiently()
    {
        // Arrange
        var clients = Enumerable.Range(1, 1200).Select(_ => new { Id = Guid.NewGuid() }).ToList();
        var index = 0;

        _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(clients));
        _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Guid.NewGuid().ToString());
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"client{++index:0000}");
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Name");
        _mockApplicationManager.Setup(m => m.GetClientTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientTypes.Public);
        _mockApplicationManager.Setup(m => m.GetApplicationTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplicationTypes.Web);
        _mockApplicationManager.Setup(m => m.GetConsentTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConsentTypes.Explicit);
        _mockApplicationManager.Setup(m => m.GetRedirectUrisAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act - skip 100, take 50
        var (items, total) = await _clientService.GetClientsAsync(100, 50, null, null, "clientId:asc");

        // Assert
        Assert.Equal(1200, total);
        Assert.Equal(50, items.Count());
        // First item after skipping 100 should be client0101 (1-based sequence)
        Assert.Equal("client0101", items.First().ClientId);
        Assert.Equal("client0150", items.Last().ClientId);
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

        // Verify domain event was published
        _mockEventPublisher.Verify(x => x.PublishAsync(It.Is<ClientCreatedEvent>(e =>
            e.ClientName == "test-client")), Times.Once);
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

    [Fact]
    public async Task CreateClientAsync_ShouldUseProvidedSecret_ForConfidential_AndNotReturnSecretInResponse()
    {
        // Arrange
        var request = new CreateClientRequest(
            "conf-client",
            "provided-secret",
            "Conf Client",
            ApplicationTypes.Web,
            ClientTypes.Confidential,
            ConsentTypes.Explicit,
            null,
            null,
            new List<string> { Permissions.Endpoints.Token }
        );

        var createdClient = new { Id = Guid.NewGuid() };

        _mockApplicationManager.Setup(m => m.FindByClientIdAsync("conf-client", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);
        _mockApplicationManager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, CancellationToken>((descriptor, _) =>
            {
                Assert.Equal("conf-client", descriptor.ClientId);
                Assert.Equal(ClientTypes.Confidential, descriptor.ClientType);
                Assert.Equal("provided-secret", descriptor.ClientSecret);
                Assert.Equal("Conf Client", descriptor.DisplayName);
            })
            .ReturnsAsync(createdClient);
        _mockApplicationManager.Setup(m => m.GetIdAsync(createdClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(createdClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync("conf-client");
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(createdClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Conf Client");

        // Act
        var result = await _clientService.CreateClientAsync(request);

        // Assert: provided secret should not be echoed back
        Assert.NotNull(result);
        Assert.Equal("conf-client", result.ClientId);
        Assert.Null(result.ClientSecret);
    }

    [Fact]
    public async Task CreateClientAsync_ShouldInferClientType_FromSecretPresence()
    {
        // Arrange 1: Type null + has secret => Confidential
        var req1 = new CreateClientRequest("c1", "s1", null, null, null, null, null, null, null);
        var created1 = new { Id = Guid.NewGuid() };

        _mockApplicationManager.Setup(m => m.FindByClientIdAsync("c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);
        var createCall = 0;
        _mockApplicationManager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, CancellationToken>((d, _) =>
            {
                createCall++;
                if (createCall == 1)
                {
                    Assert.Equal(ClientTypes.Confidential, d.ClientType);
                }
                else if (createCall == 2)
                {
                    Assert.Equal(ClientTypes.Public, d.ClientType);
                }
            })
            .ReturnsAsync(() => createCall == 1 ? (object)created1 : new { Id = Guid.NewGuid() });

        _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("c1");
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("c1");

        // Act 1
        var r1 = await _clientService.CreateClientAsync(req1);
        // Act 2
        var r2 = await _clientService.CreateClientAsync(new CreateClientRequest("c2", null, null, null, null, null, null, null, null));

        // Assert
        Assert.Null(r1.ClientSecret); // provided secret should not be echoed
        Assert.Null(r2.ClientSecret); // public no secret
    }

    [Fact]
    public async Task CreateClientAsync_ShouldFallbackDisplayNameToClientId_WhenNull()
    {
        // Arrange
        var req = new CreateClientRequest("cid-fallback", null, null, null, ClientTypes.Public, null, null, null, null);
        var created = new { Id = Guid.NewGuid() };

        _mockApplicationManager.Setup(m => m.FindByClientIdAsync("cid-fallback", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);
        _mockApplicationManager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, CancellationToken>((d, _) =>
            {
                Assert.Equal("cid-fallback", d.DisplayName);
            })
            .ReturnsAsync(created);
        _mockApplicationManager.Setup(m => m.GetIdAsync(created, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(created, It.IsAny<CancellationToken>()))
            .ReturnsAsync("cid-fallback");
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(created, It.IsAny<CancellationToken>()))
            .ReturnsAsync("cid-fallback");

        // Act
        var result = await _clientService.CreateClientAsync(req);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("cid-fallback", result.DisplayName);
    }

    [Fact]
    public async Task CreateClientAsync_ShouldIgnoreInvalidRedirectUris()
    {
        // Arrange
        var req = new CreateClientRequest(
            "cid-urls",
            null,
            "Name",
            null,
            ClientTypes.Public,
            null,
            new List<string> { "https://valid", "not a url" },
            new List<string> { "http://valid-pl", "not a url" },
            null
        );

        _mockApplicationManager.Setup(m => m.FindByClientIdAsync("cid-urls", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);
        _mockApplicationManager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, CancellationToken>((d, _) =>
            {
                Assert.Single(d.RedirectUris);
                Assert.Contains(d.RedirectUris, u => u.ToString() == "https://valid/");
                Assert.Single(d.PostLogoutRedirectUris);
                Assert.Contains(d.PostLogoutRedirectUris, u => u.ToString() == "http://valid-pl/");
            })
            .ReturnsAsync(new { Id = Guid.NewGuid() });
        _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("cid-urls");
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Name");

        // Act
        var result = await _clientService.CreateClientAsync(req);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CreateClientAsync_ShouldRespectProvidedPermissions_WhenPermissionsGiven()
    {
        // Arrange
        var req = new CreateClientRequest(
            "cid-perm",
            null,
            "Name",
            null,
            ClientTypes.Public,
            null,
            null,
            null,
            new List<string> { "p1", "p2" }
        );

        _mockApplicationManager.Setup(m => m.FindByClientIdAsync("cid-perm", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);
        _mockApplicationManager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, CancellationToken>((d, _) =>
            {
                Assert.Equal(2, d.Permissions.Count);
                Assert.Contains("p1", d.Permissions);
                Assert.Contains("p2", d.Permissions);
            })
            .ReturnsAsync(new { Id = Guid.NewGuid() });
        _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        _mockApplicationManager.Setup(m => m.GetClientIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("cid-perm");
        _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Name");

        // Act
        var result = await _clientService.CreateClientAsync(req);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CreateClientAsync_ShouldAutoAddResponseTypeCode_WhenAuthorizationCodeGrantProvided()
    {
        // Arrange
        var req = new CreateClientRequest(
            "test-client",
            null,
            "Test Client",
            null,
            ClientTypes.Public,
            null,
            null,
            null,
            new List<string> 
            { 
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode
            }
        );

        _mockApplicationManager.Setup(m => m.FindByClientIdAsync("test-client", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);
        _mockApplicationManager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, CancellationToken>((d, _) =>
            {
                // Should auto-add response_type:code when authorization_code grant is present
                Assert.Contains(Permissions.ResponseTypes.Code, d.Permissions);
            })
            .ReturnsAsync(new { Id = Guid.NewGuid() });
        _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());

        // Act
        var result = await _clientService.CreateClientAsync(req);

        // Assert
        Assert.NotNull(result);
        _mockApplicationManager.Verify(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateClientAsync_ShouldAutoAddImplicitResponseTypes_WhenImplicitGrantProvided()
    {
        // Arrange
        var req = new CreateClientRequest(
            "implicit-client",
            null,
            "Implicit Client",
            null,
            ClientTypes.Public,
            null,
            null,
            null,
            new List<string> 
            { 
                Permissions.Endpoints.Authorization,
                Permissions.GrantTypes.Implicit
            }
        );

        _mockApplicationManager.Setup(m => m.FindByClientIdAsync("implicit-client", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);
        _mockApplicationManager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, CancellationToken>((d, _) =>
            {
                // Should auto-add response_type:code, token, and id_token for implicit flow
                Assert.Contains(Permissions.ResponseTypes.Code, d.Permissions);
                Assert.Contains(Permissions.ResponseTypes.Token, d.Permissions);
                Assert.Contains(Permissions.ResponseTypes.IdToken, d.Permissions);
            })
            .ReturnsAsync(new { Id = Guid.NewGuid() });
        _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());

        // Act
        var result = await _clientService.CreateClientAsync(req);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CreateClientAsync_ShouldNotDuplicateResponseTypes_WhenAlreadyProvided()
    {
        // Arrange
        var req = new CreateClientRequest(
            "test-client",
            null,
            "Test Client",
            null,
            ClientTypes.Public,
            null,
            null,
            null,
            new List<string> 
            { 
                Permissions.Endpoints.Authorization,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.ResponseTypes.Code  // Already provided
            }
        );

        _mockApplicationManager.Setup(m => m.FindByClientIdAsync("test-client", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);
        _mockApplicationManager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, CancellationToken>((d, _) =>
            {
                // Should contain response_type:code only once
                var codePermissions = d.Permissions.Where(p => p == Permissions.ResponseTypes.Code).ToList();
                Assert.Single(codePermissions);
            })
            .ReturnsAsync(new { Id = Guid.NewGuid() });
        _mockApplicationManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());

        // Act
        var result = await _clientService.CreateClientAsync(req);

        // Assert
        Assert.NotNull(result);
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

    [Fact]
    public async Task UpdateClientAsync_ShouldReplaceCollections_WhenProvided()
    {
        // Arrange
        var clientId = Guid.NewGuid();
        var client = new { Id = clientId };
        var req = new UpdateClientRequest(null, null, null, null, null,
            new List<string> { "https://a", "https://b" },
            new List<string> { "https://c" },
            new List<string> { "perm1", "perm2" });

        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
        _mockApplicationManager.Setup(m => m.PopulateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), client, It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, object, CancellationToken>((d, _, __) =>
            {
                d.ClientSecret = "keep"; // pre-existing secret
                d.RedirectUris.Add(new Uri("https://old"));
                d.PostLogoutRedirectUris.Add(new Uri("https://old-pl"));
                d.Permissions.Add("old-perm");
            })
            .Returns(ValueTask.CompletedTask);
        _mockApplicationManager.Setup(m => m.PopulateAsync(client, It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<object, OpenIddictApplicationDescriptor, CancellationToken>((_, d, __) =>
            {
                Assert.Equal(new[] { "https://a/", "https://b/" }, d.RedirectUris.Select(u => u.ToString()));
                Assert.Equal(new[] { "https://c/" }, d.PostLogoutRedirectUris.Select(u => u.ToString()));
                Assert.Equal(new[] { "perm1", "perm2" }, d.Permissions);
                Assert.Equal("keep", d.ClientSecret); // unchanged
            })
            .Returns(ValueTask.CompletedTask);
        _mockApplicationManager.Setup(m => m.UpdateAsync(client, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        // Act
        await _clientService.UpdateClientAsync(clientId, req);

        // Assert
        _mockApplicationManager.Verify(m => m.UpdateAsync(client, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateClientAsync_ShouldDefaultTypes_WhenMissing()
    {
        // Arrange
        var clientId = Guid.NewGuid();
        var client = new { Id = clientId };
        var req = new UpdateClientRequest(null, null, null, null, null, null, null, null);

        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
        _mockApplicationManager.Setup(m => m.PopulateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), client, It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, object, CancellationToken>((d, _, __) =>
            {
                // Simulate missing types but with existing secret
                d.ApplicationType = null;
                d.ClientType = null;
                d.ClientSecret = "existing";
            })
            .Returns(ValueTask.CompletedTask);
        _mockApplicationManager.Setup(m => m.PopulateAsync(client, It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<object, OpenIddictApplicationDescriptor, CancellationToken>((_, d, __) =>
            {
                Assert.Equal(ApplicationTypes.Web, d.ApplicationType);
                Assert.Equal(ClientTypes.Confidential, d.ClientType); // inferred from having a secret
            })
            .Returns(ValueTask.CompletedTask);
        _mockApplicationManager.Setup(m => m.UpdateAsync(client, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        // Act
        await _clientService.UpdateClientAsync(clientId, req);

        // Assert
        _mockApplicationManager.Verify(m => m.UpdateAsync(client, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateClientAsync_ShouldUpdateBasicFields_WithoutChangingSecret()
    {
        // Arrange
        var clientId = Guid.NewGuid();
        var client = new { Id = clientId };
        var req = new UpdateClientRequest("new-id", null, "new-name", null, ConsentTypes.Implicit, null, null, null);

        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
        _mockApplicationManager.Setup(m => m.PopulateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), client, It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, object, CancellationToken>((d, _, __) =>
            {
                d.ClientSecret = "keepme";
            })
            .Returns(ValueTask.CompletedTask);
        _mockApplicationManager.Setup(m => m.PopulateAsync(client, It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<object, OpenIddictApplicationDescriptor, CancellationToken>((_, d, __) =>
            {
                Assert.Equal("new-id", d.ClientId);
                Assert.Equal("new-name", d.DisplayName);
                Assert.Equal(ConsentTypes.Implicit, d.ConsentType);
                Assert.Equal("keepme", d.ClientSecret);
            })
            .Returns(ValueTask.CompletedTask);
        _mockApplicationManager.Setup(m => m.UpdateAsync(client, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        // Act
        await _clientService.UpdateClientAsync(clientId, req);

        // Assert
        _mockApplicationManager.Verify(m => m.UpdateAsync(client, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateClientAsync_ShouldAutoAddResponseTypeCode_WhenAuthorizationCodeGrantProvided()
    {
        // Arrange
        var clientId = Guid.NewGuid();
        var client = new { Id = clientId };
        var request = new UpdateClientRequest(
            null, 
            null, 
            null, 
            null, 
            null, 
            null, 
            null, 
            new List<string> 
            { 
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode
            }
        );
        
        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
        _mockApplicationManager.Setup(m => m.PopulateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), client, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        _mockApplicationManager.Setup(m => m.PopulateAsync(client, It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<object, OpenIddictApplicationDescriptor, CancellationToken>((_, d, _) =>
            {
                // Should auto-add response_type:code when authorization_code grant is present
                Assert.Contains(Permissions.ResponseTypes.Code, d.Permissions);
            })
            .Returns(ValueTask.CompletedTask);
        _mockApplicationManager.Setup(m => m.UpdateAsync(client, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        // Act
        await _clientService.UpdateClientAsync(clientId, request);

        // Assert
        _mockApplicationManager.Verify(m => m.UpdateAsync(client, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateClientAsync_ShouldAutoAddImplicitResponseTypes_WhenImplicitGrantProvided()
    {
        // Arrange
        var clientId = Guid.NewGuid();
        var client = new { Id = clientId };
        var request = new UpdateClientRequest(
            null, 
            null, 
            null, 
            null, 
            null, 
            null, 
            null, 
            new List<string> 
            { 
                Permissions.Endpoints.Authorization,
                Permissions.GrantTypes.Implicit
            }
        );
        
        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
        _mockApplicationManager.Setup(m => m.PopulateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), client, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        _mockApplicationManager.Setup(m => m.PopulateAsync(client, It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<object, OpenIddictApplicationDescriptor, CancellationToken>((_, d, _) =>
            {
                // Should auto-add response_type:code, token, and id_token for implicit flow
                Assert.Contains(Permissions.ResponseTypes.Code, d.Permissions);
                Assert.Contains(Permissions.ResponseTypes.Token, d.Permissions);
                Assert.Contains(Permissions.ResponseTypes.IdToken, d.Permissions);
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
