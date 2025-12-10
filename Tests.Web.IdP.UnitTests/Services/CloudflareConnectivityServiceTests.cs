using Moq;
using Moq.Protected;
using Web.IdP.Services;
using Microsoft.Extensions.Logging;
using System.Net;
using Xunit;
using Core.Application; // Added

namespace Tests.Web.IdP.UnitTests.Services;

public class CloudflareConnectivityServiceTests
{
    private readonly Mock<ITurnstileStateService> _mockStateService;
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly CloudflareConnectivityService _service;

    public CloudflareConnectivityServiceTests()
    {
        _mockStateService = new Mock<ITurnstileStateService>();
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(_mockHttpMessageHandler.Object)
        {
            BaseAddress = new Uri("https://challenges.cloudflare.com/")
        };
        var mockLogger = new Mock<ILogger<CloudflareConnectivityService>>();

        _service = new CloudflareConnectivityService(_mockStateService.Object, httpClient, mockLogger.Object);
    }

    [Fact]
    public async Task CheckConnectivityAsync_ShouldEnable_WhenRequestSucceeds()
    {
        // Arrange
        _mockStateService.Setup(s => s.IsAvailable).Returns(false); // Currently disabled
        
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK
            });

        // Act
        await _service.CheckConnectivityAsync(CancellationToken.None);

        // Assert
        _mockStateService.Verify(s => s.SetAvailable(true), Times.Once);
    }

    [Fact]
    public async Task CheckConnectivityAsync_ShouldDisable_WhenRequestFails()
    {
        // Arrange
        _mockStateService.Setup(s => s.IsAvailable).Returns(true); // Currently enabled
        
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.ServiceUnavailable
            });

        // Act
        await _service.CheckConnectivityAsync(CancellationToken.None);

        // Assert
        _mockStateService.Verify(s => s.SetAvailable(false), Times.Once);
    }
    
    [Fact]
    public async Task CheckConnectivityAsync_ShouldDisable_WhenExceptionOccurs()
    {
        // Arrange
        _mockStateService.Setup(s => s.IsAvailable).Returns(true); // Currently enabled
        
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ThrowsAsync(new HttpRequestException("Connection failed"));

        // Act
        await _service.CheckConnectivityAsync(CancellationToken.None);

        // Assert
        _mockStateService.Verify(s => s.SetAvailable(false), Times.Once);
    }
}
