using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;
using System.Security.Claims;
using Xunit;
using Core.Application.Options;

namespace Tests.Infrastructure.UnitTests;

public class IpWhitelistAuthorizationHandlerTests
{
    private readonly Mock<IHttpContextAccessor> _mockHttpContextAccessor;
    private readonly Mock<IOptionsMonitor<ObservabilityOptions>> _mockOptions;
    private readonly Mock<ILogger<IpWhitelistAuthorizationHandler>> _mockLogger;
    private IpWhitelistAuthorizationHandler _handler = null!;

    public IpWhitelistAuthorizationHandlerTests()
    {
        _mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
        _mockOptions = new Mock<IOptionsMonitor<ObservabilityOptions>>();
        _mockLogger = new Mock<ILogger<IpWhitelistAuthorizationHandler>>();
    }

    private void SetupAllowedIPs(params string[] allowedIPs)
    {
        var options = new ObservabilityOptions { AllowedIPs = allowedIPs };
        _mockOptions.Setup(x => x.CurrentValue).Returns(options);
        
        _handler = new IpWhitelistAuthorizationHandler(
            _mockHttpContextAccessor.Object,
            _mockOptions.Object,
            _mockLogger.Object);
    }

    #region Deny Scenarios (非白名單 IP 應被拒絕)

    [Fact]
    public async Task HandleRequirementAsync_ShouldDeny_WhenHttpContextIsNull()
    {
        // Arrange
        SetupAllowedIPs("127.0.0.1");
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns((HttpContext?)null);
        
        var requirement = new IpWhitelistRequirement();
        var user = new ClaimsPrincipal();
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldDeny_WhenRemoteIpIsNull()
    {
        // Arrange
        SetupAllowedIPs("127.0.0.1");
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = null;
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        
        var requirement = new IpWhitelistRequirement();
        var user = new ClaimsPrincipal();
        var authContext = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(authContext);

        // Assert
        Assert.False(authContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldDeny_WhenIpNotInWhitelist()
    {
        // Arrange
        SetupAllowedIPs("127.0.0.1", "::1");
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.1"); // Non-whitelisted public IP
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        
        var requirement = new IpWhitelistRequirement();
        var user = new ClaimsPrincipal();
        var authContext = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(authContext);

        // Assert
        Assert.False(authContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldDeny_WhenIpNotInCidrRange()
    {
        // Arrange
        SetupAllowedIPs("10.0.0.0/8", "192.168.0.0/16");
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("11.0.0.1"); // Outside 10.0.0.0/8
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        
        var requirement = new IpWhitelistRequirement();
        var user = new ClaimsPrincipal();
        var authContext = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(authContext);

        // Assert
        Assert.False(authContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldDeny_WhenIpv6NotInWhitelist()
    {
        // Arrange
        SetupAllowedIPs("::1", "fe80::/10");
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("2001:db8::1"); // Non-whitelisted IPv6
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        
        var requirement = new IpWhitelistRequirement();
        var user = new ClaimsPrincipal();
        var authContext = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(authContext);

        // Assert
        Assert.False(authContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldDeny_WhenInvalidCidrNotation()
    {
        // Arrange
        SetupAllowedIPs("10.0.0.0/invalid", "192.168.0.0");
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        
        var requirement = new IpWhitelistRequirement();
        var user = new ClaimsPrincipal();
        var authContext = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(authContext);

        // Assert
        // Should not succeed since "10.0.0.0/invalid" is invalid CIDR
        // and 10.0.0.1 doesn't match exact IP 192.168.0.0
        Assert.False(authContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldDeny_WhenEmptyWhitelist()
    {
        // Arrange
        SetupAllowedIPs();
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        
        var requirement = new IpWhitelistRequirement();
        var user = new ClaimsPrincipal();
        var authContext = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(authContext);

        // Assert
        Assert.False(authContext.HasSucceeded);
    }

    #endregion

    #region Allow Scenarios (白名單 IP 應被允許)

    [Fact]
    public async Task HandleRequirementAsync_ShouldAllow_WhenIpv4LocalhostInWhitelist()
    {
        // Arrange
        SetupAllowedIPs("127.0.0.1", "::1");
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        
        var requirement = new IpWhitelistRequirement();
        var user = new ClaimsPrincipal();
        var authContext = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(authContext);

        // Assert
        Assert.True(authContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldAllow_WhenIpv6LocalhostInWhitelist()
    {
        // Arrange
        SetupAllowedIPs("127.0.0.1", "::1");
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.IPv6Loopback; // ::1
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        
        var requirement = new IpWhitelistRequirement();
        var user = new ClaimsPrincipal();
        var authContext = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(authContext);

        // Assert
        Assert.True(authContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldAllow_WhenIpInCidrRange_10_0_0_0_Slash8()
    {
        // Arrange
        SetupAllowedIPs("10.0.0.0/8");
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.123.45.67");
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        
        var requirement = new IpWhitelistRequirement();
        var user = new ClaimsPrincipal();
        var authContext = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(authContext);

        // Assert
        Assert.True(authContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldAllow_WhenIpInCidrRange_192_168_0_0_Slash16()
    {
        // Arrange
        SetupAllowedIPs("192.168.0.0/16");
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.100");
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        
        var requirement = new IpWhitelistRequirement();
        var user = new ClaimsPrincipal();
        var authContext = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(authContext);

        // Assert
        Assert.True(authContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldAllow_WhenIpInCidrRange_172_16_0_0_Slash12()
    {
        // Arrange
        SetupAllowedIPs("172.16.0.0/12");
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("172.20.10.5");
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        
        var requirement = new IpWhitelistRequirement();
        var user = new ClaimsPrincipal();
        var authContext = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(authContext);

        // Assert
        Assert.True(authContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldAllow_WhenIpv4MappedToIpv6MatchesIpv4Whitelist()
    {
        // Arrange
        SetupAllowedIPs("127.0.0.1");
        var httpContext = new DefaultHttpContext();
        // IPv4-mapped IPv6 address: ::ffff:127.0.0.1
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:127.0.0.1");
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        
        var requirement = new IpWhitelistRequirement();
        var user = new ClaimsPrincipal();
        var authContext = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(authContext);

        // Assert
        Assert.True(authContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldAllow_WhenExactIpMatch()
    {
        // Arrange
        SetupAllowedIPs("203.0.113.42", "203.0.113.43");
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.42");
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        
        var requirement = new IpWhitelistRequirement();
        var user = new ClaimsPrincipal();
        var authContext = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(authContext);

        // Assert
        Assert.True(authContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldAllow_WhenIpMatchesAnyWhitelistEntry()
    {
        // Arrange
        SetupAllowedIPs("127.0.0.1", "::1", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16");
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.168.5.10");
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        
        var requirement = new IpWhitelistRequirement();
        var user = new ClaimsPrincipal();
        var authContext = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(authContext);

        // Assert
        Assert.True(authContext.HasSucceeded);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task HandleRequirementAsync_ShouldDeny_WhenCidrPrefixLengthTooLarge()
    {
        // Arrange
        SetupAllowedIPs("10.0.0.0/33"); // Invalid: max is 32 for IPv4
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        
        var requirement = new IpWhitelistRequirement();
        var user = new ClaimsPrincipal();
        var authContext = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(authContext);

        // Assert
        Assert.False(authContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldDeny_WhenCidrPrefixLengthNegative()
    {
        // Arrange
        SetupAllowedIPs("10.0.0.0/-1");
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        
        var requirement = new IpWhitelistRequirement();
        var user = new ClaimsPrincipal();
        var authContext = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(authContext);

        // Assert
        Assert.False(authContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldDeny_WhenIpv4AddressComparedToIpv6Cidr()
    {
        // Arrange
        SetupAllowedIPs("fe80::/10"); // IPv6 CIDR
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.1"); // IPv4
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        
        var requirement = new IpWhitelistRequirement();
        var user = new ClaimsPrincipal();
        var authContext = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(authContext);

        // Assert
        Assert.False(authContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldAllow_WhenCidrSlash32MatchesExactIp()
    {
        // Arrange
        SetupAllowedIPs("192.168.1.100/32"); // Exact match
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.100");
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        
        var requirement = new IpWhitelistRequirement();
        var user = new ClaimsPrincipal();
        var authContext = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(authContext);

        // Assert
        Assert.True(authContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldDeny_WhenCidrSlash32DoesNotMatchExactIp()
    {
        // Arrange
        SetupAllowedIPs("192.168.1.100/32");
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.101");
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        
        var requirement = new IpWhitelistRequirement();
        var user = new ClaimsPrincipal();
        var authContext = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(authContext);

        // Assert
        Assert.False(authContext.HasSucceeded);
    }

    #endregion
}
