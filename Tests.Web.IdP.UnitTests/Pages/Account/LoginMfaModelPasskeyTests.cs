using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Web.IdP.Helpers;
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
    [Fact]
    public void ReauthenticationNotice_ShouldRequireLiveServerPendingContextForMfaAndTotpPages()
    {
        var users = CreateUserManager();
        var signIn = CreateSignInManager(users.Object);
        signIn.Setup(value => value.CanSignInAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(true);
        var context = new PageContext { HttpContext = new DefaultHttpContext
            { Session = new Tests.Web.IdP.UnitTests.TestSupport.MemorySession() } };
        context.HttpContext.Request.QueryString = new QueryString("?reauthenticate=true&returnUrl=/Account/MfaSetup");
        var mfa = new LoginMfaModel(signIn.Object, users.Object, Mock.Of<IMfaService>(), Mock.Of<IUserManagementService>(),
            Mock.Of<IPasskeyService>(), Mock.Of<IDomainEventPublisher>(), Mock.Of<ILogger<LoginMfaModel>>(),
            Mock.Of<IStringLocalizer<SharedResource>>(), Mock.Of<IMigrationIssuanceGuard>(guard => guard.CanIssueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(true)), Mock.Of<ICurrentUserLifecycleEligibility>(guard => guard.IsEligibleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(true)))
            { PageContext = context, ReturnUrl = "/Account/MfaSetup" };
        var totp = new LoginTotpModel(signIn.Object, users.Object, Mock.Of<IMfaService>(), Mock.Of<IUserManagementService>(),
            Mock.Of<IDomainEventPublisher>(), Mock.Of<ILogger<LoginTotpModel>>(), Mock.Of<IStringLocalizer<SharedResource>>(),
            Mock.Of<IMigrationIssuanceGuard>(guard => guard.CanIssueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(true)), Mock.Of<ICurrentUserLifecycleEligibility>(guard => guard.IsEligibleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(true)))
            { PageContext = context, ReturnUrl = "/Account/MfaSetup" };

        Assert.False(mfa.IsMfaEnrollmentReauthentication);
        Assert.False(totp.IsMfaEnrollmentReauthentication);
        Assert.Equal("MfaEnrollmentReauthenticationNotice", mfa.MfaReauthenticationNoticeKey);
        MfaEnrollmentSession.Begin(context.HttpContext.Session, Guid.NewGuid(), requiresMfa: true,
            action: MfaEnrollmentAction.AddAuthenticator);
        Assert.True(mfa.IsMfaEnrollmentReauthentication);
        Assert.True(totp.IsMfaEnrollmentReauthentication);
        Assert.Equal("MfaReauthenticationAddAuthenticatorNotice", mfa.MfaReauthenticationNoticeKey);
        Assert.Equal(mfa.MfaReauthenticationNoticeKey, totp.MfaReauthenticationNoticeKey);
        MfaEnrollmentSession.Consume(context.HttpContext.Session);
        Assert.False(mfa.IsMfaEnrollmentReauthentication);
        Assert.False(totp.IsMfaEnrollmentReauthentication);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LoginPages_ShouldNotSignIn_WhenProofConsumptionFails(bool nativePage, bool recovery)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "proof-user", SecurityStamp = "current-stamp", TwoFactorEnabled = true };
        var users = CreateUserManager();
        users.Setup(x => x.AccessFailedAsync(user)).ReturnsAsync(IdentityResult.Success);
        var signIn = CreateSignInManager(users.Object);
        signIn.Setup(value => value.CanSignInAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(true);
        signIn.Setup(x => x.GetTwoFactorAuthenticationUserAsync()).ReturnsAsync(user);
        var mfa = new Mock<IMfaService>();
        // A failed service result represents invalid proof or unsuccessful durable consumption.
        mfa.Setup(x => x.ValidateTotpCodeAsync(user, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        mfa.Setup(x => x.ValidateRecoveryCodeAsync(user, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        mfa.Setup(x => x.ValidateNativeRecoveryCodeAsync(user, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var localizer = new Mock<IStringLocalizer<SharedResource>>();
        localizer.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
        var pageContext = new PageContext(new ActionContext(new DefaultHttpContext { Session = new Tests.Web.IdP.UnitTests.TestSupport.MemorySession() }, new RouteData(), new ActionDescriptor()));
        users.Setup(manager => manager.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.TwoFactorUserIdScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(TwoFactorAuthenticationSession.CreateIdentity(user, users.Object)), IdentityConstants.TwoFactorUserIdScheme)));
        pageContext.HttpContext.RequestServices = new ServiceCollection().AddSingleton(auth.Object).AddSingleton<Core.Application.IAccountRecoveryCodeService>(Moq.Mock.Of<Core.Application.IAccountRecoveryCodeService>(codes => codes.GetStatusAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<CancellationToken>()) == Task.FromResult(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(false, 0, 1, true)))).BuildServiceProvider();
        IActionResult result;
        if (nativePage)
        {
            var model = new LoginTotpModel(signIn.Object, users.Object, mfa.Object, Mock.Of<IUserManagementService>(),
                Mock.Of<IDomainEventPublisher>(), Mock.Of<ILogger<LoginTotpModel>>(), localizer.Object,
                Mock.Of<IMigrationIssuanceGuard>(guard => guard.CanIssueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(true)), Mock.Of<ICurrentUserLifecycleEligibility>(guard => guard.IsEligibleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(true)))
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
                Mock.Of<IMigrationIssuanceGuard>(guard => guard.CanIssueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(true)), Mock.Of<ICurrentUserLifecycleEligibility>(guard => guard.IsEligibleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(true)))
            { PageContext = pageContext, ReturnUrl = "/", Method = recovery ? "recovery" : "totp", Input = new LoginMfaModel.InputModel
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
            EmailMfaEnabled = false,
            SecurityStamp = "current"
        };
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                 new Claim(new IdentityOptions().ClaimsIdentity.SecurityStampClaimType, user.SecurityStamp!)],
                IdentityConstants.ApplicationScheme));

        var userManager = CreateUserManager();
        userManager
            .Setup(manager => manager.GetUserAsync(principal))
            .ReturnsAsync(user);

        var signInManager = CreateSignInManager(userManager.Object);
        signInManager.Setup(value => value.CanSignInAsync(user)).ReturnsAsync(true);
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
            Mock.Of<IMigrationIssuanceGuard>(value => value.CanIssueAsync(user.Id, It.IsAny<CancellationToken>()) == Task.FromResult(true)),
            Mock.Of<ICurrentUserLifecycleEligibility>(value => value.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>()) == Task.FromResult(true)));
        model.PageContext = new PageContext(
            new ActionContext(
                new DefaultHttpContext { User = principal },
                new RouteData(),
                new ActionDescriptor()));
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.NoResult());
        auth.Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, IdentityConstants.ApplicationScheme)));
        model.HttpContext.RequestServices = new ServiceCollection().AddSingleton(auth.Object).AddSingleton<Core.Application.IAccountRecoveryCodeService>(Moq.Mock.Of<Core.Application.IAccountRecoveryCodeService>(codes => codes.GetStatusAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<CancellationToken>()) == Task.FromResult(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(false, 0)))).BuildServiceProvider();

        var returnUrl = "/connect/authorize?client_id=testclient-public";

        var result = await model.OnGetAsync(returnUrl);

        Assert.IsType<PageResult>(result);
        Assert.True(model.PasskeyEnabled);
        Assert.False(model.TotpMfaEnabled);
        Assert.Equal(user.UserName, model.PasskeyUserName);
        Assert.Equal(returnUrl, model.ReturnUrl);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task AccountCode_ShouldRequireCurrentChallengeAndAddOnlyPerformedMfa(bool temporary, bool staleStamp)
    {
        using var db = new global::Infrastructure.ApplicationDbContext(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<global::Infrastructure.ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "code-only", SecurityStamp = "current", IsActive = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var codes = new global::Infrastructure.Services.AccountRecoveryCodeService(db);
        var batch = (await codes.GenerateAsync(user))!;
        var users = CreateUserManager();
        var scheme = temporary ? IdentityConstants.TwoFactorUserIdScheme : IdentityConstants.ApplicationScheme;
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(users.Object.Options.ClaimsIdentity.SecurityStampClaimType, user.SecurityStamp),
            new Claim("amr", "pwd")], scheme));
        users.Setup(value => value.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        users.Setup(value => value.GetUserAsync(principal)).ReturnsAsync(user);
        users.Setup(value => value.AccessFailedAsync(user)).ReturnsAsync(IdentityResult.Success);
        var signIn = CreateSignInManager(users.Object);
        signIn.Setup(value => value.CanSignInAsync(user)).ReturnsAsync(true);
        List<Claim>? issued = null;
        signIn.Setup(value => value.SignInWithClaimsAsync(user, false, It.IsAny<IEnumerable<Claim>>()))
            .Callback<ApplicationUser, bool, IEnumerable<Claim>>((_, _, claims) => issued = claims.ToList()).Returns(Task.CompletedTask);
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(value => value.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>())).ReturnsAsync(AuthenticateResult.NoResult());
        auth.Setup(value => value.AuthenticateAsync(It.IsAny<HttpContext>(), scheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, scheme)));
        using var services = new ServiceCollection().AddSingleton(auth.Object).AddSingleton<IAccountRecoveryCodeService>(codes).BuildServiceProvider();
        var http = new DefaultHttpContext { User = temporary ? new ClaimsPrincipal() : principal,
            RequestServices = services, Session = new Tests.Web.IdP.UnitTests.TestSupport.MemorySession() };
        var passkeys = Mock.Of<IPasskeyService>(value => value.GetUserPasskeysAsync(user.Id, It.IsAny<CancellationToken>()) == Task.FromResult(new List<UserCredentialDto>()));
        var localizer = new Mock<IStringLocalizer<SharedResource>>();
        localizer.Setup(value => value[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
        var page = new LoginMfaModel(signIn.Object, users.Object, Mock.Of<IMfaService>(), Mock.Of<IUserManagementService>(),
            passkeys, Mock.Of<IDomainEventPublisher>(), Mock.Of<ILogger<LoginMfaModel>>(), localizer.Object,
            Mock.Of<IMigrationIssuanceGuard>(value => value.CanIssueAsync(user.Id, It.IsAny<CancellationToken>()) == Task.FromResult(true)),
            Mock.Of<ICurrentUserLifecycleEligibility>(value => value.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>()) == Task.FromResult(true)))
        {
            PageContext = new PageContext(new ActionContext(http, new RouteData(), new ActionDescriptor())), ReturnUrl = "/Account/Profile",
            Input = new LoginMfaModel.InputModel { RecoveryCode = batch[0] }
        };
        if (staleStamp) { user.SecurityStamp = "retired"; await db.SaveChangesAsync(); }
        else
        {
            Assert.IsType<PageResult>(await page.OnGetAsync("/Account/Profile"));
            Assert.True(page.AccountRecoveryCodesAvailable);
            Assert.False(page.TotpMfaEnabled);
            Assert.False(page.PasskeyEnabled);
        }
        page.Method = "recovery";
        var result = await page.OnPostAsync();
        if (staleStamp)
        {
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Null(issued);
            Assert.Equal(10, (await codes.GetStatusAsync(user.Id)).RemainingCodes);
        }
        else
        {
            Assert.IsType<RedirectResult>(result);
            Assert.Equal(new[] { "mfa" }, issued!.Select(claim => claim.Value));
            Assert.Equal(9, (await codes.GetStatusAsync(user.Id)).RemainingCodes);
            issued = null;
            Assert.IsType<PageResult>(await page.OnPostAsync());
            Assert.Null(issued);
            users.Verify(value => value.AccessFailedAsync(user), Times.Once);
        }
    }

    [Theory]
    [InlineData(true, "totp")]
    [InlineData(false, "totp")]
    [InlineData(true, "recovery")]
    [InlineData(false, "recovery")]
    [InlineData(true, "passkey")]
    [InlineData(false, "passkey")]
    [InlineData(true, "email")]
    [InlineData(false, "email")]
    [InlineData(false, "unknown")]
    public async Task Selector_ShouldRequireAvailableMethodAndPreserveCeremonyContext(bool temporary, string method)
    {
        var (page, user, _, _) = CreateSelector(temporary, totp: true, email: true, passkey: true,
            new AccountRecoveryCodeStatusDto(true, 1));
        const string returnUrl = "/Account/MfaSetup?returnUrl=%2Fconnect%2Fauthorize%3Fclient_id%3Dclient";
        MfaEnrollmentSession.Begin(page.HttpContext.Session, user.Id, requiresMfa: true,
            securityStamp: user.SecurityStamp, action: MfaEnrollmentAction.AddAuthenticator);
        var pending = page.HttpContext.Session.GetString("MfaEnrollment:Pending");
        Assert.IsType<PageResult>(await page.OnGetAsync(returnUrl, true));
        Assert.Null(page.Method);
        page.Method = method;
        page.RecoveryPurpose = "sign-in";
        var result = await page.OnGetAsync(returnUrl, true);
        if (method == "email")
        {
            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("./LoginEmailOtp", redirect.PageName);
            Assert.Equal(returnUrl, redirect.RouteValues!["returnUrl"]);
            Assert.Equal(true, redirect.RouteValues["rememberMe"]);
            Assert.Equal("sign-in", redirect.RouteValues["recoveryPurpose"]);
        }
        else
        {
            Assert.IsType<PageResult>(result);
            Assert.Equal(method == "unknown" ? null : method, page.Method);
        }
        Assert.Equal(returnUrl, page.ReturnUrl);
        Assert.True(page.RememberMe);
        Assert.Equal("sign-in", page.Input.RecoveryPurpose);
        Assert.Equal(pending, page.HttpContext.Session.GetString("MfaEnrollment:Pending"));
        Assert.Equal("MfaReauthenticationAddAuthenticatorNotice", page.MfaReauthenticationNoticeKey);
    }

    [Theory]
    [InlineData(false, 0, 0, false)]
    [InlineData(true, 0, 0, false)]
    [InlineData(true, 1, 0, true)]
    [InlineData(true, 0, 1, true)]
    public async Task Selector_ShouldUseLiveCodesInsteadOfBatchHistoryOrOtpFlags(bool hasBatch, int account, int legacy, bool available)
    {
        var (page, _, users, mfa) = CreateSelector(false, totp: legacy > 0, email: false, passkey: false,
            new AccountRecoveryCodeStatusDto(hasBatch, account, legacy, legacy > 0));
        page.Method = "recovery";
        Assert.IsType<PageResult>(await page.OnGetAsync("/continue"));
        Assert.Equal(available, page.RecoveryCodesAvailable);
        Assert.Equal(account > 0, page.IsRecoveryOnly);
        Assert.Equal(available ? "recovery" : null, page.Method);
        page.Method = "recovery";
        page.Input.RecoveryCode = "saved-code";
        if (!available)
        {
            Assert.IsType<PageResult>(await page.OnPostAsync());
            users.Verify(value => value.AccessFailedAsync(It.IsAny<ApplicationUser>()), Times.Never);
            mfa.Verify(value => value.ValidateTotpCodeAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Fact]
    public async Task EmailOnlyAddAuthenticator_ShouldOfferEmailWithoutEmptyTotpOrRecoverySubmit()
    {
        var (page, user, _, _) = CreateSelector(false, false, true, false, new AccountRecoveryCodeStatusDto(true, 0));
        MfaEnrollmentSession.Begin(page.HttpContext.Session, user.Id, requiresMfa: true,
            securityStamp: user.SecurityStamp, action: MfaEnrollmentAction.AddAuthenticator);
        page.Method = "totp";
        Assert.IsType<PageResult>(await page.OnGetAsync("/Account/MfaSetup", true));
        Assert.Null(page.Method);
        Assert.False(page.TotpMfaEnabled);
        Assert.False(page.RecoveryCodesAvailable);
        Assert.True(page.EmailMfaEnabled);
        page.Method = "email";
        Assert.Equal("./LoginEmailOtp", Assert.IsType<RedirectToPageResult>(
            await page.OnGetAsync("/Account/MfaSetup", true)).PageName);
    }

    [Theory]
    [InlineData("totp", true)]
    [InlineData("recovery", true)]
    [InlineData(null, false)]
    [InlineData("email", false)]
    public async Task Post_ShouldRejectMixedOrUnselectedMethodWithoutSpendingAttempt(string? method, bool mixed)
    {
        var (page, _, users, mfa) = CreateSelector(true, true, true, false, new AccountRecoveryCodeStatusDto(true, 1));
        page.ReturnUrl = "/continue";
        page.Method = method;
        page.Input = new() { TotpCode = "123456", RecoveryCode = mixed ? "saved-code" : null };
        Assert.IsType<PageResult>(await page.OnPostAsync());
        Assert.Null(page.Method);
        users.Verify(value => value.AccessFailedAsync(It.IsAny<ApplicationUser>()), Times.Never);
        mfa.Verify(value => value.ValidateTotpCodeAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (LoginMfaModel Page, ApplicationUser User, Mock<UserManager<ApplicationUser>> Users, Mock<IMfaService> Mfa)
        CreateSelector(bool temporary, bool totp, bool email, bool passkey, AccountRecoveryCodeStatusDto status)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "selector-user", SecurityStamp = "current",
            TwoFactorEnabled = totp, EmailMfaEnabled = email, IsActive = true };
        var users = CreateUserManager();
        var signIn = CreateSignInManager(users.Object);
        signIn.Setup(value => value.CanSignInAsync(user)).ReturnsAsync(true);
        var scheme = temporary ? IdentityConstants.TwoFactorUserIdScheme : IdentityConstants.ApplicationScheme;
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(users.Object.Options.ClaimsIdentity.SecurityStampClaimType, user.SecurityStamp)], scheme));
        users.Setup(value => value.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        users.Setup(value => value.GetUserAsync(principal)).ReturnsAsync(user);
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(value => value.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>())).ReturnsAsync(AuthenticateResult.NoResult());
        auth.Setup(value => value.AuthenticateAsync(It.IsAny<HttpContext>(), scheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, scheme)));
        var codes = Mock.Of<IAccountRecoveryCodeService>(value => value.GetStatusAsync(user.Id, It.IsAny<CancellationToken>()) == Task.FromResult(status));
        var passkeys = new Mock<IPasskeyService>();
        passkeys.Setup(value => value.GetUserPasskeysAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(passkey ? [new UserCredentialDto { Id = 1 }] : []);
        var mfa = new Mock<IMfaService>();
        var localizer = new Mock<IStringLocalizer<SharedResource>>();
        localizer.Setup(value => value[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
        var http = new DefaultHttpContext { User = temporary ? new ClaimsPrincipal() : principal,
            Session = new Tests.Web.IdP.UnitTests.TestSupport.MemorySession(),
            RequestServices = new ServiceCollection().AddSingleton(auth.Object).AddSingleton(codes).BuildServiceProvider() };
        var page = new LoginMfaModel(signIn.Object, users.Object, mfa.Object, Mock.Of<IUserManagementService>(), passkeys.Object,
            Mock.Of<IDomainEventPublisher>(), Mock.Of<ILogger<LoginMfaModel>>(), localizer.Object,
            Mock.Of<IMigrationIssuanceGuard>(value => value.CanIssueAsync(user.Id, It.IsAny<CancellationToken>()) == Task.FromResult(true)),
            Mock.Of<ICurrentUserLifecycleEligibility>(value => value.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>()) == Task.FromResult(true)))
            { PageContext = new PageContext(new ActionContext(http, new RouteData(), new ActionDescriptor())) };
        return (page, user, users, mfa);
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
