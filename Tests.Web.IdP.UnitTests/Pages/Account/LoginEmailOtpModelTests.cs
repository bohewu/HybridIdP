using System.Security.Claims;
using System.Text.Json;
using Core.Application;
using Core.Domain;
using Core.Domain.Events;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP;
using Web.IdP.Helpers;
using Web.IdP.Pages.Account;
using Web.IdP.Services;

namespace Tests.Web.IdP.UnitTests.Pages.Account;

public sealed class LoginEmailOtpModelTests
{
    [Theory]
    [InlineData(true, 45)]
    [InlineData(false, 45)]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    public async Task OnGetAsync_ShouldRestorePendingAndCooldownIndependently_WithoutSending(bool pending, int remainingSeconds)
    {
        var (model, user, mfa) = CreateModel();
        user.TwoFactorEnabled = true;
        user.EmailMfaCode = pending ? "HASHED_CODE" : null;
        user.EmailMfaCodeExpiry = pending ? DateTime.UtcNow.AddMinutes(10) : null;
        mfa.Setup(service => service.GetEmailMfaCooldownRemainingSecondsAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(remainingSeconds);
        const string returnUrl = "/Account/MfaSetup?returnUrl=%2FAccount%2FProfile";

        Assert.IsType<PageResult>(await model.OnGetAsync(returnUrl, rememberMe: true));
        Assert.Equal(pending, model.EmailCodeSent);
        Assert.Equal(remainingSeconds, model.EmailCodeRemainingSeconds);
        Assert.Equal(returnUrl, model.ReturnUrl);
        Assert.True(model.RememberMe);
        Assert.True(model.TwoFactorEnabled);
        mfa.Verify(service => service.SendEmailMfaCodeAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("invalid-model", true, 45)]
    [InlineData("invalid-code", true, 45)]
    [InlineData("retired-code", false, 45)]
    [InlineData("expired-code", false, 0)]
    public async Task OnPostAsync_ShouldRestoreCurrentPendingAndCooldown_WhenRenderingFailure(string condition, bool expectedPending, int remainingSeconds)
    {
        var (model, user, mfa) = CreateModel();
        model.ReturnUrl = "/Account/MfaSetup";
        model.Input = new LoginEmailOtpModel.InputModel { EmailCode = "000000" };
        user.EmailMfaCode = "HASHED_CODE";
        user.EmailMfaCodeExpiry = DateTime.UtcNow.AddMinutes(condition == "expired-code" ? -1 : 10);
        mfa.Setup(service => service.GetEmailMfaCooldownRemainingSecondsAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(remainingSeconds);
        mfa.Setup(service => service.VerifyEmailMfaCodeAsync(user, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => { if (condition == "retired-code") user.EmailMfaCode = null; })
            .ReturnsAsync(false);
        if (condition == "invalid-model") model.ModelState.AddModelError("Input.EmailCode", "Invalid code length");

        Assert.IsType<PageResult>(await model.OnPostAsync());
        Assert.Equal(expectedPending, model.EmailCodeSent);
        Assert.Equal(remainingSeconds, model.EmailCodeRemainingSeconds);
        Assert.Equal("/Account/MfaSetup", model.ReturnUrl);
        mfa.Verify(service => service.SendEmailMfaCodeAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("fresh", true)]
    [InlineData("pending", true)]
    [InlineData("consumed", false)]
    [InlineData("expired", false)]
    [InlineData("exhausted", false)]
    [InlineData("missing-expiry", false)]
    public async Task OnPostSendCodeAsync_ShouldReportPendingChallengeIndependentlyOfCooldown(string condition, bool expectedPending)
    {
        var (model, user, mfa) = CreateModel();
        user.EmailMfaCode = condition is "fresh" or "consumed" ? null : "HASHED_CODE";
        user.EmailMfaCodeExpiry = condition == "missing-expiry" ? null :
            DateTime.UtcNow.AddMinutes(condition == "expired" ? -1 : 10);
        user.EmailMfaVerificationAttempts = condition == "exhausted" ? 5 : 4;
        mfa.Setup(service => service.SendEmailMfaCodeAsync(user, It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                if (condition != "fresh") return;
                user.EmailMfaCode = "NEW_HASHED_CODE";
                user.EmailMfaCodeExpiry = DateTime.UtcNow.AddMinutes(10);
                user.EmailMfaVerificationAttempts = 0;
            })
            .ReturnsAsync((condition == "fresh", 60));
        mfa.Setup(service => service.GetEmailMfaCooldownRemainingSecondsAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(60);

        var result = await model.OnPostSendCodeAsync();
        var response = condition == "fresh"
            ? JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(result).Value)
            : JsonSerializer.SerializeToElement(Assert.IsType<ObjectResult>(result).Value);

        if (condition != "fresh") Assert.Equal(429, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal(condition == "fresh", response.GetProperty("success").GetBoolean());
        Assert.Equal(60, response.GetProperty("remainingSeconds").GetInt32());
        Assert.Equal(expectedPending, response.GetProperty("hasPendingCode").GetBoolean());
        Assert.IsType<PageResult>(await model.OnGetAsync("/Account/MfaSetup"));
        Assert.Equal(expectedPending, model.EmailCodeSent);
        Assert.Equal(60, model.EmailCodeRemainingSeconds);
    }

    [Fact]
    public async Task OnPostSendCodeAsync_ShouldRejectRetiredTemporaryCookieBeforeSending()
    {
        var (model, user, mfa) = CreateModel();
        user.SecurityStamp = "rotated-stamp";

        Assert.IsType<NotFoundResult>(await model.OnPostSendCodeAsync());
        mfa.Verify(service => service.SendEmailMfaCodeAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ReauthenticationNotice_ShouldRequireLiveServerPendingContext()
    {
        var (model, user, _) = CreateModel();
        model.ReturnUrl = "/Account/MfaSetup";
        model.HttpContext.Request.QueryString = new QueryString("?reauthenticate=true&returnUrl=/Account/MfaSetup");

        Assert.False(model.IsMfaEnrollmentReauthentication);
        Assert.Equal("MfaEnrollmentReauthenticationNotice", model.MfaReauthenticationNoticeKey);
        MfaEnrollmentSession.Begin(model.HttpContext.Session, user.Id, requiresMfa: true, securityStamp: user.SecurityStamp,
            action: MfaEnrollmentAction.AddAuthenticator);
        Assert.True(model.IsMfaEnrollmentReauthentication);
        Assert.Equal("MfaReauthenticationAddAuthenticatorNotice", model.MfaReauthenticationNoticeKey);
        MfaEnrollmentSession.Consume(model.HttpContext.Session);
        Assert.False(model.IsMfaEnrollmentReauthentication);
    }

    [Theory]
    [InlineData("current", true)]
    [InlineData("stamp", false)]
    [InlineData("subject", false)]
    [InlineData("disabled", false)]
    [InlineData("deleted", false)]
    [InlineData("password-change", false)]
    public async Task ApplicationStepUp_ShouldKeepEmailCooldownAndRejectInvalidSubjectBeforeSending(string condition, bool allowed)
    {
        var (model, user, mfa) = CreateModel(application: true);
        model.RecoveryPurpose = "sign-in";
        user.EmailMfaCode = "HASHED_CODE";
        user.EmailMfaCodeExpiry = DateTime.UtcNow.AddMinutes(5);
        mfa.Setup(value => value.GetEmailMfaCooldownRemainingSecondsAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync(45);
        if (condition == "stamp") user.SecurityStamp = "retired";
        if (condition == "subject") model.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], IdentityConstants.ApplicationScheme));
        if (condition == "disabled") user.IsActive = false;
        if (condition == "deleted") user.IsDeleted = true;
        if (condition == "password-change") user.RequiresPasswordChange = true;
        var result = await model.OnGetAsync("/Account/MfaSetup?returnUrl=%2Fcontinue", true);
        if (allowed)
        {
            Assert.IsType<PageResult>(result);
            Assert.True(model.EmailCodeSent);
            Assert.Equal(45, model.EmailCodeRemainingSeconds);
            Assert.True(model.RememberMe);
            Assert.Equal("sign-in", model.RecoveryPurpose);
            Assert.Equal("/Account/MfaSetup?returnUrl=%2Fcontinue", model.ReturnUrl);
        }
        else
        {
            Assert.IsType<RedirectToPageResult>(result);
            Assert.IsType<NotFoundResult>(await model.OnPostSendCodeAsync());
            model.ReturnUrl = "/continue";
            model.Input = new() { EmailCode = "123456" };
            Assert.IsType<RedirectToPageResult>(await model.OnPostAsync());
        }
        mfa.Verify(value => value.SendEmailMfaCodeAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()), Times.Never);
        mfa.Verify(value => value.VerifyEmailMfaCodeAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (LoginEmailOtpModel Model, ApplicationUser User, Mock<IMfaService> Mfa) CreateModel(bool application = false)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = "email-user", Email = "email-user@example.test",
            SecurityStamp = "current-stamp", EmailMfaEnabled = true
        };
        var users = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        users.Setup(manager => manager.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        var signIn = new Mock<SignInManager<ApplicationUser>>(
            users.Object, Mock.Of<IHttpContextAccessor>(), Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            null!, null!, null!, null!);
        signIn.Setup(value => value.CanSignInAsync(user)).ReturnsAsync(true);
        var scheme = application ? IdentityConstants.ApplicationScheme : IdentityConstants.TwoFactorUserIdScheme;
        var principal = new ClaimsPrincipal(TwoFactorAuthenticationSession.CreateIdentity(user, users.Object));
        users.Setup(value => value.GetUserAsync(principal)).ReturnsAsync(user);
        var authentication = new Mock<IAuthenticationService>();
        authentication.Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.NoResult());
        authentication.Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), scheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, scheme)));
        var mfa = new Mock<IMfaService>();
        var localizer = new Mock<IStringLocalizer<SharedResource>>();
        localizer.Setup(value => value[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
        var model = new LoginEmailOtpModel(signIn.Object, users.Object, mfa.Object,
            Mock.Of<IUserManagementService>(), Mock.Of<IDomainEventPublisher>(), Mock.Of<ILogger<LoginEmailOtpModel>>(),
            localizer.Object,
            Mock.Of<IMigrationIssuanceGuard>(value => value.CanIssueAsync(user.Id, It.IsAny<CancellationToken>()) == Task.FromResult(true)),
            Mock.Of<ICurrentUserLifecycleEligibility>(value => value.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>()) == Task.FromResult(true)))
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = application ? principal : new ClaimsPrincipal(),
                    Session = new MemorySession(),
                    RequestServices = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider()
                }
            }
        };
        return (model, user, mfa);
    }
}
