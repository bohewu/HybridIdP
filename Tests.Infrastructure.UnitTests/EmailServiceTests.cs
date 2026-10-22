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
    public async Task SendEmailWithConfirmationAsync_ShouldAwaitDispatcherWithoutQueueing()
    {
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _mockDispatcher.Setup(dispatcher => dispatcher.SendConfirmedAsync(
            It.Is<EmailMessage>(message => message.To == "to@example.test" && message.IsHtml), It.IsAny<CancellationToken>()))
            .Returns(accepted.Task);
        var sending = _service.SendEmailWithConfirmationAsync("to@example.test", "Subject", "Body", true);
        try
        {
            Assert.False(sending.IsCompleted);
            accepted.SetResult();
            await sending;
            _mockQueue.VerifyNoOtherCalls();
            _mockDispatcher.Verify(dispatcher => dispatcher.SendConfirmedAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Once);
            _mockDispatcher.Verify(dispatcher => dispatcher.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally { accepted.TrySetResult(); }
    }

    [Fact]
    public async Task SendEmailWithConfirmationAsync_ShouldFailWithoutQueueFallback_WhenDispatchFails()
    {
        _mockDispatcher.Setup(dispatcher => dispatcher.SendConfirmedAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.IO.IOException("Injected connection failure"));

        var failure = await Assert.ThrowsAsync<EmailDeliveryException>(() =>
            _service.SendEmailWithConfirmationAsync("to@example.test", "Subject", "Body"));

        Assert.Equal("smtp_confirmation_unavailable", failure.Code);
        _mockQueue.VerifyNoOtherCalls();
        _mockDispatcher.Verify(dispatcher => dispatcher.SendConfirmedAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendEmailWithConfirmationAsync_ShouldBoundUnconfirmedDispatchAndCancelIt()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken dispatchToken = default;
        _mockDispatcher.Setup(dispatcher => dispatcher.SendConfirmedAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((_, ct) => dispatchToken = ct).Returns(pending.Task);
        try
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var failure = await Assert.ThrowsAsync<EmailDeliveryException>(() =>
                _service.SendEmailWithConfirmationAsync("to@example.test", "Subject", "Body"));
            Assert.Equal("smtp_confirmation_unavailable", failure.Code);
            Assert.InRange(timer.Elapsed.TotalSeconds, 14, 20);
            Assert.True(dispatchToken.IsCancellationRequested);
            _mockQueue.VerifyNoOtherCalls();
            _mockDispatcher.Verify(dispatcher => dispatcher.SendConfirmedAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally { pending.TrySetResult(); }
    }

    [Fact]
    public async Task ConfirmationInterfaces_ShouldFailClosed_ForLegacyNotificationAdapters()
    {
        IEmailService service = new LegacyEmailService();
        IEmailDispatcher dispatcher = new LegacyDispatcher();
        await Assert.ThrowsAsync<EmailDeliveryException>(() => service.SendEmailWithConfirmationAsync("to@example.test", "Subject", "Body"));
        await Assert.ThrowsAsync<EmailDeliveryException>(() => dispatcher.SendConfirmedAsync(new EmailMessage("to@example.test", "Subject", "Body")));
    }

    private sealed class LegacyEmailService : IEmailService
    {
        public Task SendEmailAsync(string to, string subject, string body, bool isHtml = false, CancellationToken ct = default) =>
            throw new System.InvalidOperationException("Must not fall back to queueing.");
        public Task SendTestEmailAsync(MailSettingsDto settings, string to, CancellationToken ct = default) =>
            throw new System.InvalidOperationException("Must not fall back to test sending.");
    }

    private sealed class LegacyDispatcher : IEmailDispatcher
    {
        public Task SendAsync(EmailMessage message, CancellationToken ct = default) =>
            throw new System.InvalidOperationException("Must not fall back to unconfirmed sending.");
        public Task SendTestAsync(EmailMessage message, MailSettingsDto settings, CancellationToken ct = default) =>
            throw new System.InvalidOperationException("Must not fall back to test sending.");
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
