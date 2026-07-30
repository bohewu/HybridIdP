using System.Security.Claims;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Options;
using Core.Domain;
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
}
