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
using Web.IdP.Services;

namespace Tests.Web.IdP.UnitTests.Pages.Account;

public sealed class LoginMfaModelPasskeyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LoginPages_ShouldNotSignIn_WhenProofConsumptionFails(bool nativePage, bool recovery)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "proof-user", TwoFactorEnabled = true };
        var users = CreateUserManager();
        users.Setup(x => x.AccessFailedAsync(user)).ReturnsAsync(IdentityResult.Success);
        var signIn = CreateSignInManager(users.Object);
        signIn.Setup(x => x.GetTwoFactorAuthenticationUserAsync()).ReturnsAsync(user);
        var mfa = new Mock<IMfaService>();
        // A failed service result represents invalid proof or unsuccessful durable consumption.
        mfa.Setup(x => x.ValidateTotpCodeAsync(user, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        mfa.Setup(x => x.ValidateRecoveryCodeAsync(user, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        mfa.Setup(x => x.ValidateNativeRecoveryCodeAsync(user, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var localizer = new Mock<IStringLocalizer<SharedResource>>();
        localizer.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
        var pageContext = new PageContext(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()));
        IActionResult result;
        if (nativePage)
        {
            var model = new LoginTotpModel(signIn.Object, users.Object, mfa.Object, Mock.Of<IUserManagementService>(),
                Mock.Of<IDomainEventPublisher>(), Mock.Of<ILogger<LoginTotpModel>>(), localizer.Object,
                Mock.Of<IMigrationIssuanceGuard>(), Mock.Of<ICurrentUserLifecycleEligibility>())
            { PageContext = pageContext, ReturnUrl = "/", Input = new LoginTotpModel.InputModel
                { TotpCode = recovery ? null : "123456", RecoveryCode = recovery ? "ABCDE-FGHIJ" : null } };
            result = await model.OnPostAsync(default);
        }
        else
        {
            var passkeys = new Mock<IPasskeyService>();
            passkeys.Setup(x => x.GetUserPasskeysAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
            var model = new LoginMfaModel(signIn.Object, users.Object, mfa.Object, Mock.Of<IUserManagementService>(),
                passkeys.Object, Mock.Of<IDomainEventPublisher>(), Mock.Of<ILogger<LoginMfaModel>>(), localizer.Object,
                Mock.Of<IMigrationIssuanceGuard>(), Mock.Of<ICurrentUserLifecycleEligibility>())
            { PageContext = pageContext, ReturnUrl = "/", Input = new LoginMfaModel.InputModel
                { TotpCode = recovery ? null : "123456", RecoveryCode = recovery ? "ABCDE-FGHIJ" : null } };
            result = await model.OnPostAsync(default);
        }
        Assert.IsType<PageResult>(result);
        users.Verify(x => x.AccessFailedAsync(user), Times.Once);
        signIn.Verify(x => x.SignInWithClaimsAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>(), It.IsAny<IEnumerable<Claim>>()), Times.Never);
    }
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
            Mock.Of<IStringLocalizer<SharedResource>>(),
            Mock.Of<IMigrationIssuanceGuard>(),
            Mock.Of<ICurrentUserLifecycleEligibility>());
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
