using System.Security.Claims;
using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Web.IdP.Options;
using Web.IdP.Pages.Account;
using Web.IdP.Services;

namespace Tests.Web.IdP.UnitTests.Pages.Account;

public class ExternalLoginConfirmationEmailAssuranceTests
{
    [Theory]
    [InlineData(AuthConstants.Providers.Google, "true", true)]
    [InlineData(AuthConstants.Providers.Google, "false", false)]
    [InlineData(AuthConstants.Providers.Google, null, false)]
    [InlineData(AuthConstants.Providers.Microsoft, "true", true)]
    [InlineData("CustomProvider", "true", false)]
    public async Task OnPostCreateAsync_ShouldCarryOnlySupportedProviderEmailAssurance(
        string provider,
        string? assuranceValue,
        bool expectedVerified)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "provider-user"),
            new(ClaimTypes.Email, "user@example.com")
        };
        if (assuranceValue != null)
        {
            claims.Add(new Claim(
                AuthConstants.Claims.ExternalEmailVerified,
                assuranceValue,
                ClaimValueTypes.Boolean));
        }

        var info = new ExternalLoginInfo(
            new ClaimsPrincipal(new ClaimsIdentity(claims, "External")),
            provider,
            "provider-user",
            provider);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "external-user"
        };

        var userManager = CreateUserManagerMock();
        var signInManager = CreateSignInManagerMock(userManager);
        signInManager
            .Setup(manager => manager.GetExternalLoginInfoAsync(null))
            .ReturnsAsync(info);

        ExternalAuthResult? capturedAuth = null;
        var jitProvisioningService = new Mock<IJitProvisioningService>();
        jitProvisioningService
            .Setup(service => service.ProvisionExternalUserAsync(
                It.IsAny<ExternalAuthResult>(),
                It.IsAny<CancellationToken>()))
            .Callback<ExternalAuthResult, CancellationToken>((auth, _) => capturedAuth = auth)
            .ReturnsAsync(user);

        var settingsService = new Mock<ISettingsService>();
        settingsService
            .Setup(service => service.GetValueAsync<bool?>(
                SettingKeys.Security.RegistrationEnabled,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var userManagementService = new Mock<IUserManagementService>();
        userManagementService
            .Setup(service => service.UpdateLastLoginAsync(user.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loginHistoryService = new Mock<ILoginHistoryService>();
        loginHistoryService
            .Setup(service => service.DetectAbnormalLoginAsync(It.IsAny<Core.Domain.Entities.LoginHistory>()))
            .ReturnsAsync(false);
        loginHistoryService
            .Setup(service => service.RecordLoginAsync(It.IsAny<Core.Domain.Entities.LoginHistory>()))
            .Returns(Task.CompletedTask);

        var externalSignInCoordinator = new Mock<IExternalSignInCoordinator>();
        externalSignInCoordinator
            .Setup(service => service.CompleteAsync(
                It.IsAny<HttpContext>(),
                user,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExternalSignInCompletionResult.Succeeded());

        var model = new ExternalLoginConfirmationModel(
            userManager.Object,
            signInManager.Object,
            jitProvisioningService.Object,
            Mock.Of<ILoginService>(),
            settingsService.Object,
            Mock.Of<IBrandingService>(),
            userManagementService.Object,
            loginHistoryService.Object,
            Mock.Of<IStringLocalizer<global::Web.IdP.SharedResource>>(),
            Mock.Of<ILogger<ExternalLoginConfirmationModel>>(),
            Microsoft.Extensions.Options.Options.Create(new LoginNoticesOptions()),
            externalSignInCoordinator.Object)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await model.OnPostCreateAsync("/");

        Assert.IsType<LocalRedirectResult>(result);
        Assert.NotNull(capturedAuth);
        Assert.Equal(expectedVerified, capturedAuth.EmailVerified);
    }

    [Fact]
    public async Task OnPostLinkAsync_MissingEmailAssurance_LinksAuthenticatedLocalUser()
    {
        var info = new ExternalLoginInfo(
            new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "provider-user"),
                    new Claim(ClaimTypes.Email, "external@example.com")
                ],
                "External")),
            AuthConstants.Providers.Google,
            "provider-user",
            AuthConstants.Providers.Google);
        var localUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "local-user"
        };

        Assert.DoesNotContain(
            info.Principal.Claims,
            claim => claim.Type == AuthConstants.Claims.ExternalEmailVerified);

        var userManager = CreateUserManagerMock();
        userManager
            .Setup(manager => manager.AddLoginAsync(localUser, info))
            .ReturnsAsync(IdentityResult.Success);

        var signInManager = CreateSignInManagerMock(userManager);
        signInManager
            .Setup(manager => manager.GetExternalLoginInfoAsync(null))
            .ReturnsAsync(info);

        var loginService = new Mock<ILoginService>();
        loginService
            .Setup(service => service.AuthenticateAsync(
                "local-user",
                "test-password",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginResult.Success(localUser));
        loginService
            .Setup(service => service.CanLinkExternalLoginAsync(
                localUser,
                info.LoginProvider,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null));

        var settingsService = new Mock<ISettingsService>();
        settingsService
            .Setup(service => service.GetValueAsync<bool?>(
                SettingKeys.Security.RegistrationEnabled,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var brandingService = new Mock<IBrandingService>();
        brandingService
            .Setup(service => service.GetAppNameAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("HybridAuth");

        var userManagementService = new Mock<IUserManagementService>();
        userManagementService
            .Setup(service => service.UpdateLastLoginAsync(localUser.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loginHistoryService = new Mock<ILoginHistoryService>();
        loginHistoryService
            .Setup(service => service.DetectAbnormalLoginAsync(It.IsAny<Core.Domain.Entities.LoginHistory>()))
            .ReturnsAsync(false);
        loginHistoryService
            .Setup(service => service.RecordLoginAsync(It.IsAny<Core.Domain.Entities.LoginHistory>()))
            .Returns(Task.CompletedTask);

        var externalSignInCoordinator = new Mock<IExternalSignInCoordinator>();
        externalSignInCoordinator
            .Setup(service => service.CompleteAsync(
                It.IsAny<HttpContext>(),
                localUser,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExternalSignInCompletionResult.Succeeded());

        var model = new ExternalLoginConfirmationModel(
            userManager.Object,
            signInManager.Object,
            Mock.Of<IJitProvisioningService>(),
            loginService.Object,
            settingsService.Object,
            brandingService.Object,
            userManagementService.Object,
            loginHistoryService.Object,
            Mock.Of<IStringLocalizer<global::Web.IdP.SharedResource>>(),
            Mock.Of<ILogger<ExternalLoginConfirmationModel>>(),
            Microsoft.Extensions.Options.Options.Create(new LoginNoticesOptions()),
            externalSignInCoordinator.Object)
        {
            Input = new ExternalLoginConfirmationModel.InputModel
            {
                Login = "local-user",
                Password = "test-password"
            },
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await model.OnPostLinkAsync("/account");

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/account", redirect.Url);
        loginService.Verify(
            service => service.AuthenticateAsync("local-user", "test-password", It.IsAny<CancellationToken>()),
            Times.Once);
        userManager.Verify(manager => manager.AddLoginAsync(localUser, info), Times.Once);
        externalSignInCoordinator.Verify(
            service => service.CompleteAsync(
                It.IsAny<HttpContext>(),
                localUser,
                It.IsAny<CancellationToken>()),
            Times.Once);
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
}
