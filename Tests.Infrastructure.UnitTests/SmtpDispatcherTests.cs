using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Core.Domain.Models;
using Infrastructure.Services;
using Core.Application;
using Core.Domain.Constants;
using FluentAssertions;

namespace Tests.Infrastructure.UnitTests;

public class SmtpDispatcherTests
{
    private readonly Mock<ISettingsService> _mockSettings;
    private readonly Mock<ILogger<SmtpDispatcher>> _mockLogger;

    public SmtpDispatcherTests()
    {
        _mockSettings = new Mock<ISettingsService>();
        _mockLogger = new Mock<ILogger<SmtpDispatcher>>();
    }

    [Fact]
    public async Task SendAsync_ShouldRetrieveSettings_AndAttemptSend()
    {
        // THIS IS AN INTEGRATION/INTERACTION TEST MOCKING SETTINGS SERVICE
        // We cannot easily mock SmtpClient extension methods without a wrapper, 
        // so here we focus on verifying it retrieves settings correctly.
        
        // Arrange
        _mockSettings.Setup(s => s.GetValueAsync<string>(SettingKeys.Email.SmtpHost, It.IsAny<CancellationToken>()))
            .ReturnsAsync("localhost");
        _mockSettings.Setup(s => s.GetValueAsync<string>(SettingKeys.Email.SmtpPort, It.IsAny<CancellationToken>()))
            .ReturnsAsync("1025");
        _mockSettings.Setup(s => s.GetValueAsync<string>(SettingKeys.Email.FromAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync("test@test.com");

        var dispatcher = new SmtpDispatcher(_mockSettings.Object, _mockLogger.Object);
        var message = new EmailMessage("to@test.com", "Subject", "Body");

        // Act & Assert
        // We expect an exception or success depending on if localhost:1025 is actually running.
        // For a unit test, we might just assert it calls GetValueAsync.
        // If we want to test actual sending, we need Mailpit running (Integration Test).
        
        // For this TDD step, let's verify logic flows:
        await dispatcher.SendAsync(message);
        
        _mockSettings.Verify(s => s.GetValueAsync<string>(SettingKeys.Email.SmtpHost, It.IsAny<CancellationToken>()), Times.Once);
    }
}
