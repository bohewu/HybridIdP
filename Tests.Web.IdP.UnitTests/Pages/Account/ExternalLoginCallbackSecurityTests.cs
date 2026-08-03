using System.Security.Claims;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Options;
using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Web.IdP.Pages.Account;
using Web.IdP.Services;
using Xunit;

namespace Tests.Web.IdP.UnitTests.Pages.Account;

public class ExternalLoginCallbackSecurityTests
{
    [Theory]
    [InlineData(AuthConstants.Providers.Google, null)]
    [InlineData(AuthConstants.Providers.Google, "false")]
    [InlineData(AuthConstants.Providers.Microsoft, "false")]
    [InlineData("CustomProvider", "true")]
    public async Task OnGetAsync_UntrustedMatchingEmail_DoesNotAutoLinkOrSignIn(
        string provider,
        string? assuranceValue)
    {
        var info = CreateExternalLoginInfo(provider, assuranceValue);
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "existing-user" };
        var userManager = CreateUserManagerMock();
        userManager
            .Setup(manager => manager.FindByLoginAsync(info.LoginProvider, info.ProviderKey))
            .ReturnsAsync((ApplicationUser?)null);

        var signInManager = CreateSignInManagerMock(userManager);
        signInManager
            .Setup(manager => manager.GetExternalLoginInfoAsync(null))
            .ReturnsAsync(info);

        var externalSignInCoordinator = new Mock<IExternalSignInCoordinator>();
        var model = CreateModel(
            signInManager,
            userManager,
            new Mock<ILoginService>(),
            externalSignInCoordinator,
            autoLinkMatchingEmail: true);

        var result = await model.OnGetAsync("/");

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("./ExternalLoginConfirmation", redirect.PageName);
        userManager.Verify(manager => manager.FindByEmailAsync("matched@example.com"), Times.Never);
        userManager.Verify(manager => manager.AddLoginAsync(user, info), Times.Never);
        externalSignInCoordinator.Verify(
            service => service.CompleteAsync(
                It.IsAny<HttpContext>(),
                It.IsAny<ApplicationUser>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task OnGetAsync_TrustedGoogleMatchingEmail_AutoLinksAndCompletesSignIn()
    {
        var info = CreateExternalLoginInfo(AuthConstants.Providers.Google, "true");
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "existing-user" };
        var userManager = CreateUserManagerMock();
        userManager
            .Setup(manager => manager.FindByLoginAsync(info.LoginProvider, info.ProviderKey))
            .ReturnsAsync((ApplicationUser?)null);
        userManager
            .Setup(manager => manager.FindByEmailAsync("matched@example.com"))
            .ReturnsAsync(user);
        userManager
            .Setup(manager => manager.AddLoginAsync(user, info))
            .ReturnsAsync(IdentityResult.Success);

        var signInManager = CreateSignInManagerMock(userManager);
        signInManager
            .Setup(manager => manager.GetExternalLoginInfoAsync(null))
            .ReturnsAsync(info);
        var loginService = new Mock<ILoginService>();
        loginService
            .Setup(service => service.CanLinkExternalLoginAsync(user, info.LoginProvider, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null));
        var externalSignInCoordinator = new Mock<IExternalSignInCoordinator>();
        externalSignInCoordinator
            .Setup(service => service.CompleteAsync(
                It.IsAny<HttpContext>(),
                user,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExternalSignInCompletionResult.Succeeded());

        var model = CreateModel(
            signInManager,
            userManager,
            loginService,
            externalSignInCoordinator,
            autoLinkMatchingEmail: true);

        var result = await model.OnGetAsync("/");

        Assert.IsType<LocalRedirectResult>(result);
        userManager.Verify(manager => manager.FindByEmailAsync("matched@example.com"), Times.Once);
        userManager.Verify(manager => manager.AddLoginAsync(user, info), Times.Once);
        externalSignInCoordinator.Verify(
            service => service.CompleteAsync(
                It.IsAny<HttpContext>(),
                user,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("changed@example.com", "false")]
    public async Task OnGetAsync_ExistingProviderLink_SucceedsWithoutTrustedCurrentEmail(
        string? email,
        string? assuranceValue)
    {
        var info = CreateExternalLoginInfo(AuthConstants.Providers.Google, assuranceValue, email);
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "linked-user" };
        var userManager = CreateUserManagerMock();
        userManager
            .Setup(manager => manager.FindByLoginAsync(info.LoginProvider, info.ProviderKey))
            .ReturnsAsync(user);

        var signInManager = CreateSignInManagerMock(userManager);
        signInManager
            .Setup(manager => manager.GetExternalLoginInfoAsync(null))
            .ReturnsAsync(info);
        var externalSignInCoordinator = new Mock<IExternalSignInCoordinator>();
        externalSignInCoordinator
            .Setup(service => service.CompleteAsync(
                It.IsAny<HttpContext>(),
                user,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExternalSignInCompletionResult.Succeeded());

        var model = CreateModel(
            signInManager,
            userManager,
            new Mock<ILoginService>(),
            externalSignInCoordinator,
            autoLinkMatchingEmail: true);

        var result = await model.OnGetAsync("/");

        Assert.IsType<LocalRedirectResult>(result);
        userManager.Verify(manager => manager.FindByEmailAsync(It.IsAny<string>()), Times.Never);
        userManager.Verify(manager => manager.AddLoginAsync(It.IsAny<ApplicationUser>(), info), Times.Never);
        externalSignInCoordinator.Verify(
            service => service.CompleteAsync(
                It.IsAny<HttpContext>(),
                user,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OnGetAsync_LinkedIneligibleUser_ValidatesBeforeCreatingApplicationCookie()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "disabled-user",
            IsActive = false
        };
        var info = new ExternalLoginInfo(
            new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "provider-user")],
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
            .Setup(manager => manager.FindByLoginAsync(info.LoginProvider, info.ProviderKey))
            .ReturnsAsync(user);

        var contextAccessor = new Mock<IHttpContextAccessor>();
        var claimsFactory = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
        var signInManager = new Mock<SignInManager<ApplicationUser>>(
            userManager.Object,
            contextAccessor.Object,
            claimsFactory.Object,
            null,
            null,
            null,
            null);
        signInManager
            .Setup(manager => manager.GetExternalLoginInfoAsync(null))
            .ReturnsAsync(info);
        signInManager
            .Setup(manager => manager.ExternalLoginSignInAsync(
                info.LoginProvider,
                info.ProviderKey,
                false,
                true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        var loginService = new Mock<ILoginService>();
        var externalSignInCoordinator = new Mock<IExternalSignInCoordinator>();
        externalSignInCoordinator
            .Setup(service => service.CompleteAsync(
                It.IsAny<HttpContext>(),
                user,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExternalSignInCompletionResult.Blocked(LoginResult.UserInactive()));

        var model = new ExternalLoginCallbackModel(
            signInManager.Object,
            userManager.Object,
            Mock.Of<ILogger<ExternalLoginCallbackModel>>(),
            Microsoft.Extensions.Options.Options.Create(new ExternalLoginOptions()),
            loginService.Object,
            Mock.Of<IUserManagementService>(),
            Mock.Of<ILoginHistoryService>(),
            externalSignInCoordinator.Object)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await model.OnGetAsync("/");

        Assert.IsType<RedirectToPageResult>(result);
        signInManager.Verify(
            manager => manager.ExternalLoginSignInAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()),
            Times.Never);
        signInManager.Verify(
            manager => manager.SignInWithClaimsAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<bool>(),
                It.IsAny<IEnumerable<Claim>>()),
            Times.Never);
        externalSignInCoordinator.Verify(
            service => service.CompleteAsync(
                It.IsAny<HttpContext>(),
                user,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static ExternalLoginInfo CreateExternalLoginInfo(
        string provider,
        string? assuranceValue,
        string? email = "matched@example.com")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "provider-user")
        };
        if (email != null)
        {
            claims.Add(new Claim(ClaimTypes.Email, email));
        }

        if (assuranceValue != null)
        {
            claims.Add(new Claim(
                AuthConstants.Claims.ExternalEmailVerified,
                assuranceValue,
                ClaimValueTypes.Boolean));
        }

        return new ExternalLoginInfo(
            new ClaimsPrincipal(new ClaimsIdentity(claims, "External")),
            provider,
            "provider-user",
            provider);
    }

    private static Mock<UserManager<ApplicationUser>> CreateUserManagerMock()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
    }

    private static Mock<SignInManager<ApplicationUser>> CreateSignInManagerMock(
        Mock<UserManager<ApplicationUser>> userManager)
    {
        return new Mock<SignInManager<ApplicationUser>>(
            userManager.Object,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            null,
            null,
            null,
            null);
    }

    private static ExternalLoginCallbackModel CreateModel(
        Mock<SignInManager<ApplicationUser>> signInManager,
        Mock<UserManager<ApplicationUser>> userManager,
        Mock<ILoginService> loginService,
        Mock<IExternalSignInCoordinator> externalSignInCoordinator,
        bool autoLinkMatchingEmail)
    {
        var userManagementService = new Mock<IUserManagementService>();
        userManagementService
            .Setup(service => service.UpdateLastLoginAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new ExternalLoginCallbackModel(
            signInManager.Object,
            userManager.Object,
            Mock.Of<ILogger<ExternalLoginCallbackModel>>(),
            Microsoft.Extensions.Options.Options.Create(new ExternalLoginOptions
            {
                AutoLinkMatchingEmail = autoLinkMatchingEmail
            }),
            loginService.Object,
            userManagementService.Object,
            Mock.Of<ILoginHistoryService>(),
            externalSignInCoordinator.Object)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }
}
