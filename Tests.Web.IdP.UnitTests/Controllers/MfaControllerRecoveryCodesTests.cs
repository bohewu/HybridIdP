using System.Security.Claims;
using Core.Application;
using Core.Application.Interfaces;
using Core.Domain;
using Core.Domain.Entities;
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

namespace Tests.Web.IdP.UnitTests.Controllers;

public class MfaControllerRecoveryCodesTests
{
    [Fact]
    public async Task PasswordConfirmation_ShouldShareConfiguredAccountBudgetAndStopCheckingAfterLockout()
    {
        var fixture = CreateCookieAuthenticatedController(true);
        var failures = 0;
        var locked = false;
        fixture.UserManager.Setup(manager => manager.IsLockedOutAsync(fixture.User)).ReturnsAsync(() => locked);
        fixture.UserManager.Setup(manager => manager.AccessFailedAsync(fixture.User))
            .Callback(() => locked = ++failures >= fixture.UserManager.Object.Options.Lockout.MaxFailedAccessAttempts)
            .ReturnsAsync(IdentityResult.Success);
        var policy = new SecurityPolicy { MaxFailedAccessAttempts = 3, LockoutDurationMinutes = 10 };
        for (var index = 0; index < 5; index++)
            Assert.False(await PasswordConfirmation.CheckAsync(fixture.UserManager.Object, fixture.User, "invalid-confirmation", policy));
        Assert.Equal(3, failures);
        fixture.UserManager.Verify(manager => manager.CheckPasswordAsync(fixture.User, "invalid-confirmation"), Times.Exactly(3));
    }

    [Theory]
    [InlineData("password")]
    [InlineData("consumed")]
    [InlineData("stamp")]
    public async Task GenerateRecoveryCodes_ShouldRejectMissingOrRevokedMfaManagementAuthority(string condition)
    {
        var fixture = CreateCookieAuthenticatedController(true);
        if (condition == "password")
        {
            var identity = (ClaimsIdentity)fixture.Controller.User.Identity!;
            identity.RemoveClaim(identity.FindFirst("amr")!);
            identity.AddClaim(new Claim("amr", "pwd"));
        }
        if (condition == "consumed") MfaEnrollmentSession.Consume(fixture.Controller.HttpContext.Session);
        if (condition == "stamp") fixture.User.SecurityStamp = "reset-stamp";
        var result = await fixture.Controller.GenerateRecoveryCodes(new RecoveryCodesRequest { Password = Guid.NewGuid().ToString("N") }, default);
        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        fixture.UserManager.Verify(manager => manager.CheckPasswordAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        VerifyNoRegenerationOrAudit(fixture);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenericEmailEnrollment_ShouldRejectOrdinaryCookieWithoutFreshProof(bool verify)
    {
        var fixture = CreateCookieAuthenticatedController(true);
        fixture.User.Email = "enrollment@example.test";
        MfaEnrollmentSession.Consume(fixture.Controller.HttpContext.Session);
        var result = verify
            ? await fixture.Controller.VerifyEmailMfaCode(new EmailMfaVerifyRequest { Code = "123456" }, default)
            : await fixture.Controller.SendEmailMfaCode(default);
        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
        fixture.MfaService.Verify(service => service.SendEmailMfaCodeAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.MfaService.Verify(service => service.VerifyAndEnableEmailMfaAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenericEmailEnrollment_ShouldCompleteWithFreshMfaProofAndConsumeItOnlyAfterSuccess()
    {
        var fixture = CreateCookieAuthenticatedController(true);
        fixture.User.Email = "enrollment@example.test";
        fixture.MfaService.Setup(service => service.SendEmailMfaCodeAsync(fixture.User, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, 60));
        Assert.IsType<OkObjectResult>(await fixture.Controller.SendEmailMfaCode(default));
        Assert.True(MfaEnrollmentSession.HasFreshProof(fixture.Controller.HttpContext.Session, fixture.User.Id));
        fixture.MfaService.Setup(service => service.VerifyAndEnableEmailMfaAsync(fixture.User, "invalid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        Assert.IsType<OkObjectResult>(await fixture.Controller.VerifyEmailMfaCode(new EmailMfaVerifyRequest { Code = "invalid" }, default));
        Assert.True(MfaEnrollmentSession.HasFreshProof(fixture.Controller.HttpContext.Session, fixture.User.Id));
        fixture.MfaService.Setup(service => service.VerifyAndEnableEmailMfaAsync(fixture.User, "valid", It.IsAny<CancellationToken>()))
            .Callback(() => fixture.User.EmailMfaEnabled = true).ReturnsAsync(true);
        Assert.IsType<OkObjectResult>(await fixture.Controller.VerifyEmailMfaCode(new EmailMfaVerifyRequest { Code = "valid" }, default));
        Assert.True(fixture.User.EmailMfaEnabled);
        Assert.False(MfaEnrollmentSession.HasFreshProof(fixture.Controller.HttpContext.Session, fixture.User.Id));
        fixture.MfaService.Verify(service => service.SendEmailMfaCodeAsync(fixture.User, It.IsAny<CancellationToken>()), Times.Once);
        fixture.AuditService.Verify(service => service.LogEventAsync("EmailMfaEnabled", fixture.User.Id.ToString(),
            null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateRecoveryCodes_BearerPrincipalWithoutInteractiveAuthentication_ReturnsUnauthorizedOrForbiddenWithoutMutation()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "bearer-user",
            TwoFactorEnabled = true
        };
        var recoveryStateSentinel = new object();
        object recoveryState = recoveryStateSentinel;

        var mfaService = new Mock<IMfaService>();
        mfaService
            .Setup(service => service.GenerateRecoveryCodesAsync(
                user,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => recoveryState = new object())
            .ReturnsAsync(Array.Empty<string>());

        var securityPolicyService = new Mock<ISecurityPolicyService>();
        securityPolicyService
            .Setup(service => service.GetCurrentPolicyAsync())
            .ReturnsAsync(new SecurityPolicy { EnableTotpMfa = true });

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
        userManager
            .Setup(manager => manager.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        var auditService = new Mock<IAuditService>();
        var authenticationService = new Mock<IAuthenticationService>();
        authenticationService
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.NoResult());

        using var services = new ServiceCollection()
            .AddSingleton(authenticationService.Object)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", user.Id.ToString())],
                "Bearer"))
        };

        var controller = new MfaController(
            mfaService.Object,
            securityPolicyService.Object,
            userManager.Object,
            auditService.Object,
            Mock.Of<IPasskeyService>(),
            Mock.Of<ILogger<MfaController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var result = await controller.GenerateRecoveryCodes(
            request: null,
            CancellationToken.None);

        Assert.True(
            result.Result is UnauthorizedResult
            || result.Result is ObjectResult { StatusCode: StatusCodes.Status403Forbidden },
            "A bearer principal without an interactive application session must be rejected.");
        mfaService.Verify(
            service => service.GenerateRecoveryCodesAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        auditService.Verify(
            service => service.LogEventAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Same(recoveryStateSentinel, recoveryState);
    }

    [Fact]
    public async Task GenerateRecoveryCodes_PasswordUserWithoutPassword_ReturnsPasswordRequiredWithoutMutation()
    {
        var fixture = CreateCookieAuthenticatedController(hasPassword: true);

        var result = await fixture.Controller.GenerateRecoveryCodes(
            new RecoveryCodesRequest(),
            CancellationToken.None);

        Assert.Equal("passwordRequired", GetError(result));
        VerifyNoRegenerationOrAudit(fixture);
        fixture.UserManager.Verify(
            manager => manager.CheckPasswordAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task GenerateRecoveryCodes_PasswordUserWithInvalidPassword_ReturnsInvalidPasswordWithoutMutation()
    {
        var fixture = CreateCookieAuthenticatedController(hasPassword: true);
        var password = Guid.NewGuid().ToString("N");
        fixture.UserManager
            .Setup(manager => manager.CheckPasswordAsync(fixture.User, password))
            .ReturnsAsync(false);

        var result = await fixture.Controller.GenerateRecoveryCodes(
            new RecoveryCodesRequest { Password = password },
            CancellationToken.None);

        Assert.Equal("invalidPassword", GetError(result));
        VerifyNoRegenerationOrAudit(fixture);
    }

    [Fact]
    public async Task GenerateRecoveryCodes_PasswordUserWithValidPassword_PersistsBeforeAuditAndReturnsCodesOnce()
    {
        var fixture = CreateCookieAuthenticatedController(hasPassword: true);
        var password = Guid.NewGuid().ToString("N");
        var codes = CreateOpaqueValues(2);
        var persisted = false;
        fixture.UserManager
            .Setup(manager => manager.CheckPasswordAsync(fixture.User, password))
            .ReturnsAsync(true);
        fixture.MfaService
            .Setup(service => service.GenerateRecoveryCodesAsync(
                fixture.User,
                10,
                It.IsAny<CancellationToken>()))
            .Callback(() => persisted = true)
            .ReturnsAsync(codes);
        fixture.AuditService
            .Setup(service => service.LogEventAsync(
                "MfaRecoveryCodesRegenerated",
                fixture.User.Id.ToString(),
                null,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Callback(() => Assert.True(persisted))
            .Returns(Task.CompletedTask);

        var result = await fixture.Controller.GenerateRecoveryCodes(
            new RecoveryCodesRequest { Password = password },
            CancellationToken.None);

        var response = Assert.IsType<RecoveryCodesResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(codes, response.RecoveryCodes);
        fixture.MfaService.Verify(
            service => service.GenerateRecoveryCodesAsync(
                fixture.User,
                10,
                It.IsAny<CancellationToken>()),
            Times.Once);
        VerifySuccessAuditOnce(fixture);
    }

    [Fact]
    public async Task GenerateRecoveryCodes_PasswordlessUserWithoutTotp_ReturnsTotpRequiredWithoutMutation()
    {
        var fixture = CreateCookieAuthenticatedController(hasPassword: false);

        var result = await fixture.Controller.GenerateRecoveryCodes(
            new RecoveryCodesRequest(),
            CancellationToken.None);

        Assert.Equal("totpRequired", GetError(result));
        VerifyNoRegenerationOrAudit(fixture);
        fixture.MfaService.Verify(
            service => service.ValidateTotpCodeAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GenerateRecoveryCodes_PasswordlessUserWithRejectedTotp_ReturnsInvalidCodeWithoutMutation()
    {
        var fixture = CreateCookieAuthenticatedController(hasPassword: false);
        var totpCode = Random.Shared.Next(0, 1_000_000).ToString("D6");
        fixture.MfaService
            .Setup(service => service.ValidateTotpCodeAsync(
                fixture.User,
                totpCode,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await fixture.Controller.GenerateRecoveryCodes(
            new RecoveryCodesRequest { TotpCode = totpCode },
            CancellationToken.None);

        Assert.Equal("invalidCode", GetError(result));
        VerifyNoRegenerationOrAudit(fixture);
    }

    [Fact]
    public async Task GenerateRecoveryCodes_PasswordlessUserWithValidTotp_ValidatesAndPersistsBeforeAudit()
    {
        var fixture = CreateCookieAuthenticatedController(hasPassword: false);
        var totpCode = Random.Shared.Next(0, 1_000_000).ToString("D6");
        var codes = CreateOpaqueValues(2);
        var totpValidated = false;
        var persisted = false;
        fixture.MfaService
            .Setup(service => service.ValidateTotpCodeAsync(
                fixture.User,
                totpCode,
                It.IsAny<CancellationToken>()))
            .Callback(() => totpValidated = true)
            .ReturnsAsync(true);
        fixture.MfaService
            .Setup(service => service.GenerateRecoveryCodesAsync(
                fixture.User,
                10,
                It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                Assert.True(totpValidated);
                persisted = true;
            })
            .ReturnsAsync(codes);
        fixture.AuditService
            .Setup(service => service.LogEventAsync(
                "MfaRecoveryCodesRegenerated",
                fixture.User.Id.ToString(),
                null,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Callback(() => Assert.True(persisted))
            .Returns(Task.CompletedTask);

        var result = await fixture.Controller.GenerateRecoveryCodes(
            new RecoveryCodesRequest { TotpCode = totpCode },
            CancellationToken.None);

        var response = Assert.IsType<RecoveryCodesResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(codes, response.RecoveryCodes);
        VerifySuccessAuditOnce(fixture);
    }

    [Fact]
    public async Task GenerateRecoveryCodes_PersistenceFailure_ThrowsWithoutSuccessAudit()
    {
        var fixture = CreateCookieAuthenticatedController(hasPassword: true);
        var password = Guid.NewGuid().ToString("N");
        fixture.UserManager
            .Setup(manager => manager.CheckPasswordAsync(fixture.User, password))
            .ReturnsAsync(true);
        fixture.MfaService
            .Setup(service => service.GenerateRecoveryCodesAsync(
                fixture.User,
                10,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Persistence failed."));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Controller.GenerateRecoveryCodes(
                new RecoveryCodesRequest { Password = password },
                CancellationToken.None));

        fixture.AuditService.Verify(
            service => service.LogEventAsync(
                "MfaRecoveryCodesRegenerated",
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static ControllerFixture CreateCookieAuthenticatedController(bool hasPassword)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "interactive-user",
            SecurityStamp = "current-stamp",
            TwoFactorEnabled = true
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("amr", "mfa"),
             new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp)],
            IdentityConstants.ApplicationScheme));

        var mfaService = new Mock<IMfaService>();
        var securityPolicyService = new Mock<ISecurityPolicyService>();
        securityPolicyService
            .Setup(service => service.GetCurrentPolicyAsync())
            .ReturnsAsync(new SecurityPolicy { EnableTotpMfa = true });

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
            .Setup(manager => manager.GetUserAsync(principal))
            .ReturnsAsync(user);
        userManager
            .Setup(manager => manager.HasPasswordAsync(user))
            .ReturnsAsync(hasPassword);
        userManager.Setup(manager => manager.AccessFailedAsync(user)).ReturnsAsync(IdentityResult.Success);

        var auditService = new Mock<IAuditService>();
        var authenticationService = new Mock<IAuthenticationService>();
        authenticationService.Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.NoResult());
        authenticationService
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.Success(
                new AuthenticationTicket(
                    principal,
                    IdentityConstants.ApplicationScheme)));

        var services = new ServiceCollection()
            .AddSingleton(authenticationService.Object)
            .AddSingleton(Mock.Of<ICurrentUserLifecycleEligibility>(service =>
                service.IsEligibleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(true)))
            .AddSingleton(Mock.Of<IMigrationIssuanceGuard>(service =>
                service.CanIssueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(true)))
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            Session = new Tests.Web.IdP.UnitTests.TestSupport.MemorySession(),
            User = principal
        };
        MfaEnrollmentSession.Begin(httpContext.Session, user.Id, true, securityStamp: user.SecurityStamp);
        Assert.True(MfaEnrollmentSession.CompletePending(httpContext.Session, principal));

        var controller = new MfaController(
            mfaService.Object,
            securityPolicyService.Object,
            userManager.Object,
            auditService.Object,
            Mock.Of<IPasskeyService>(),
            Mock.Of<ILogger<MfaController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return new ControllerFixture(
            controller,
            user,
            mfaService,
            userManager,
            auditService);
    }

    private static List<string> CreateOpaqueValues(int count) =>
        Enumerable.Range(0, count)
            .Select(_ => Guid.NewGuid().ToString("N"))
            .ToList();

    private static string? GetError(ActionResult<RecoveryCodesResponse> result)
    {
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        return badRequest.Value?
            .GetType()
            .GetProperty("error")?
            .GetValue(badRequest.Value) as string;
    }

    private static void VerifyNoRegenerationOrAudit(ControllerFixture fixture)
    {
        fixture.MfaService.Verify(
            service => service.GenerateRecoveryCodesAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.AuditService.Verify(
            service => service.LogEventAsync(
                "MfaRecoveryCodesRegenerated",
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static void VerifySuccessAuditOnce(ControllerFixture fixture)
    {
        fixture.AuditService.Verify(
            service => service.LogEventAsync(
                "MfaRecoveryCodesRegenerated",
                fixture.User.Id.ToString(),
                null,
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private sealed record ControllerFixture(
        MfaController Controller,
        ApplicationUser User,
        Mock<IMfaService> MfaService,
        Mock<UserManager<ApplicationUser>> UserManager,
        Mock<IAuditService> AuditService);
}
