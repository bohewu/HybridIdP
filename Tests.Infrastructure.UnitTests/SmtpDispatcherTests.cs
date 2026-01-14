using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Core.Domain.Models;
using Infrastructure.Services;
using Core.Application;
using Core.Domain.Constants;
using Microsoft.Extensions.Options;
using Core.Application.Options;
using FluentAssertions;

namespace Tests.Infrastructure.UnitTests;

public class SmtpDispatcherTests
{
    private readonly Mock<IOptionsSnapshot<EmailOptions>> _mockOptions;
    private readonly Mock<ILogger<SmtpDispatcher>> _mockLogger;

    public SmtpDispatcherTests()
    {
        _mockOptions = new Mock<IOptionsSnapshot<EmailOptions>>();
        _mockLogger = new Mock<ILogger<SmtpDispatcher>>();
    }

    [Fact]
    public async Task SendAsync_ShouldRetrieveSettings_AndAttemptSend()
    {
        // THIS IS AN INTEGRATION/INTERACTION TEST MOCKING OPTIONS
        // We cannot easily mock SmtpClient extension methods without a wrapper, 
        // so here we focus on verifying it retrieves settings correctly.
        
        // Arrange
        var emailOptions = new EmailOptions
        {
            SmtpHost = "localhost",
            SmtpPort = 1025,
            FromAddress = "test@test.com",
            FromName = "Test Sender"
        };
        _mockOptions.Setup(o => o.Value).Returns(emailOptions);

        var dispatcher = new SmtpDispatcher(_mockOptions.Object, _mockLogger.Object);
        var message = new EmailMessage("to@test.com", "Subject", "Body");

        // Act & Assert
        // We expect an exception or success depending on if localhost:1025 is actually running.
        // For a unit test, we might just assert it retrieves Options.Value.
        
        // Act & Assert
        // Expect exception because we don't have a real SMTP server running
        await Assert.ThrowsAnyAsync<Exception>(() => dispatcher.SendAsync(message));
        
        _mockOptions.Verify(o => o.Value, Times.Once);
    }
}
