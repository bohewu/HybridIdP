using System.Security.Claims;
using Core.Application;
using Core.Application.Interfaces;
using Core.Domain;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP.Controllers.Account;
using Web.IdP.Helpers;
using Web.IdP.Services;

namespace Tests.Web.IdP.UnitTests.Controllers;

public class MfaSetupApiControllerEmailMfaTests
{
    [Fact]
    public async Task EnableEmailMfa_WithoutOtpProof_ShouldRejectBeforeStateOrSessionPromotion()
    {
        var fixture = CreateFixture();

        var result = await fixture.Controller.EnableEmailMfa(default);

        Assert.IsType<BadRequestObjectResult>(result);
        fixture.MfaService.Verify(
            service => service.VerifyAndEnableEmailMfaAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.SignInManager.Verify(
            manager => manager.SignInWithClaimsAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<bool>(),
                It.IsAny<IEnumerable<Claim>>()),
            Times.Never);
        fixture.AuditService.Verify(
            service => service.LogEventAsync(
                "EmailMfaEnabled",
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task VerifyEmailMfaCode_InvalidProof_ShouldNotEnableOrPromoteSession()
    {
        var fixture = CreateFixture();
        fixture.MfaService
            .Setup(service => service.VerifyAndEnableEmailMfaAsync(
                fixture.User,
                "000000",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await fixture.Controller.VerifyEmailMfaCode(
            new MfaSetupVerifyRequest { Code = "000000" },
            default);

        Assert.IsType<OkObjectResult>(result);
        Assert.False(fixture.User.EmailMfaEnabled);
        fixture.SignInManager.Verify(
            manager => manager.SignInWithClaimsAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<bool>(),
                It.IsAny<IEnumerable<Claim>>()),
            Times.Never);
        fixture.AuditService.Verify(
            service => service.LogEventAsync(
                "EmailMfaEnabled",
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task VerifyEmailMfaCode_ValidProof_ShouldEnableAndPromoteSessionWithMfaClaims()
    {
        var fixture = CreateFixture();
        fixture.MfaService
            .Setup(service => service.VerifyAndEnableEmailMfaAsync(
                fixture.User,
                "123456",
                It.IsAny<CancellationToken>()))
            .Callback(() => fixture.User.EmailMfaEnabled = true)
            .ReturnsAsync(true);
        fixture.SignInManager
            .Setup(manager => manager.SignInWithClaimsAsync(
                fixture.User,
                false,
                It.IsAny<IEnumerable<Claim>>()))
            .Returns(Task.CompletedTask);

        var result = await fixture.Controller.VerifyEmailMfaCode(
            new MfaSetupVerifyRequest { Code = "123456" },
            default);

        Assert.IsType<OkObjectResult>(result);
        Assert.True(fixture.User.EmailMfaEnabled);
        fixture.SignInManager.Verify(
            manager => manager.SignInWithClaimsAsync(
                fixture.User,
                false,
                It.Is<IEnumerable<Claim>>(claims =>
                    claims.Any(claim => claim.Type == "amr" && claim.Value == "mfa") &&
                    claims.Any(claim => claim.Type == "amr" && claim.Value == "otp"))),
            Times.Once);
        fixture.AuditService.Verify(
            service => service.LogEventAsync(
                "EmailMfaEnabled",
                fixture.User.Id.ToString(),
                null,
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task VerifyEmailMfaCode_WhenMigrationIsIncomplete_DeniesBeforeFullCookie()
    {
        var fixture = CreateFixture();
        fixture.MfaService
            .Setup(service => service.VerifyAndEnableEmailMfaAsync(
                fixture.User,
                "123456",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        fixture.MigrationIssuanceGuard
            .Setup(service => service.CanIssueAsync(fixture.User.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await fixture.Controller.VerifyEmailMfaCode(
            new MfaSetupVerifyRequest { Code = "123456" },
            default);

        Assert.IsType<UnauthorizedResult>(result);
        fixture.SignInManager.Verify(
            manager => manager.SignInWithClaimsAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<bool>(),
                It.IsAny<IEnumerable<Claim>>()),
            Times.Never);
        Assert.DoesNotContain(AuthenticationMethodSession.SessionKey, fixture.Session.Keys);
    }

    [Fact]
    public async Task VerifyTotp_WhenCurrentLifecycleIsIneligible_DeniesBeforeFullCookie()
    {
        var fixture = CreateFixture();
        ArrangeTwoFactorPartialAuthentication(fixture);
        fixture.MfaService
            .Setup(service => service.VerifyAndEnableTotpAsync(
                fixture.User,
                "123456",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        fixture.LifecycleEligibility
            .Setup(service => service.IsEligibleAsync(fixture.User.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await fixture.Controller.VerifyTotp(
            new MfaSetupVerifyRequest { Code = "123456" },
            default);

        Assert.IsType<UnauthorizedResult>(result.Result);
        fixture.MigrationIssuanceGuard.Verify(
            service => service.CanIssueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.SignInManager.Verify(
            manager => manager.SignInWithClaimsAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<bool>(),
                It.IsAny<IEnumerable<Claim>>()),
            Times.Never);
        Assert.DoesNotContain(AuthenticationMethodSession.SessionKey, fixture.Session.Keys);
    }

    [Fact]
    public async Task VerifyTotp_WithEligibleLifecycleAndFinalizedMigration_IssuesFullCookie()
    {
        var fixture = CreateFixture();
        ArrangeTwoFactorPartialAuthentication(fixture);
        fixture.MfaService
            .Setup(service => service.VerifyAndEnableTotpAsync(
                fixture.User,
                "123456",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        fixture.MfaService
            .Setup(service => service.GenerateRecoveryCodesAsync(
                fixture.User,
                10,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        fixture.SignInManager
            .Setup(manager => manager.SignInWithClaimsAsync(
                fixture.User,
                false,
                It.IsAny<IEnumerable<Claim>>()))
            .Returns(Task.CompletedTask);

        var result = await fixture.Controller.VerifyTotp(
            new MfaSetupVerifyRequest { Code = "123456" },
            default);

        Assert.IsType<OkObjectResult>(result.Result);
        fixture.SignInManager.Verify(
            manager => manager.SignInWithClaimsAsync(
                fixture.User,
                false,
                It.IsAny<IEnumerable<Claim>>()),
            Times.Once);
    }

    private static ControllerFixture CreateFixture()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "pending-email-mfa-user",
            Email = "pending@example.test"
        };
        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(),
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
        var mfaService = new Mock<IMfaService>();
        var policyService = new Mock<ISecurityPolicyService>();
        policyService
            .Setup(service => service.GetCurrentPolicyAsync())
            .ReturnsAsync(new SecurityPolicy
            {
                EnableEmailMfa = true
            });
        var auditService = new Mock<IAuditService>();
        auditService
            .Setup(service => service.LogEventAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var migrationIssuanceGuard = new Mock<IMigrationIssuanceGuard>();
        migrationIssuanceGuard
            .Setup(service => service.CanIssueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var lifecycleEligibility = new Mock<ICurrentUserLifecycleEligibility>();
        lifecycleEligibility
            .Setup(service => service.IsEligibleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = new MfaSetupApiController(
            mfaService.Object,
            policyService.Object,
            userManager.Object,
            signInManager.Object,
            auditService.Object,
            Mock.Of<IPasskeyService>(),
            Mock.Of<ILogger<MfaSetupApiController>>(),
            migrationIssuanceGuard.Object,
            lifecycleEligibility.Object);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())],
                "Test"))
        };
        httpContext.Features.Set<ISessionFeature>(new TestSessionFeature
        {
            Session = new MemorySession()
        });
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return new ControllerFixture(
            controller,
            user,
            mfaService,
            signInManager,
            auditService,
            migrationIssuanceGuard,
            lifecycleEligibility,
            (MemorySession)httpContext.Session);
    }

    private static void ArrangeTwoFactorPartialAuthentication(ControllerFixture fixture)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, fixture.User.Id.ToString())],
            IdentityConstants.TwoFactorUserIdScheme));
        var authentication = new Mock<IAuthenticationService>();
        authentication
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                IdentityConstants.TwoFactorUserIdScheme))
            .ReturnsAsync(AuthenticateResult.Success(
                new AuthenticationTicket(principal, IdentityConstants.TwoFactorUserIdScheme)));
        fixture.Controller.HttpContext.RequestServices = new ServiceCollection()
            .AddSingleton(authentication.Object)
            .BuildServiceProvider();
    }

    private sealed record ControllerFixture(
        MfaSetupApiController Controller,
        ApplicationUser User,
        Mock<IMfaService> MfaService,
        Mock<SignInManager<ApplicationUser>> SignInManager,
        Mock<IAuditService> AuditService,
        Mock<IMigrationIssuanceGuard> MigrationIssuanceGuard,
        Mock<ICurrentUserLifecycleEligibility> LifecycleEligibility,
        MemorySession Session);

    private sealed class TestSessionFeature : ISessionFeature
    {
        public required ISession Session { get; set; }
    }
}
