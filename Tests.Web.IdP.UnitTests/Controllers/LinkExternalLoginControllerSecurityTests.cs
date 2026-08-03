using System.Security.Claims;
using Core.Application;
using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Web.IdP.Controllers.Account;

namespace Tests.Web.IdP.UnitTests.Controllers;

public class LinkExternalLoginControllerSecurityTests
{
    [Fact]
    public async Task Callback_ProviderClaimsIncludeMfa_DoesNotChangeCurrentAuthenticationMethods()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "linked-user",
            IsActive = true
        };
        var expectedXsrf = user.Id.ToString();
        var externalInfo = new ExternalLoginInfo(
            new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "provider-user"),
                    new Claim(AuthConstants.ClaimTypes.Amr, AuthConstants.Amr.Mfa)
                ],
                "External")),
            "TestProvider",
            "provider-user",
            "Test Provider");

        var userStore = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
        userManager
            .Setup(manager => manager.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(user);
        userManager
            .Setup(manager => manager.AddLoginAsync(user, externalInfo))
            .ReturnsAsync(IdentityResult.Success);

        var signInManager = new Mock<SignInManager<ApplicationUser>>(
            userManager.Object,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            null,
            null,
            null,
            null);
        signInManager
            .Setup(manager => manager.GetExternalLoginInfoAsync(expectedXsrf))
            .ReturnsAsync(externalInfo);

        var loginService = new Mock<ILoginService>();
        loginService
            .Setup(service => service.CanLinkExternalLoginAsync(
                user,
                externalInfo.LoginProvider,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null));

        var authenticationService = new Mock<IAuthenticationService>();
        authenticationService
            .Setup(service => service.SignOutAsync(
                It.IsAny<HttpContext>(),
                IdentityConstants.ExternalScheme,
                It.IsAny<AuthenticationProperties>()))
            .Returns(Task.CompletedTask);
        var services = new ServiceCollection()
            .AddSingleton(authenticationService.Object)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())],
                IdentityConstants.ApplicationScheme))
        };

        var controller = new LinkExternalLoginController(
            signInManager.Object,
            userManager.Object,
            Mock.Of<ILogger<LinkExternalLoginController>>(),
            loginService.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var result = await controller.Callback();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/Account/Profile?success=LinkAdded", redirect.Url);
        signInManager.Verify(
            manager => manager.GetExternalLoginInfoAsync(expectedXsrf),
            Times.Once);
        loginService.Verify(
            service => service.CanLinkExternalLoginAsync(
                user,
                externalInfo.LoginProvider,
                It.IsAny<CancellationToken>()),
            Times.Once);
        userManager.Verify(
            manager => manager.AddLoginAsync(user, externalInfo),
            Times.Once);
        signInManager.Verify(
            manager => manager.RefreshSignInAsync(It.IsAny<ApplicationUser>()),
            Times.Never);
        signInManager.Verify(
            manager => manager.SignInWithClaimsAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<bool>(),
                It.IsAny<IEnumerable<Claim>>()),
            Times.Never);
        authenticationService.Verify(
            service => service.SignOutAsync(
                httpContext,
                IdentityConstants.ExternalScheme,
                It.IsAny<AuthenticationProperties>()),
            Times.Once);
    }

    [Fact]
    public async Task Callback_ExternalInfoMissingForExpectedXsrf_RedirectsWithoutLinking()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "linked-user",
            IsActive = true
        };
        var expectedXsrf = user.Id.ToString();

        var userStore = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
        userManager
            .Setup(manager => manager.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(user);

        var signInManager = new Mock<SignInManager<ApplicationUser>>(
            userManager.Object,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            null,
            null,
            null,
            null);
        signInManager
            .Setup(manager => manager.GetExternalLoginInfoAsync(expectedXsrf))
            .ReturnsAsync((ExternalLoginInfo?)null);

        var loginService = new Mock<ILoginService>();
        var authenticationService = new Mock<IAuthenticationService>();
        var services = new ServiceCollection()
            .AddSingleton(authenticationService.Object)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())],
                IdentityConstants.ApplicationScheme))
        };

        var controller = new LinkExternalLoginController(
            signInManager.Object,
            userManager.Object,
            Mock.Of<ILogger<LinkExternalLoginController>>(),
            loginService.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var result = await controller.Callback();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/Account/Profile?error=ExternalLoginFailed", redirect.Url);
        signInManager.Verify(
            manager => manager.GetExternalLoginInfoAsync(expectedXsrf),
            Times.Once);
        loginService.Verify(
            service => service.CanLinkExternalLoginAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        userManager.Verify(
            manager => manager.AddLoginAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<UserLoginInfo>()),
            Times.Never);
        authenticationService.Verify(
            service => service.SignOutAsync(
                httpContext,
                IdentityConstants.ExternalScheme,
                It.IsAny<AuthenticationProperties>()),
            Times.Never);
    }

    [Fact]
    public async Task Callback_CurrentUserMissing_RedirectsWithoutLinking()
    {
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
        userManager
            .Setup(manager => manager.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((ApplicationUser?)null);

        var signInManager = new Mock<SignInManager<ApplicationUser>>(
            userManager.Object,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            null,
            null,
            null,
            null);
        var loginService = new Mock<ILoginService>();
        var authenticationService = new Mock<IAuthenticationService>();
        var services = new ServiceCollection()
            .AddSingleton(authenticationService.Object)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
                IdentityConstants.ApplicationScheme))
        };

        var controller = new LinkExternalLoginController(
            signInManager.Object,
            userManager.Object,
            Mock.Of<ILogger<LinkExternalLoginController>>(),
            loginService.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var result = await controller.Callback();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/", redirect.Url);
        signInManager.Verify(
            manager => manager.GetExternalLoginInfoAsync(It.IsAny<string>()),
            Times.Never);
        loginService.Verify(
            service => service.CanLinkExternalLoginAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        userManager.Verify(
            manager => manager.AddLoginAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<UserLoginInfo>()),
            Times.Never);
        authenticationService.Verify(
            service => service.SignOutAsync(
                httpContext,
                IdentityConstants.ExternalScheme,
                It.IsAny<AuthenticationProperties>()),
            Times.Never);
    }
}
