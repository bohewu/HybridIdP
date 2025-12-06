using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Web.IdP.Pages.Connect;
using Xunit;

namespace Tests.Web.IdP.UnitTests;

public class DeviceModelTests
{
    private readonly Mock<IOpenIddictScopeManager> _scopeManagerMock;
    private readonly Mock<IOpenIddictApplicationManager> _appManagerMock;
    private readonly Mock<IOpenIddictAuthorizationManager> _authzManagerMock;
    private readonly Mock<IAuthenticationService> _authServiceMock;
    private readonly DeviceModel _sut;

    public DeviceModelTests()
    {
        _scopeManagerMock = new Mock<IOpenIddictScopeManager>();
        _appManagerMock = new Mock<IOpenIddictApplicationManager>();
        _authzManagerMock = new Mock<IOpenIddictAuthorizationManager>();
        _authServiceMock = new Mock<IAuthenticationService>();

        _sut = new DeviceModel(
            _scopeManagerMock.Object,
            _appManagerMock.Object,
            _authzManagerMock.Object
        );

        // Setup HttpContext with Mock AuthenticationService
        var services = new ServiceCollection();
        services.AddSingleton(_authServiceMock.Object);
        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = services.BuildServiceProvider();
        
        _sut.PageContext = new PageContext
        {
            HttpContext = httpContext
        };
    }

    [Fact]
    public async Task OnPostAsync_WithValidUserCode_ReturnsSignInResult_WithRedirect()
    {
        // Arrange
        _sut.UserCode = "valid-code";

        // 1. Mock Device Request Authentication (OpenIddict Middleware Result)
        var deviceIdentity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        deviceIdentity.AddClaim(OpenIddictConstants.Claims.ClientId, "device-client");
        deviceIdentity.AddClaim(OpenIddictConstants.Claims.Scope, "openid profile");
        
        var deviceTicket = new AuthenticationTicket(
            new ClaimsPrincipal(deviceIdentity), 
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        _authServiceMock
            .Setup(s => s.AuthenticateAsync(
                It.IsAny<HttpContext>(), 
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme))
            .ReturnsAsync(AuthenticateResult.Success(deviceTicket));

        // 2. Mock User Session Authentication (Cookies) - SIMULATING REAL ASP.NET IDENTITY PRINCIPAL
        var userIdentity = new ClaimsIdentity("Cookies");
        userIdentity.AddClaim(ClaimTypes.NameIdentifier, "user-123"); // Identity uses NameIdentifier, not "sub"
        userIdentity.AddClaim(ClaimTypes.Name, "testuser");
        
        var userTicket = new AuthenticationTicket(
            new ClaimsPrincipal(userIdentity), 
            "Cookies"); // Default scheme

        _authServiceMock
            .Setup(s => s.AuthenticateAsync(
                It.IsAny<HttpContext>(), 
                null)) // Default scheme
            .ReturnsAsync(AuthenticateResult.Success(userTicket));

        // 3. Mock Scope Manager
        _scopeManagerMock.Setup(m => m.ListResourcesAsync(It.IsAny<System.Collections.Immutable.ImmutableArray<string>>(), It.IsAny<CancellationToken>()))
            .Returns(ConfiguredAsyncEnumerable(new[] { "resource1" }));

        // Act
        var result = await _sut.OnPostAsync();

        // Assert
        var signInResult = Assert.IsType<SignInResult>(result);
        Assert.Equal(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, signInResult.AuthenticationScheme);
        Assert.NotNull(signInResult.Properties);
        Assert.Equal("/", signInResult.Properties.RedirectUri); // Verify RedirectUri fix
    }

    private static IAsyncEnumerable<T> ConfiguredAsyncEnumerable<T>(IEnumerable<T> enumerable)
    {
        return enumerable.ToAsyncEnumerable();
    }
}
