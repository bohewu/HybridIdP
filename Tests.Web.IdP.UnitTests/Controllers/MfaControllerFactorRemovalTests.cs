using System.Security.Claims;
using Core.Application;
using Core.Application.Interfaces;
using Core.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OpenIddict.Validation.AspNetCore;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP.Controllers.Account;
using Web.IdP.Helpers;
using Web.IdP.Services;

namespace Tests.Web.IdP.UnitTests.Controllers;

public class MfaControllerFactorRemovalTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task BeginReauthentication_ShouldKeepDefaultEnrollmentAndFixedRemovalReturn(bool forRemoval, bool bearer)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), IsActive = true, SecurityStamp = "stamp", EmailMfaEnabled = true };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("amr", "pwd") }, "test"));
        var scheme = bearer ? OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme : IdentityConstants.ApplicationScheme;
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(x => x.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>())).ReturnsAsync(AuthenticateResult.NoResult());
        auth.Setup(x => x.AuthenticateAsync(It.IsAny<HttpContext>(), scheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, scheme)));
        var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        users.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);
        using var services = new ServiceCollection().AddSingleton(auth.Object).BuildServiceProvider();
        var session = new MemorySession();
        var controller = new MfaController(Mock.Of<IMfaService>(), Mock.Of<ISecurityPolicyService>(), users.Object,
            Mock.Of<IAuditService>(), Mock.Of<IPasskeyService>(), Mock.Of<ILogger<MfaController>>())
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal, Session = session, RequestServices = services } } };

        var result = Assert.IsType<OkObjectResult>(await controller.BeginReauthentication(forRemoval));
        var url = Assert.IsType<string>(result.Value!.GetType().GetProperty("loginUrl")!.GetValue(result.Value));
        var returnUrl = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri("https://idp.test" + url).Query)["returnUrl"].ToString();
        Assert.Equal(forRemoval ? "/Account/Profile" : "/Account/MfaSetup?returnUrl=%2FAccount%2FProfile", returnUrl);
        Assert.True(MfaEnrollmentSession.HasPending(session));
        Assert.False(MfaEnrollmentSession.HasFreshProof(session, user.Id));
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("amr", "mfa"));
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("AspNet.Identity.SecurityStamp", "stamp"));
        Assert.True(MfaEnrollmentSession.CompletePending(session, principal));
        Assert.True(MfaEnrollmentSession.HasFreshProof(session, user.Id));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task Disable_ShouldRequireFreshMfaForCookieAndBearer(bool bearer, bool fresh, bool mfa)
    {
        var password = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { Id = Guid.NewGuid(), IsActive = true, SecurityStamp = "stamp",
            TwoFactorEnabled = true, EmailMfaEnabled = true };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("sub", user.Id.ToString()),
            new Claim("amr", mfa ? "mfa" : "pwd"), new Claim("AspNet.Identity.SecurityStamp", "stamp") }, "test"));
        var session = new MemorySession();
        if (fresh)
        {
            var completed = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim("amr", "mfa"), new Claim("AspNet.Identity.SecurityStamp", "stamp") }, "test"));
            MfaEnrollmentSession.Begin(session, user.Id, true, securityStamp: "stamp");
            Assert.True(MfaEnrollmentSession.CompletePending(session, completed));
        }
        var authentication = new Mock<IAuthenticationService>();
        authentication.Setup(x => x.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>())).ReturnsAsync(AuthenticateResult.NoResult());
        var scheme = bearer ? OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme : IdentityConstants.ApplicationScheme;
        authentication.Setup(x => x.AuthenticateAsync(It.IsAny<HttpContext>(), scheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, scheme)));
        var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        users.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);
        users.Setup(x => x.HasPasswordAsync(user)).ReturnsAsync(true);
        users.Setup(x => x.CheckPasswordAsync(user, password)).ReturnsAsync(true);
        var service = new Mock<IMfaService>();
        service.Setup(x => x.DisableEmailMfaAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync(MfaRemovalResult.Succeeded);
        service.Setup(x => x.DisableMfaAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync(MfaRemovalResult.Succeeded);
        var eligible = new Mock<ICurrentUserLifecycleEligibility>();
        eligible.Setup(x => x.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var migration = new Mock<IMigrationIssuanceGuard>();
        migration.Setup(x => x.CanIssueAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        using var services = new ServiceCollection().AddSingleton(authentication.Object).AddSingleton(eligible.Object)
            .AddSingleton(migration.Object).BuildServiceProvider();
        var controller = new MfaController(service.Object, Mock.Of<ISecurityPolicyService>(), users.Object,
            Mock.Of<IAuditService>(), Mock.Of<IPasskeyService>(), Mock.Of<ILogger<MfaController>>())
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal, Session = session, RequestServices = services } } };

        var totp = await controller.Disable(new MfaDisableRequest { Password = password }, default);
        if (fresh && mfa)
        {
            Assert.IsType<OkObjectResult>(totp);
            // One operation consumes the management proof.
            Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.DisableEmailMfa(default)).StatusCode);
            MfaEnrollmentSession.Begin(session, user.Id, true, securityStamp: "stamp");
            Assert.True(MfaEnrollmentSession.CompletePending(session, principal));
            Assert.IsType<OkObjectResult>(await controller.DisableEmailMfa(default));
        }
        else
        {
            Assert.Equal(403, Assert.IsType<ObjectResult>(totp).StatusCode);
            Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.DisableEmailMfa(default)).StatusCode);
            service.Verify(x => x.DisableMfaAsync(user, It.IsAny<CancellationToken>()), Times.Never);
            service.Verify(x => x.DisableEmailMfaAsync(user, It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
