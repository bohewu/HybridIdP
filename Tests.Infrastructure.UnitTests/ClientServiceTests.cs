using System;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Entities;
using Core.Domain.Events;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenIddict.Abstractions;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

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
            _mockScopeManager.Object
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

            RedirectUris: new List<string> { "http://dummy" },
            PostLogoutRedirectUris: null,
            Permissions: null
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
            RedirectUris: new List<string> { "http://dummy" },
            PostLogoutRedirectUris: null,
            Permissions: null
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
            RedirectUris: new List<string> { "http://dummy" },
            PostLogoutRedirectUris: null,
            Permissions: null
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
            Permissions: null
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
            Permissions: null // Defaults to Auth Code
        );

        _mockApplicationManager.Setup(m => m.FindByClientIdAsync(request.ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object)null);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateClientAsync(request));
    }
}
