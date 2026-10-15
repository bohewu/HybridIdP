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
using Microsoft.EntityFrameworkCore;
using Infrastructure;
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
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public async Task Disable_ShouldCarryCurrentCookieStampWithoutRenewingProof_WhenRemovalSucceeds(
        bool email, bool bearer, bool supersededStamp)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), IsActive = true, SecurityStamp = "before",
            TwoFactorEnabled = true, EmailMfaEnabled = true };
        using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        database.Users.Add(user);
        await database.SaveChangesAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("amr", "mfa"),
            new Claim("AspNet.Identity.SecurityStamp", "before"), new Claim("auth_time", "1770000000"),
            new Claim(AuthorizationAuthenticationSession.AssuranceVersionClaim,
                AuthorizationAuthenticationSession.AssuranceVersion)
        }, "test"));
        var session = new MemorySession();
        MfaEnrollmentSession.Begin(session, user.Id, true, securityStamp: "before");
        Assert.True(MfaEnrollmentSession.CompletePending(session, principal));
        var authentication = new Mock<IAuthenticationService>();
        authentication.Setup(x => x.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.NoResult());
        var scheme = bearer ? OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme : IdentityConstants.ApplicationScheme;
        var properties = new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(20) };
        authentication.Setup(x => x.AuthenticateAsync(It.IsAny<HttpContext>(), scheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, properties, scheme)));
        ClaimsPrincipal? refreshed = null;
        authentication.Setup(x => x.SignInAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme,
                It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()))
            .Callback<HttpContext, string, ClaimsPrincipal, AuthenticationProperties>((_, _, value, options) =>
            {
                refreshed = value;
                Assert.Same(properties, options);
            }).Returns(Task.CompletedTask);
        var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        users.Object.Options = new IdentityOptions();
        users.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);
        var service = new Mock<IMfaService>();
        service.Setup(x => x.ValidateTotpCodeAsync(user, "123456", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        async Task<MfaRemovalResult> RemoveFactor()
        {
            if (email) user.EmailMfaEnabled = false;
            else user.TwoFactorEnabled = false;
            user.SecurityStamp = supersededStamp ? "independent-revocation" : "removal-stamp";
            await database.SaveChangesAsync();
            user.SecurityStamp = "removal-stamp";
            return MfaRemovalResult.Succeeded;
        }
        service.Setup(x => x.DisableEmailMfaAsync(user, It.IsAny<CancellationToken>())).Returns(RemoveFactor);
        service.Setup(x => x.DisableMfaAsync(user, It.IsAny<CancellationToken>())).Returns(RemoveFactor);
        var eligible = new Mock<ICurrentUserLifecycleEligibility>();
        eligible.Setup(x => x.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var migration = new Mock<IMigrationIssuanceGuard>();
        migration.Setup(x => x.CanIssueAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        using var services = new ServiceCollection().AddSingleton(authentication.Object).AddSingleton(eligible.Object)
            .AddSingleton(migration.Object).AddSingleton(users.Object).AddSingleton<IApplicationDbContext>(database).AddSingleton<Core.Application.IAccountRecoveryCodeService>(Moq.Mock.Of<Core.Application.IAccountRecoveryCodeService>(codes => codes.GetStatusAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<CancellationToken>()) == Task.FromResult(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(false, 0)))).BuildServiceProvider();
        var controller = new MfaController(service.Object, Mock.Of<ISecurityPolicyService>(), users.Object,
            Mock.Of<IAuditService>(), Mock.Of<IPasskeyService>(), Mock.Of<ILogger<MfaController>>())
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal, Session = session, RequestServices = services } } };

        var result = email ? await controller.DisableEmailMfa(default)
            : await controller.Disable(new MfaDisableRequest { TotpCode = "123456" }, default);

        Assert.IsType<OkObjectResult>(result);
        Assert.False(MfaEnrollmentSession.HasFreshProof(session, user.Id));
        Assert.Equal("before", principal.FindFirstValue("AspNet.Identity.SecurityStamp"));
        if (bearer || supersededStamp)
        {
            Assert.Null(refreshed);
            return;
        }
        Assert.NotNull(refreshed);
        Assert.Equal("removal-stamp", refreshed.FindFirstValue("AspNet.Identity.SecurityStamp"));
        Assert.Equal("1770000000", refreshed.FindFirstValue("auth_time"));
        Assert.False(MfaEnrollmentSession.HasMfa(refreshed));
        Assert.Null(refreshed.FindFirstValue(AuthorizationAuthenticationSession.MfaSecurityStampClaim));
        authentication.Verify(x => x.SignOutAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme,
            It.IsAny<AuthenticationProperties>()), Times.Never);
        authentication.Verify(x => x.SignOutAsync(It.IsAny<HttpContext>(), IdentityConstants.TwoFactorUserIdScheme,
            It.IsAny<AuthenticationProperties>()), Times.Once);
    }

    [Theory]
    [InlineData(false, false, false, null)]
    [InlineData(true, false, false, null)]
    [InlineData(false, true, false, null)]
    [InlineData(true, true, false, null)]
    [InlineData(false, false, true, null)]
    [InlineData(false, true, true, null)]
    [InlineData(false, false, false, "totp-add")]
    [InlineData(false, false, true, "email-enable")]
    public async Task BeginReauthentication_ShouldKeepDefaultEnrollmentAndFixedRemovalReturn(bool forRemoval, bool bearer,
        bool returnToProfile, string? action)
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
        using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        using var services = new ServiceCollection().AddSingleton(auth.Object).AddSingleton(users.Object)
            .AddSingleton<IApplicationDbContext>(database).AddSingleton<Core.Application.IAccountRecoveryCodeService>(Moq.Mock.Of<Core.Application.IAccountRecoveryCodeService>(codes => codes.GetStatusAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<CancellationToken>()) == Task.FromResult(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(false, 0)))).BuildServiceProvider();
        var session = new MemorySession();
        var controller = new MfaController(Mock.Of<IMfaService>(), Mock.Of<ISecurityPolicyService>(), users.Object,
            Mock.Of<IAuditService>(), Mock.Of<IPasskeyService>(), Mock.Of<ILogger<MfaController>>())
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal, Session = session, RequestServices = services } } };

        var result = Assert.IsType<OkObjectResult>(await controller.BeginReauthentication(forRemoval, returnToProfile, action));
        auth.Verify(x => x.SignOutAsync(controller.HttpContext, IdentityConstants.TwoFactorUserIdScheme,
            It.IsAny<AuthenticationProperties>()), Times.Once);
        var url = Assert.IsType<string>(result.Value!.GetType().GetProperty("loginUrl")!.GetValue(result.Value));
        var returnUrl = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri("https://idp.test" + url).Query)["returnUrl"].ToString();
        Assert.Equal(forRemoval || returnToProfile ? "/Account/Profile" : "/Account/MfaSetup?returnUrl=%2FAccount%2FProfile", returnUrl);
        Assert.True(MfaEnrollmentSession.HasPending(session));
        Assert.False(MfaEnrollmentSession.HasFreshProof(session, user.Id));
        Assert.Equal(action switch
        {
            "totp-add" => "MfaReauthenticationAddAuthenticatorNotice",
            "email-enable" => "MfaReauthenticationEnableEmailOtpNotice",
            _ => "MfaEnrollmentReauthenticationNotice"
        }, MfaEnrollmentSession.GetPendingNoticeKey(session));
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("amr", "mfa"));
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("AspNet.Identity.SecurityStamp", "stamp"));
        Assert.False(MfaEnrollmentSession.CompletePending(controller.HttpContext, principal));
        AccountSecurityOperationSession.MarkVerified(controller.HttpContext, user, "email");
        Assert.True(MfaEnrollmentSession.CompletePending(controller.HttpContext, principal));
        Assert.True(MfaEnrollmentSession.HasFreshProof(session, user.Id));
        Assert.Equal(action == "totp-add" ? "totp" : null, MfaEnrollmentSession.TakeSetupMethod(session, user));
        Assert.True(MfaEnrollmentSession.HasFreshProof(session, user.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://outside.example")]
    [InlineData("totp-add?returnUrl=/outside")]
    public async Task BeginReauthentication_ShouldRejectInvalidActionBeforeStartingCeremony(string action)
    {
        var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        var session = new MemorySession();
        var controller = new MfaController(Mock.Of<IMfaService>(), Mock.Of<ISecurityPolicyService>(), users.Object,
            Mock.Of<IAuditService>(), Mock.Of<IPasskeyService>(), Mock.Of<ILogger<MfaController>>())
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { Session = session } } };

        Assert.IsType<BadRequestObjectResult>(await controller.BeginReauthentication(action: action));

        Assert.Empty(session.Keys);
        users.Verify(manager => manager.GetUserAsync(It.IsAny<ClaimsPrincipal>()), Times.Never);
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
            .AddSingleton(migration.Object).AddSingleton<Core.Application.IAccountRecoveryCodeService>(Moq.Mock.Of<Core.Application.IAccountRecoveryCodeService>(codes => codes.GetStatusAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<CancellationToken>()) == Task.FromResult(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(false, 0)))).BuildServiceProvider();
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
