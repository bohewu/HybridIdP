using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Core.Domain.Models;
using Infrastructure.BackgroundServices;
using Core.Application.Interfaces;
using FluentAssertions;
using System;

namespace Tests.Infrastructure.UnitTests;

public class EmailQueueProcessorTests
{
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly Mock<IServiceScope> _mockScope;
    private readonly Mock<IEmailQueue> _mockQueue;
    private readonly Mock<IEmailDispatcher> _mockDispatcher;
    private readonly Mock<ILogger<EmailQueueProcessor>> _mockLogger;

    public EmailQueueProcessorTests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockScope = new Mock<IServiceScope>();
        _mockQueue = new Mock<IEmailQueue>();
        _mockDispatcher = new Mock<IEmailDispatcher>();
        _mockLogger = new Mock<ILogger<EmailQueueProcessor>>();

        _mockScopeFactory.Setup(x => x.CreateScope()).Returns(_mockScope.Object);
        _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
        _mockServiceProvider.Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(_mockScopeFactory.Object);
        _mockServiceProvider.Setup(x => x.GetService(typeof(IEmailDispatcher)))
            .Returns(_mockDispatcher.Object);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldDequeueAndDispatch()
    {
        // Arrange
        var message = new EmailMessage { To = "test@test.com" };
        using var cts = new CancellationTokenSource();
        var messageDispatched = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _mockQueue.SetupSequence(q => q.DequeueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(message) // First call returns message
            .Returns(async () => { // Second call waits indefinitely (simulating idle)
                await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
                return null!; 
            });
        _mockDispatcher
            .Setup(dispatcher => dispatcher.SendAsync(message, It.IsAny<CancellationToken>()))
            .Callback(() => messageDispatched.TrySetResult())
            .Returns(Task.CompletedTask);

        var processor = new EmailQueueProcessor(_mockQueue.Object, _mockScopeFactory.Object, _mockLogger.Object);

        // Act
        await processor.StartAsync(cts.Token);
        await messageDispatched.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cts.CancelAsync();
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await processor.StopAsync(stopCts.Token);

        // Assert
        _mockDispatcher.Verify(d => d.SendAsync(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldContinueAfterIndividualDeliveryFailure()
    {
        var failedMessage = new EmailMessage { To = "failed@test.com" };
        var succeedingMessage = new EmailMessage { To = "succeeding@test.com" };
        using var cts = new CancellationTokenSource();
        var succeedingDispatch = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<EmailMessage> WaitForCancellationAsync()
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
            return null!;
        }

        _mockQueue.SetupSequence(queue => queue.DequeueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedMessage)
            .ReturnsAsync(succeedingMessage)
            .Returns(new ValueTask<EmailMessage>(WaitForCancellationAsync()));
        _mockDispatcher
            .Setup(dispatcher => dispatcher.SendAsync(
                failedMessage,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP rejected the message"));
        _mockDispatcher
            .Setup(dispatcher => dispatcher.SendAsync(
                succeedingMessage,
                It.IsAny<CancellationToken>()))
            .Callback(() => succeedingDispatch.TrySetResult())
            .Returns(Task.CompletedTask);

        var processor = new EmailQueueProcessor(
            _mockQueue.Object,
            _mockScopeFactory.Object,
            _mockLogger.Object);

        await processor.StartAsync(cts.Token);
        await succeedingDispatch.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();
        await processor.StopAsync(CancellationToken.None);

        _mockDispatcher.Verify(dispatcher => dispatcher.SendAsync(
            failedMessage,
            It.IsAny<CancellationToken>()), Times.Once);
        _mockDispatcher.Verify(dispatcher => dispatcher.SendAsync(
            succeedingMessage,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_GracefulShutdown_DrainsRemainingEmails()
    {
        // Arrange
        var message1 = new EmailMessage { To = "first@test.com" };
        var message2 = new EmailMessage { To = "second@test.com" };
        using var cts = new CancellationTokenSource();
        var dequeueStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        
        // Setup: First dequeue blocks, then TryDequeue returns messages during drain
        _mockQueue.Setup(q => q.DequeueAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) => {
                dequeueStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return null!;
            });
        
        // During drain, TryDequeue returns messages then false
        var drainCallCount = 0;
        _mockQueue.Setup(q => q.TryDequeue(out It.Ref<EmailMessage?>.IsAny))
            .Returns((out EmailMessage? msg) => {
                drainCallCount++;
                if (drainCallCount == 1) { msg = message1; return true; }
                if (drainCallCount == 2) { msg = message2; return true; }
                msg = null;
                return false;
            });
        
        var processor = new EmailQueueProcessor(_mockQueue.Object, _mockScopeFactory.Object, _mockLogger.Object);

        // Act
        await processor.StartAsync(cts.Token);
        await dequeueStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cts.CancelAsync();
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await processor.StopAsync(stopCts.Token);

        // Assert - Both messages should have been dispatched during drain
        _mockDispatcher.Verify(d => d.SendAsync(message1, It.IsAny<CancellationToken>()), Times.Once);
        _mockDispatcher.Verify(d => d.SendAsync(message2, It.IsAny<CancellationToken>()), Times.Once);
    }
}

