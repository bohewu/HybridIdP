using Microsoft.Extensions.Configuration;
using Web.IdP.Options;
using Xunit;

namespace Tests.Application.UnitTests.Configuration;

public class TokenOptionsTests
{
    [Fact]
    public void TokenOptions_Should_Bind_From_Configuration()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string> {
            {"TokenOptions:AccessTokenLifetimeMinutes", "120"},
            {"TokenOptions:RefreshTokenLifetimeMinutes", "43200"},
            {"TokenOptions:DeviceCodeLifetimeMinutes", "15"}
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings!)
            .Build();

        // Act
        var options = new Web.IdP.Options.TokenOptions();
        configuration.GetSection(Web.IdP.Options.TokenOptions.SectionName).Bind(options);

        // Assert
        Assert.Equal(120, options.AccessTokenLifetimeMinutes);
        Assert.Equal(43200, options.RefreshTokenLifetimeMinutes); // 30 days
        Assert.Equal(15, options.DeviceCodeLifetimeMinutes);
    }

    [Fact]
    public void TokenOptions_Should_Have_Correct_Defaults()
    {
        // Arrange & Act
        var options = new Web.IdP.Options.TokenOptions();

        // Assert
        Assert.Equal(60, options.AccessTokenLifetimeMinutes);
        Assert.Equal(20160, options.RefreshTokenLifetimeMinutes); // 14 days
        Assert.Equal(30, options.DeviceCodeLifetimeMinutes);
    }
}
