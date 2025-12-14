using System.Collections.Immutable;
using System.Security.Claims;
using Core.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Web.IdP.Services;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tests.Application.UnitTests;

public class DeviceFlowServiceTests
{
    private readonly Mock<IOpenIddictScopeManager> _mockScopeManager;
    private readonly Mock<IOpenIddictApplicationManager> _mockApplicationManager;
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<IStringLocalizer<DeviceFlowService>> _mockLocalizer;
    private readonly Mock<ILogger<DeviceFlowService>> _mockLogger;
    private readonly Mock<IClaimsEnrichmentService> _mockClaimsEnricher;
    private readonly DeviceFlowService _service;

    public DeviceFlowServiceTests()
    {
        _mockScopeManager = new Mock<IOpenIddictScopeManager>();
        _mockApplicationManager = new Mock<IOpenIddictApplicationManager>();
        _mockUserManager = MockUserManager<ApplicationUser>();
        _mockLocalizer = new Mock<IStringLocalizer<DeviceFlowService>>();
        _mockLogger = new Mock<ILogger<DeviceFlowService>>();
        _mockClaimsEnricher = new Mock<IClaimsEnrichmentService>();

        _mockLocalizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));

        _mockClaimsEnricher.Setup(x => x.AddScopeMappedClaimsAsync(It.IsAny<ClaimsIdentity>(), It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
            .Returns(Task.CompletedTask);
        _mockClaimsEnricher.Setup(x => x.AddPermissionClaimsAsync(It.IsAny<ClaimsIdentity>(), It.IsAny<ApplicationUser>()))
            .Returns(Task.CompletedTask);

        _service = new DeviceFlowService(
            _mockScopeManager.Object,
            _mockApplicationManager.Object,
            _mockUserManager.Object,
            _mockLocalizer.Object,
            _mockLogger.Object,
            _mockClaimsEnricher.Object);
    }

    private static Mock<UserManager<TUser>> MockUserManager<TUser>() where TUser : class
    {
        var store = new Mock<IUserStore<TUser>>();
        return new Mock<UserManager<TUser>>(store.Object, null, null, null, null, null, null, null, null);
    }

    [Fact]
    public async Task PrepareVerificationViewModelAsync_ReturnsError_WhenClientNotFound()
    {
        // Arrange
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(Claims.ClientId, "test-client")
        }, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));
        
        var authResult = AuthenticateResult.Success(new AuthenticationTicket(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));

        _mockApplicationManager.Setup(m => m.FindByClientIdAsync("test-client", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Act
        var result = await _service.PrepareVerificationViewModelAsync(authResult);

        // Assert
        Assert.Equal(Errors.InvalidClient, result.Error);
        Assert.Equal("InvalidClient", result.ErrorDescription);
    }

    /*
    [Fact]
    public async Task PrepareVerificationViewModelAsync_ReturnsViewModel_WhenValidRequest()
    {
        // ...
        // Assert
        // Assert.Equal("openid profile", result.Scope);
        // ...
    }
    */

    [Fact]
    public async Task ProcessVerificationAsync_ReturnsError_WhenUserNotFound()
    {
        // Arrange
        var userPrincipal = new ClaimsPrincipal(new ClaimsIdentity("test"));
        _mockUserManager.Setup(m => m.GetUserAsync(userPrincipal)).ReturnsAsync((ApplicationUser)null!);

        var authResult = AuthenticateResult.NoResult();

        // Act
        var result = await _service.ProcessVerificationAsync(userPrincipal, authResult);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var vm = Assert.IsType<DeviceVerificationViewModel>(badRequest.Value);
        Assert.Equal(Errors.ServerError, vm.Error);
    }

    [Fact]
    public async Task ProcessVerificationAsync_ReturnsSignInResult_WhenValid()
    {
        // Arrange
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "testuser", Email = "test@test.com" };
        var userPrincipal = new ClaimsPrincipal(new ClaimsIdentity("test"));

        _mockUserManager.Setup(m => m.GetUserAsync(userPrincipal)).ReturnsAsync(user);
        _mockUserManager.Setup(m => m.GetUserIdAsync(user)).ReturnsAsync(user.Id.ToString());
        _mockUserManager.Setup(m => m.GetEmailAsync(user)).ReturnsAsync(user.Email);
        _mockUserManager.Setup(m => m.GetUserNameAsync(user)).ReturnsAsync(user.UserName);
        _mockUserManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(new List<string>());

        var devicePrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(Claims.ClientId, "test-client"),
            new Claim(Claims.Scope, "openid")
        }, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));

        var authResult = AuthenticateResult.Success(new AuthenticationTicket(devicePrincipal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));

        _mockScopeManager.Setup(m => m.ListResourcesAsync(It.IsAny<ImmutableArray<string>>(), It.IsAny<CancellationToken>()))
            .Returns(new List<string>().ToAsyncEnumerable());

        // Act
        var result = await _service.ProcessVerificationAsync(userPrincipal, authResult);

        // Assert
        var signInResult = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
        Assert.Equal(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, signInResult.AuthenticationScheme);
        Assert.NotNull(signInResult.Principal);
        Assert.True(signInResult.Principal.HasClaim(c => c.Type == Claims.Subject && c.Value == user.Id.ToString()));
    }
}
