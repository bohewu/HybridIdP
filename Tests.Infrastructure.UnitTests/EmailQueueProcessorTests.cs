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
        var cts = new CancellationTokenSource();

        _mockQueue.SetupSequence(q => q.DequeueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(message) // First call returns message
            .Returns(async () => { // Second call waits indefinitely (simulating idle)
                await Task.Delay(500, cts.Token); 
                return null!; 
            });

        var processor = new EmailQueueProcessor(_mockQueue.Object, _mockScopeFactory.Object, _mockLogger.Object);

        // Act
        // We run the background service for a short time
        var executeTask = processor.StartAsync(cts.Token);
        
        // Allow some time for processing
        await Task.Delay(100);
        cts.Cancel(); // Stop the service

        try { await executeTask; } catch (OperationCanceledException) { }

        // Assert
        _mockDispatcher.Verify(d => d.SendAsync(message, It.IsAny<CancellationToken>()), Times.Once);
    }
}
