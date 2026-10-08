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
using Web.IdP.Services;
using Web.IdP.Helpers;
using Tests.Web.IdP.UnitTests.TestSupport;
using Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Tests.Web.IdP.UnitTests.Controllers;

public class LinkExternalLoginControllerSecurityTests
{
    [Theory]
    [InlineData(ExternalSignInCompletionStatus.Succeeded, false)]
    [InlineData(ExternalSignInCompletionStatus.Succeeded, true)]
    [InlineData(ExternalSignInCompletionStatus.TotpRequired, true)]
    [InlineData(ExternalSignInCompletionStatus.EmailOtpRequired, true)]
    [InlineData(ExternalSignInCompletionStatus.MfaEnrollmentRequired, true)]
    [InlineData(ExternalSignInCompletionStatus.PasskeyRequired, true)]
    public async Task Callback_ProviderClaimsIncludeMfa_RequiresFreshCorrelatedOperationProof(ExternalSignInCompletionStatus status, bool fresh)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "linked-user",
            SecurityStamp = "existing",
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
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        using var services = new ServiceCollection()
            .AddSingleton(authenticationService.Object)
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton(userManager.Object)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(userManager.Object.Options.ClaimsIdentity.SecurityStampClaimType, user.SecurityStamp)],
                IdentityConstants.ApplicationScheme))
        };
        httpContext.Session = new MemorySession();
        authenticationService.Setup(service => service.AuthenticateAsync(httpContext, IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(httpContext.User, IdentityConstants.ApplicationScheme)));
        if (fresh)
        {
            var nonce = await AccountSecurityOperationSession.BeginAsync(httpContext, user, AccountSecurityOperationSession.ExternalLinkPurpose, externalInfo.LoginProvider);
            AccountSecurityOperationSession.MarkVerified(httpContext, user, "password");
            externalInfo.AuthenticationProperties = new AuthenticationProperties();
            externalInfo.AuthenticationProperties.Items[AccountSecurityOperationSession.CorrelationProperty] = nonce;
        }

        var coordinator = new Mock<IExternalSignInCoordinator>();
        coordinator.Setup(x => x.LinkAsync(httpContext, user, externalInfo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalSignInCompletionResult(status));
        var controller = new LinkExternalLoginController(
            signInManager.Object,
            userManager.Object,
            Mock.Of<ILogger<LinkExternalLoginController>>(),
            loginService.Object, coordinator.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var result = await controller.Callback();

        if (!fresh)
            Assert.Equal("/Account/Profile?error=FreshAuthenticationRequired", Assert.IsType<RedirectResult>(result).Url);
        else if (status == ExternalSignInCompletionStatus.Succeeded)
            Assert.Equal("/Account/Profile?success=LinkAdded", Assert.IsType<RedirectResult>(result).Url);
        else Assert.IsType<RedirectToPageResult>(result);
        signInManager.Verify(
            manager => manager.GetExternalLoginInfoAsync(expectedXsrf),
            Times.Once);
        loginService.Verify(
            service => service.CanLinkExternalLoginAsync(
                user,
                externalInfo.LoginProvider,
                It.IsAny<CancellationToken>()),
            fresh ? Times.Once() : Times.Never());
        userManager.Verify(
            manager => manager.AddLoginAsync(user, externalInfo),
            Times.Never);
        coordinator.Verify(x => x.LinkAsync(httpContext, user, externalInfo, It.IsAny<CancellationToken>()), fresh ? Times.Once() : Times.Never());
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
            !fresh || status == ExternalSignInCompletionStatus.Succeeded ? Times.Once() : Times.Never());
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
            loginService.Object, Mock.Of<IExternalSignInCoordinator>())
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
            loginService.Object, Mock.Of<IExternalSignInCoordinator>())
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
