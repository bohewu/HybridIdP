using System.Security.Claims;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Interfaces;
using Core.Domain;
using Core.Domain.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using Web.IdP;
using Web.IdP.Pages.Account;

namespace Tests.Web.IdP.UnitTests.Pages.Account;

public sealed class LoginMfaModelPasskeyTests
{
    [Fact]
    public async Task OnGetAsync_ShouldRenderPasskeyStepUp_WhenAuthenticatedSessionHasPasskey()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "passkey-user@example.test",
            TwoFactorEnabled = false,
            EmailMfaEnabled = false
        };
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())],
                IdentityConstants.ApplicationScheme));

        var userManager = CreateUserManager();
        userManager
            .Setup(manager => manager.GetUserAsync(principal))
            .ReturnsAsync(user);

        var signInManager = CreateSignInManager(userManager.Object);
        signInManager
            .Setup(manager => manager.GetTwoFactorAuthenticationUserAsync())
            .ReturnsAsync((ApplicationUser?)null);

        var passkeyService = new Mock<IPasskeyService>();
        passkeyService
            .Setup(service => service.GetUserPasskeysAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new UserCredentialDto { Id = 1 }]);

        var model = new LoginMfaModel(
            signInManager.Object,
            userManager.Object,
            Mock.Of<IMfaService>(),
            Mock.Of<IUserManagementService>(),
            passkeyService.Object,
            Mock.Of<IDomainEventPublisher>(),
            Mock.Of<ILogger<LoginMfaModel>>(),
            Mock.Of<IStringLocalizer<SharedResource>>());
        model.PageContext = new PageContext(
            new ActionContext(
                new DefaultHttpContext { User = principal },
                new RouteData(),
                new ActionDescriptor()));

        var returnUrl = "/connect/authorize?client_id=testclient-public";

        var result = await model.OnGetAsync(returnUrl);

        Assert.IsType<PageResult>(result);
        Assert.True(model.PasskeyEnabled);
        Assert.False(model.TotpMfaEnabled);
        Assert.Equal(user.UserName, model.PasskeyUserName);
        Assert.Equal(returnUrl, model.ReturnUrl);
    }

    private static Mock<UserManager<ApplicationUser>> CreateUserManager()
    {
        return new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
    }

    private static Mock<SignInManager<ApplicationUser>> CreateSignInManager(
        UserManager<ApplicationUser> userManager)
    {
        return new Mock<SignInManager<ApplicationUser>>(
            userManager,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            null,
            null,
            null,
            null);
    }
}
