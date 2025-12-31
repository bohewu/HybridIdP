using System;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Domain.Constants;
using Infrastructure.Services;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests
{
    public class BrandingServiceTests
    {
        private readonly Mock<ISettingsService> _settingsServiceMock;
        private readonly BrandingService _service;

        public BrandingServiceTests()
        {
            _settingsServiceMock = new Mock<ISettingsService>();
            _service = new BrandingService(_settingsServiceMock.Object);
        }

        [Fact]
        public async Task GetCopyrightAsync_ShouldReturnDefaultWithCurrentYear_WhenSettingIsNull()
        {
            // Arrange
            _settingsServiceMock.Setup(x => x.GetValueAsync(SettingKeys.Branding.Copyright, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string)null);

            // Act
            var result = await _service.GetCopyrightAsync();

            // Assert
            Assert.Equal($"© {DateTime.Now.Year}", result);
        }

        [Fact]
        public async Task GetCopyrightAsync_ShouldReturnDefaultWithCurrentYear_WhenSettingIsEmpty()
        {
            // Arrange
            _settingsServiceMock.Setup(x => x.GetValueAsync(SettingKeys.Branding.Copyright, It.IsAny<CancellationToken>()))
                .ReturnsAsync("");

            // Act
            var result = await _service.GetCopyrightAsync();

            // Assert
            Assert.Equal($"© {DateTime.Now.Year}", result);
        }

        [Fact]
        public async Task GetCopyrightAsync_ShouldAutoCorrect_C_WrappedInParentheses()
        {
             // Arrange
            _settingsServiceMock.Setup(x => x.GetValueAsync(SettingKeys.Branding.Copyright, It.IsAny<CancellationToken>()))
                .ReturnsAsync("(c) 2024 MyCompany");

            // Act
            var result = await _service.GetCopyrightAsync();

            // Assert
            Assert.Equal("© 2024 MyCompany", result);
        }

        [Fact]
        public async Task GetCopyrightAsync_ShouldAutoCorrect_C_Space_Year()
        {
             // Arrange
            _settingsServiceMock.Setup(x => x.GetValueAsync(SettingKeys.Branding.Copyright, It.IsAny<CancellationToken>()))
                .ReturnsAsync("c 2025 HybridAuth");

            // Act
            var result = await _service.GetCopyrightAsync();

            // Assert
            Assert.Equal("© 2025 HybridAuth", result);
        }

        [Fact]
        public async Task GetCopyrightAsync_ShouldReturnOriginal_WhenAlreadyCorrect()
        {
             // Arrange
            _settingsServiceMock.Setup(x => x.GetValueAsync(SettingKeys.Branding.Copyright, It.IsAny<CancellationToken>()))
                .ReturnsAsync("© 2023 Original");

            // Act
            var result = await _service.GetCopyrightAsync();

            // Assert
            Assert.Equal("© 2023 Original", result);
        }

         [Fact]
        public async Task GetCopyrightAsync_ShouldReturnOriginal_WhenNoMatchRule()
        {
             // Arrange
            _settingsServiceMock.Setup(x => x.GetValueAsync(SettingKeys.Branding.Copyright, It.IsAny<CancellationToken>()))
                .ReturnsAsync("Copyright 2023");

            // Act
            var result = await _service.GetCopyrightAsync();

            // Assert
            Assert.Equal("Copyright 2023", result);
        }
    }
}
