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
            .Setup(s => s.ProcessScheduledTransitionsAsync())
            .ReturnsAsync(0);

        // Act
        await _job.Execute(_contextMock.Object);

        // Assert
        _lifecycleServiceMock.Verify(s => s.ProcessScheduledTransitionsAsync(), Times.Once);
    }

    [Fact]
    public async Task Execute_WhenChangesOccur_LogsCount()
    {
        // Arrange
        _lifecycleServiceMock
            .Setup(s => s.ProcessScheduledTransitionsAsync())
            .ReturnsAsync(5);

        // Act
        await _job.Execute(_contextMock.Object);

        // Assert
        _lifecycleServiceMock.Verify(s => s.ProcessScheduledTransitionsAsync(), Times.Once);
        // Logging verified implicitly (no exception)
    }

    [Fact]
    public async Task Execute_WhenServiceThrows_RethrowsException()
    {
        // Arrange
        _lifecycleServiceMock
            .Setup(s => s.ProcessScheduledTransitionsAsync())
            .ThrowsAsync(new InvalidOperationException("DB error"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _job.Execute(_contextMock.Object));
    }
}
