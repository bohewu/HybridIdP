using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

using Core.Domain.Constants; // Added

namespace Tests.Infrastructure.UnitTests;

public class EmailServiceTests
{
    private readonly Mock<ISettingsService> _mockSettings;
    private readonly Mock<ILogger<EmailService>> _mockLogger;
    private readonly EmailService _service;

    public EmailServiceTests()
    {
        _mockSettings = new Mock<ISettingsService>();
        _mockLogger = new Mock<ILogger<EmailService>>();
        // We will need to handle the SmtpClient dependency if we want to test sending.
        // For now, we are testing the service orchestration, but without an abstraction for SmtpClient, 
        // we can't assert that Send was called without actually sending.
        // So we will implement the service with a protected virtual method or a factory for SmtpClient if needed.
        // For this iteration, we will focus on the logic that builds the message or retrieves settings.
        
        _service = new EmailService(_mockSettings.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GetSettingsAsync_ShouldRetrieveAllMailSettings()
    {
        // Arrange
        _mockSettings.Setup(x => x.GetValueAsync<string>(SettingKeys.Email.SmtpHost, It.IsAny<CancellationToken>())).ReturnsAsync("smtp.example.com");
        _mockSettings.Setup(x => x.GetValueAsync<string>(SettingKeys.Email.SmtpPort, It.IsAny<CancellationToken>())).ReturnsAsync("587");
        _mockSettings.Setup(x => x.GetValueAsync<string>(SettingKeys.Email.SmtpUsername, It.IsAny<CancellationToken>())).ReturnsAsync("user");
        _mockSettings.Setup(x => x.GetValueAsync<string>(SettingKeys.Email.SmtpPassword, It.IsAny<CancellationToken>())).ReturnsAsync("pass");
        _mockSettings.Setup(x => x.GetValueAsync<string>(SettingKeys.Email.SmtpEnableSsl, It.IsAny<CancellationToken>())).ReturnsAsync("true");
        _mockSettings.Setup(x => x.GetValueAsync<string>(SettingKeys.Email.FromAddress, It.IsAny<CancellationToken>())).ReturnsAsync("no-reply@example.com");
        _mockSettings.Setup(x => x.GetValueAsync<string>(SettingKeys.Email.FromName, It.IsAny<CancellationToken>())).ReturnsAsync("Test Sender");

        // Act
        // We need to expose the settings retrieval logic or test it indirectly.
        // Since SendEmailAsync will fail without a real server, we might want to separate the logic.
        // Let's make a public method GetSettingsAsync on the service or make it internal and visible to tests.
        // For now, I'll assume we can call a method to get settings.
        
        var settings = await _service.GetMailSettingsAsync();

        // Assert
        Assert.Equal("smtp.example.com", settings.Host);
        Assert.Equal(587, settings.Port);
        Assert.Equal("user", settings.Username);
        Assert.Equal("pass", settings.Password);
        Assert.True(settings.EnableSsl);
        Assert.Equal("no-reply@example.com", settings.FromAddress);
        Assert.Equal("Test Sender", settings.FromName);
    }
}
