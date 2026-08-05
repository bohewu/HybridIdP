using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Infrastructure.Services;
using Moq;
using Xunit;
using Core.Domain.Models;
using Core.Application.Interfaces;

namespace Tests.Infrastructure.UnitTests;

public class EmailServiceTests
{
    private readonly Mock<IEmailQueue> _mockQueue;
    private readonly Mock<IEmailDispatcher> _mockDispatcher;
    private readonly EmailService _service;

    public EmailServiceTests()
    {
        _mockQueue = new Mock<IEmailQueue>();
        _mockDispatcher = new Mock<IEmailDispatcher>();
        _service = new EmailService(_mockQueue.Object, _mockDispatcher.Object);
    }

    [Fact]
    public async Task SendEmailAsync_ShouldQueueMessage()
    {
        // Arrange
        var to = "test@example.com";
        var subject = "Subject";
        var body = "Body";

        // Act
        await _service.SendEmailAsync(to, subject, body);

        // Assert
        _mockQueue.Verify(q => q.QueueEmailAsync(It.Is<EmailMessage>(
            m => m.To == to && m.Subject == subject && m.Body == body
        )), Times.Once);
    }

    [Fact]
    public async Task SendTestEmailAsync_ShouldDispatchImmediatelyWithProvidedSettings()
    {
        // Arrange
        var to = "test@example.com";
        var settings = new MailSettingsDto();

        // Act
        await _service.SendTestEmailAsync(settings, to);

        // Assert
        _mockDispatcher.Verify(dispatcher => dispatcher.SendTestAsync(
            It.Is<EmailMessage>(message =>
                message.To == to && message.Subject == "Test Email from HybridIdP"),
            settings,
            It.IsAny<CancellationToken>()), Times.Once);
        _mockQueue.Verify(
            queue => queue.QueueEmailAsync(It.IsAny<EmailMessage>()),
            Times.Never);
    }
}
