using System.Security.Claims;
using Core.Application;
using Core.Application.Interfaces;
using Core.Domain;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP.Controllers.Account;

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

        var controller = new MfaSetupApiController(
            mfaService.Object,
            policyService.Object,
            userManager.Object,
            signInManager.Object,
            auditService.Object,
            Mock.Of<IPasskeyService>(),
            Mock.Of<ILogger<MfaSetupApiController>>());

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
            auditService);
    }

    private sealed record ControllerFixture(
        MfaSetupApiController Controller,
        ApplicationUser User,
        Mock<IMfaService> MfaService,
        Mock<SignInManager<ApplicationUser>> SignInManager,
        Mock<IAuditService> AuditService);

    private sealed class TestSessionFeature : ISessionFeature
    {
        public required ISession Session { get; set; }
    }
}
