using Web.IdP.Services;
using Xunit;
using Core.Application; // Added

namespace Tests.Web.IdP.UnitTests.Services;

public class TurnstileStateServiceTests
{
    [Fact]
    public void IsAvailable_ShouldBeTrueByDefault()
    {
        // Arrange
        var service = new TurnstileStateService();

        // Act & Assert
        Assert.True(service.IsAvailable);
    }

    [Fact]
    public void SetAvailable_ShouldUpdateState()
    {
        // Arrange
        var service = new TurnstileStateService();

        // Act
        service.SetAvailable(false);

        // Assert
        Assert.False(service.IsAvailable);

        // Act
        service.SetAvailable(true);

        // Assert
        Assert.True(service.IsAvailable);
    }
}
