using Infrastructure.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests.Infrastructure.UnitTests.Configuration;

public class ForwardedHeadersHelperTests
{
    [Theory]
    [InlineData("203.0.113.1, 192.0.2.10", "10.1.0.2", 2, "203.0.113.1")]
    [InlineData("203.0.113.2, 192.0.2.10", "10.1.0.2", 2, "203.0.113.2")]
    [InlineData("198.51.100.9, 203.0.113.1, 192.0.2.10", "10.1.0.2", 2, "203.0.113.1")]
    [InlineData("127.0.0.1, 192.0.2.10", "198.51.100.20", 2, "198.51.100.20")]
    [InlineData("203.0.113.1", "10.1.0.2", 1, "203.0.113.1")]
    public async Task ForwardedChain_ShouldResolveOnlyTrustedHops(string header, string peer, int limit, string expected)
    {
        var options = CreateOptions();
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = limit;
        ForwardedHeadersHelper.ConfigureKnownNetworks(options, "10.1.0.2;192.0.2.10");
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Headers["X-Forwarded-For"] = header;
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options));

        await middleware.Invoke(context);

        Assert.Equal(IPAddress.Parse(expected), context.Connection.RemoteIpAddress);
    }

    public ForwardedHeadersHelperTests()
    {
    }

    private ForwardedHeadersOptions CreateOptions() 
    {
        var options = new ForwardedHeadersOptions();
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        return options;
    }

    [Fact]
    public void ConfigureKnownNetworks_ShouldParseCidrCorrectly()
    {
        // Arrange
        var options = CreateOptions();
        var config = "10.0.0.0/8; 172.16.0.0/12";

        // Act
        ForwardedHeadersHelper.ConfigureKnownNetworks(options, config);

        // Assert
        Assert.Equal(2, options.KnownIPNetworks.Count);
        
        // 10.0.0.0/8
        // 10.0.0.0/8
        var net1 = options.KnownIPNetworks.Single(n => n.BaseAddress.ToString() == "10.0.0.0" && n.PrefixLength == 8);

        // 172.16.0.0/12
        var net2 = options.KnownIPNetworks.Single(n => n.BaseAddress.ToString() == "172.16.0.0" && n.PrefixLength == 12);
    }

    [Fact]
    public void ConfigureKnownNetworks_ShouldParseSingleIPCorrectly()
    {
        // Arrange
        var options = CreateOptions();
        var config = "192.168.1.10";

        // Act
        ForwardedHeadersHelper.ConfigureKnownNetworks(options, config);

        // Assert
        Assert.Single(options.KnownProxies);
        Assert.Equal("192.168.1.10", options.KnownProxies[0].ToString());
    }

    [Fact]
    public void ConfigureKnownNetworks_ShouldHandleMixedInput()
    {
        // Arrange
        var options = CreateOptions();
        var config = "192.168.1.10; 10.0.0.0/8";

        // Act
        ForwardedHeadersHelper.ConfigureKnownNetworks(options, config);

        // Assert
        // Should have 1 IP and 1 Network
        Assert.Single(options.KnownProxies);
        Assert.Equal("192.168.1.10", options.KnownProxies[0].ToString());

        Assert.Single(options.KnownIPNetworks);
        var net = options.KnownIPNetworks[0];
        Assert.Equal("10.0.0.0", net.BaseAddress.ToString());
        Assert.Equal(8, net.PrefixLength);
    }

    [Fact]
    public void ConfigureKnownNetworks_ShouldIgnoreInvalidEntries()
    {
        // Arrange
        var options = CreateOptions();
        var config = "invalid-ip; 999.999.999.999; 10.0.0.0/invalid";

        // Act
        ForwardedHeadersHelper.ConfigureKnownNetworks(options, config);

        // Assert
        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }
    
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void ConfigureKnownNetworks_ShouldHandleEmptyInput(string? input)
    {
        // Arrange
        var options = CreateOptions();

        // Act
        ForwardedHeadersHelper.ConfigureKnownNetworks(options, input);

        // Assert
        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }
}
