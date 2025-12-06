using Microsoft.AspNetCore.Mvc;
using Moq;
using OpenIddict.Abstractions;
using Web.IdP.Services;
using Xunit;

namespace Tests.Application.UnitTests
{
    public class RevocationServiceTests
    {
        private readonly Mock<IOpenIddictTokenManager> _mockTokenManager;
        private readonly RevocationService _service;

        public RevocationServiceTests()
        {
            _mockTokenManager = new Mock<IOpenIddictTokenManager>();
            _service = new RevocationService(_mockTokenManager.Object);
        }

        [Fact]
        public async Task HandleRevocationRequestAsync_NullRequest_ThrowsArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _service.HandleRevocationRequestAsync(null!));
        }

        [Fact]
        public async Task HandleRevocationRequestAsync_TokenFound_RevokesTokenAndReturnsOk()
        {
            // Arrange
            var request = new OpenIddictRequest
            {
                Token = "valid_token"
            };
            var token = new object();

            _mockTokenManager.Setup(m => m.FindByIdAsync(request.Token, It.IsAny<CancellationToken>()))
                .ReturnsAsync(token);

            // Act
            var result = await _service.HandleRevocationRequestAsync(request);

            // Assert
            _mockTokenManager.Verify(m => m.TryRevokeAsync(token, It.IsAny<CancellationToken>()), Times.Once);
            Assert.IsType<OkResult>(result);
        }

        [Fact]
        public async Task HandleRevocationRequestAsync_TokenNotFound_ReturnsOk()
        {
            // Arrange
            var request = new OpenIddictRequest
            {
                Token = "invalid_token"
            };

            _mockTokenManager.Setup(m => m.FindByIdAsync(request.Token, It.IsAny<CancellationToken>()))
                .ReturnsAsync((object?)null);

            // Act
            var result = await _service.HandleRevocationRequestAsync(request);

            // Assert
            _mockTokenManager.Verify(m => m.TryRevokeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.IsType<OkResult>(result);
        }
    }
}
