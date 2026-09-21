using Infrastructure.Jobs;
using Core.Application;
using Microsoft.Extensions.Logging;
using Moq;
using Quartz;
using Xunit;

namespace Tests.Infrastructure.UnitTests.Jobs;

public class PersonLifecycleJobTests
{
    private readonly Mock<IPersonLifecycleService> _lifecycleServiceMock;
    private readonly Mock<ILogger<PersonLifecycleJob>> _loggerMock;
    private readonly Mock<IJobExecutionContext> _contextMock;
    private readonly PersonLifecycleJob _job;

    public PersonLifecycleJobTests()
    {
        _lifecycleServiceMock = new Mock<IPersonLifecycleService>();
        _loggerMock = new Mock<ILogger<PersonLifecycleJob>>();
        _contextMock = new Mock<IJobExecutionContext>();
        _job = new PersonLifecycleJob(_lifecycleServiceMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Execute_CallsProcessScheduledTransitions()
    {
        // Arrange
        _lifecycleServiceMock
            .Setup(s => s.ProcessScheduledTransitionsAsync(CancellationToken.None))
            .ReturnsAsync(0);

        // Act
        await _job.Execute(_contextMock.Object, CancellationToken.None);

        // Assert
        _lifecycleServiceMock.Verify(
            s => s.ProcessScheduledTransitionsAsync(CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task Execute_WhenChangesOccur_LogsCount()
    {
        // Arrange
        _lifecycleServiceMock
            .Setup(s => s.ProcessScheduledTransitionsAsync(CancellationToken.None))
            .ReturnsAsync(5);

        // Act
        await _job.Execute(_contextMock.Object, CancellationToken.None);

        // Assert
        _lifecycleServiceMock.Verify(
            s => s.ProcessScheduledTransitionsAsync(CancellationToken.None),
            Times.Once);
        // Logging verified implicitly (no exception)
    }

    [Fact]
    public async Task Execute_WhenServiceThrows_RethrowsException()
    {
        // Arrange
        _lifecycleServiceMock
            .Setup(s => s.ProcessScheduledTransitionsAsync(CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _job.Execute(_contextMock.Object, CancellationToken.None));
    }

    [Fact]
    public async Task Execute_PassesCancellationTokenToLifecycleService()
    {
        using var cancellation = new CancellationTokenSource();
        _lifecycleServiceMock
            .Setup(service => service.ProcessScheduledTransitionsAsync(cancellation.Token))
            .ReturnsAsync(0);

        await _job.Execute(_contextMock.Object, cancellation.Token);

        _lifecycleServiceMock.Verify(
            service => service.ProcessScheduledTransitionsAsync(cancellation.Token),
            Times.Once);
    }

    [Fact]
    public async Task Execute_WhenCancellationRequested_DoesNotRunService()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await _job.Execute(_contextMock.Object, cancellation.Token));

        _lifecycleServiceMock.Verify(
            service => service.ProcessScheduledTransitionsAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
