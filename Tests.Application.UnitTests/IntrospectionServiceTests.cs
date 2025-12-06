using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Moq;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Web.IdP.Services;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tests.Application.UnitTests
{
    public class IntrospectionServiceTests
    {
        private readonly Mock<IOpenIddictTokenManager> _mockTokenManager;
        private readonly IntrospectionService _service;

        public IntrospectionServiceTests()
        {
            _mockTokenManager = new Mock<IOpenIddictTokenManager>();
            _service = new IntrospectionService(_mockTokenManager.Object);
        }

        [Fact]
        public async Task HandleIntrospectionRequestAsync_NullRequest_ThrowsArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _service.HandleIntrospectionRequestAsync(null!));
        }

        [Fact]
        public async Task HandleIntrospectionRequestAsync_InvalidToken_ReturnsForbidResult()
        {
            // Arrange
            var request = new OpenIddictRequest
            {
                Token = "invalid_token"
            };

            _mockTokenManager.Setup(m => m.FindByIdAsync(request.Token, It.IsAny<CancellationToken>()))
                .ReturnsAsync((object?)null);

            // Act
            var result = await _service.HandleIntrospectionRequestAsync(request);

            // Assert
            var forbidResult = Assert.IsType<ForbidResult>(result);
            Assert.Equal(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, forbidResult.AuthenticationSchemes[0]);
            Assert.Equal(Errors.InvalidToken, forbidResult.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
        }

        [Fact]
        public async Task HandleIntrospectionRequestAsync_ValidToken_ReturnsSignInResult()
        {
            // Arrange
            var request = new OpenIddictRequest
            {
                Token = "valid_token"
            };

            _mockTokenManager.Setup(m => m.FindByIdAsync(request.Token, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new object()); // Mock token object

            // Act
            var result = await _service.HandleIntrospectionRequestAsync(request);

            // Assert
            var signInResult = Assert.IsType<SignInResult>(result);
            Assert.Equal(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, signInResult.AuthenticationScheme);
        }
    }
}
