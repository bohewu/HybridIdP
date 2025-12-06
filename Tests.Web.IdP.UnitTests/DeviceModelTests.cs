using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Web.IdP.Pages.Connect;
using Core.Domain;
using Xunit;

namespace Tests.Web.IdP.UnitTests;

public class DeviceModelTests
{
    private readonly Mock<IOpenIddictScopeManager> _scopeManagerMock;
    private readonly Mock<IOpenIddictApplicationManager> _appManagerMock;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<IAuthenticationService> _authServiceMock;
    private readonly Mock<IStringLocalizer<DeviceModel>> _localizerMock;
    private readonly Mock<ILogger<DeviceModel>> _loggerMock;
    private readonly DeviceModel _sut;

    public DeviceModelTests()
    {
        _scopeManagerMock = new Mock<IOpenIddictScopeManager>();
        _appManagerMock = new Mock<IOpenIddictApplicationManager>();
        _authServiceMock = new Mock<IAuthenticationService>();
        _localizerMock = new Mock<IStringLocalizer<DeviceModel>>();
        _loggerMock = new Mock<ILogger<DeviceModel>>();
        
        // UserManager requires a mock IUserStore
        var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        // Setup localizer to return key as value (for testing)
        _localizerMock.Setup(l => l[It.IsAny<string>()])
            .Returns<string>(key => new LocalizedString(key, key));

        _sut = new DeviceModel(
            _scopeManagerMock.Object,
            _appManagerMock.Object,
            _userManagerMock.Object,
            _localizerMock.Object,
            _loggerMock.Object
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

        // 2. Mock User - setup ClaimsPrincipal on HttpContext
        var userIdentity = new ClaimsIdentity("Cookies");
        userIdentity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-123"));
        userIdentity.AddClaim(new Claim(ClaimTypes.Name, "testuser"));
        _sut.PageContext.HttpContext.User = new ClaimsPrincipal(userIdentity);
        
        // 3. Mock UserManager
        var testUser = new ApplicationUser { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), UserName = "testuser", Email = "test@example.com" };
        _userManagerMock.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(testUser);
        _userManagerMock.Setup(m => m.GetUserIdAsync(testUser))
            .ReturnsAsync("user-123");
        _userManagerMock.Setup(m => m.GetEmailAsync(testUser))
            .ReturnsAsync("test@example.com");
        _userManagerMock.Setup(m => m.GetUserNameAsync(testUser))
            .ReturnsAsync("testuser");
        _userManagerMock.Setup(m => m.GetRolesAsync(testUser))
            .ReturnsAsync(new List<string> { "User" });

        // 4. Mock Scope Manager
        _scopeManagerMock.Setup(m => m.ListResourcesAsync(It.IsAny<System.Collections.Immutable.ImmutableArray<string>>(), It.IsAny<CancellationToken>()))
            .Returns(ConfiguredAsyncEnumerable(new[] { "resource1" }));

        // Act
        var result = await _sut.OnPostAsync();

        // Assert
        var signInResult = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
        Assert.Equal(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, signInResult.AuthenticationScheme);
        Assert.NotNull(signInResult.Properties);
        Assert.Equal("/", signInResult.Properties.RedirectUri); // Verify RedirectUri fix
    }

    [Fact]
    public async Task OnPostAsync_WithInvalidUserCode_ReturnsPageWithError()
    {
        // Arrange
        _sut.UserCode = "invalid-code";

        // 1. Mock Device Request Authentication - returns failure
        _authServiceMock
            .Setup(s => s.AuthenticateAsync(
                It.IsAny<HttpContext>(), 
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme))
            .ReturnsAsync(AuthenticateResult.Fail("Invalid user code"));

        // 2. Mock User
        var userIdentity = new ClaimsIdentity("Cookies");
        userIdentity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-123"));
        _sut.PageContext.HttpContext.User = new ClaimsPrincipal(userIdentity);
        
        var testUser = new ApplicationUser { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), UserName = "testuser" };
        _userManagerMock.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(testUser);

        // Act
        var result = await _sut.OnPostAsync();

        // Assert
        var pageResult = Assert.IsType<PageResult>(result);
        Assert.Equal("invalid_token", _sut.Error);
        Assert.Equal("InvalidUserCode", _sut.ErrorDescription); // Uses localization key as value in test
    }

    private static IAsyncEnumerable<T> ConfiguredAsyncEnumerable<T>(IEnumerable<T> enumerable)
    {
        return enumerable.ToAsyncEnumerable();
    }
}
