using Core.Application.Options;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Tests.Infrastructure.IntegrationTests;

/// <summary>
/// Integration tests for RateLimiting configuration.
/// Verifies that RateLimitingOptions correctly binds from configuration.
/// </summary>
public class RateLimitingOptionsIntegrationTests
{
    [Fact]
    public void RateLimitingOptions_DefaultValues_AreCorrect()
    {
        // Arrange
        var options = new RateLimitingOptions();

        // Assert - verify default values match expected settings
        Assert.True(options.Enabled);
        Assert.Equal(5, options.LoginPermitLimit);
        Assert.Equal(60, options.LoginWindowSeconds);
        Assert.Equal(10, options.TokenPermitLimit);
        Assert.Equal(60, options.TokenWindowSeconds);
        Assert.Equal(100, options.AdminApiPermitLimit);
        Assert.Equal(60, options.AdminApiWindowSeconds);
        Assert.Equal(2, options.QueueLimit);
    }

    [Fact]
    public void RateLimitingOptions_BindsFromConfiguration()
    {
        // Arrange
        var configDict = new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:LoginPermitLimit"] = "10",
            ["RateLimiting:LoginWindowSeconds"] = "120",
            ["RateLimiting:TokenPermitLimit"] = "20",
            ["RateLimiting:TokenWindowSeconds"] = "30",
            ["RateLimiting:AdminApiPermitLimit"] = "200",
            ["RateLimiting:AdminApiWindowSeconds"] = "60",
            ["RateLimiting:QueueLimit"] = "5"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        var options = new RateLimitingOptions();
        configuration.GetSection(RateLimitingOptions.Section).Bind(options);

        // Assert
        Assert.True(options.Enabled);
        Assert.Equal(10, options.LoginPermitLimit);
        Assert.Equal(120, options.LoginWindowSeconds);
        Assert.Equal(20, options.TokenPermitLimit);
        Assert.Equal(30, options.TokenWindowSeconds);
        Assert.Equal(200, options.AdminApiPermitLimit);
        Assert.Equal(60, options.AdminApiWindowSeconds);
        Assert.Equal(5, options.QueueLimit);
    }

    [Fact]
    public void RateLimitingOptions_DisabledByConfiguration()
    {
        // Arrange
        var configDict = new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "false"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        var options = new RateLimitingOptions();
        configuration.GetSection(RateLimitingOptions.Section).Bind(options);

        // Assert
        Assert.False(options.Enabled);
    }
}
