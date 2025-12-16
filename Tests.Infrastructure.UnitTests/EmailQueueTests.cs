using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Core.Domain.Models;
using Infrastructure.Services;
using System.Threading;
using System;

namespace Tests.Infrastructure.UnitTests;

public class EmailQueueTests
{
    [Fact]
    public async Task QueueEmailAsync_ShouldAddItem_And_DequeueAsync_ShouldRetrieveIt()
    {
        // Arrange
        var queue = new EmailQueue();
        var message = new EmailMessage { To = "test@example.com", Subject = "Test" };

        // Act
        await queue.QueueEmailAsync(message);
        var result = await queue.DequeueAsync(CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(message);
    }

    [Fact]
    public async Task Queue_ShouldBeFIFO()
    {
        // Arrange
        var queue = new EmailQueue();
        var msg1 = new EmailMessage { Subject = "1" };
        var msg2 = new EmailMessage { Subject = "2" };

        // Act
        await queue.QueueEmailAsync(msg1);
        await queue.QueueEmailAsync(msg2);

        var result1 = await queue.DequeueAsync(CancellationToken.None);
        var result2 = await queue.DequeueAsync(CancellationToken.None);

        // Assert
        result1.Subject.Should().Be("1");
        result2.Subject.Should().Be("2");
    }
}
