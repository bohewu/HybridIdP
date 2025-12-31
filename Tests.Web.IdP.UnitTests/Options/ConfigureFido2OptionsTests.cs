using Fido2NetLib;
using Microsoft.Extensions.Configuration;
using Web.IdP.Options;
using Xunit;

namespace Tests.Web.IdP.UnitTests.Options;

public class ConfigureFido2OptionsTests
{
    private readonly ConfigureFido2Options _sut;
    private readonly IConfiguration _configuration;

    public ConfigureFido2OptionsTests()
    {
        // Use in-memory collection for mocking configuration
        var inMemorySettings = new Dictionary<string, string?>();
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
        
        _sut = new ConfigureFido2Options(_configuration);
    }

    [Fact]
    public void PostConfigure_ShouldFallbackToBrandingAppName_WhenServerNameMissing()
    {
        // Arrange
        var options = new Fido2Configuration { ServerName = null };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "Branding:AppName", "MyBrandingApp" }
        }).Build();
        
        var sut = new ConfigureFido2Options(config);

        // Act
        sut.PostConfigure(null, options);

        // Assert
        Assert.Equal("MyBrandingApp", options.ServerName);
    }

    [Fact]
    public void PostConfigure_ShouldUseFido2ServerName_WhenPresent()
    {
        // Arrange
        var options = new Fido2Configuration { ServerName = null };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "Fido2:ServerName", "MyFidoApp" },
            { "Branding:AppName", "MyBrandingApp" }
        }).Build();
        
        var sut = new ConfigureFido2Options(config);

        // Act
        sut.PostConfigure(null, options);

        // Assert
        Assert.Equal("MyFidoApp", options.ServerName);
    }

    [Fact]
    public void PostConfigure_ShouldUseDefaultHybridIdP_WhenNoConfigPresent()
    {
        // Arrange
        var options = new Fido2Configuration { ServerName = null };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var sut = new ConfigureFido2Options(config);

        // Act
        sut.PostConfigure(null, options);

        // Assert
        Assert.Equal("HybridIdP", options.ServerName);
    }

    [Fact]
    public void PostConfigure_ShouldParseSingleOrigin_Correctly()
    {
        // Arrange
        var options = new Fido2Configuration();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "Fido2:Origins", "https://id.ncut.edu.tw" }
        }).Build();
        var sut = new ConfigureFido2Options(config);

        // Act
        sut.PostConfigure(null, options);

        // Assert
        Assert.NotNull(options.Origins);
        Assert.Single(options.Origins);
        Assert.Contains("https://id.ncut.edu.tw", options.Origins);
    }

    [Fact]
    public void PostConfigure_ShouldParseCommaSeparatedOrigins_Correctly()
    {
        // Arrange
        var options = new Fido2Configuration();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "Fido2:Origins", "https://a.com, https://b.com ,https://c.com" }
        }).Build();
        var sut = new ConfigureFido2Options(config);

        // Act
        sut.PostConfigure(null, options);

        // Assert
        Assert.NotNull(options.Origins);
        Assert.Equal(3, options.Origins.Count);
        Assert.Contains("https://a.com", options.Origins);
        Assert.Contains("https://b.com", options.Origins);
        Assert.Contains("https://c.com", options.Origins);
    }
}
