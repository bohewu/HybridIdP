using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using Web.IdP.Helpers;
using Core.Application;
using Core.Application.Interfaces;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Core.Domain.Events;
using Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP;
using Web.IdP.Pages.Account;
using Web.IdP.Services;

namespace Tests.Web.IdP.UnitTests.Pages.Account;

public sealed class MigrationPageIssuanceGateTests
{
    [Theory]
    [InlineData(Core.Application.DTOs.LoginStatus.InvalidCredentials)]
    [InlineData(Core.Application.DTOs.LoginStatus.LockedOut)]
    [InlineData(Core.Application.DTOs.LoginStatus.UserInactive)]
    [InlineData(Core.Application.DTOs.LoginStatus.PersonInactive)]
    public async Task Login_ShouldUseSamePublicMessageForPreAuthenticationFailures(Core.Application.DTOs.LoginStatus status)
    {
        var user = CreateUser();
        var identity = CreateIdentity(user);
        identity.SignInManager.Setup(manager => manager.GetExternalAuthenticationSchemesAsync()).ReturnsAsync([]);
        var login = new Mock<ILoginService>();
        var failure = status switch
        {
            Core.Application.DTOs.LoginStatus.LockedOut => Core.Application.DTOs.LoginResult.LockedOut(),
            Core.Application.DTOs.LoginStatus.UserInactive => Core.Application.DTOs.LoginResult.UserInactive(),
            Core.Application.DTOs.LoginStatus.PersonInactive => Core.Application.DTOs.LoginResult.PersonInactive(),
            _ => Core.Application.DTOs.LoginResult.InvalidCredentials()
        };
        login.Setup(service => service.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(failure);
        var localizer = new Mock<IStringLocalizer<SharedResource>>();
        localizer.Setup(value => value[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
        var policy = new Mock<ISecurityPolicyService>();
        policy.Setup(service => service.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy());
        var model = new LoginModel(CreateLifecycleEligibility(user.Id, true).Object, identity.SignInManager.Object, identity.UserManager.Object,
            login.Object, Mock.Of<ITurnstileService>(), Mock.Of<ILoginHistoryService>(), Mock.Of<INotificationService>(),
            policy.Object, CreateEventPublisher().Object,
            Microsoft.Extensions.Options.Options.Create(new Core.Application.Options.TurnstileOptions()),
            Mock.Of<ILogger<LoginModel>>(), localizer.Object, Mock.Of<ILocalizationService>(),
            Microsoft.Extensions.Options.Options.Create(new global::Web.IdP.Options.LoginNoticesOptions()),
            Mock.Of<ITurnstileStateService>(), Mock.Of<ISettingsService>(), CreatePasskeyService().Object,
            CreateUserManagementService().Object, Mock.Of<OpenIddict.Abstractions.IOpenIddictApplicationManager>(),
            CreateMigrationGuard(user.Id, true).Object,
            new Infrastructure.Services.ForgotPasswordRoutingEvaluator(
                Microsoft.Extensions.Options.Options.Create(new Infrastructure.Options.ForgotPasswordRecoveryOptions())))
        { Input = new LoginModel.InputModel { Login = "probe", Password = "${LOGIN_PROBE_TEST_001}" } };
        SetHttpContext(model);
        var url = new Mock<IUrlHelper>();
        url.Setup(helper => helper.IsLocalUrl(It.IsAny<string>())).Returns(true);
        model.Url = url.Object;
        Assert.IsType<PageResult>(await model.OnPostAsync("/continue"));
        Assert.Equal("InvalidLoginAttempt", Assert.Single(model.ModelState.Values.SelectMany(value => value.Errors)).ErrorMessage);
        identity.SignInManager.Verify(manager => manager.SignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never());
        identity.SignInManager.Verify(manager => manager.SignInWithClaimsAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>(), It.IsAny<IEnumerable<Claim>>()), Times.Never());
    }
    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task PasswordLogin_ShouldCheckCurrentLifecycleImmediatelyBeforeBothFullCookieBranches(bool grace, bool allowed)
    {
        var user = CreateUser();
        user.MfaRequirementNotifiedAt = DateTime.UtcNow;
        var identity = CreateIdentity(user);
        identity.SignInManager.Setup(manager => manager.GetExternalAuthenticationSchemesAsync()).ReturnsAsync([]);
        var login = new Mock<ILoginService>();
        login.Setup(service => service.AuthenticateAsync("user", "${MIGRATION_TEST_001}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Core.Application.DTOs.LoginResult.Success(user));
        var lifecycle = CreateLifecycleEligibility(user.Id, true);
        lifecycle.SetupSequence(service => service.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true).ReturnsAsync(allowed);
        var policy = new Mock<ISecurityPolicyService>();
        policy.Setup(service => service.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy
        { EnforceMandatoryMfaEnrollment = grace, MfaEnforcementGracePeriodDays = 7 });
        var localizer = new Mock<IStringLocalizer<SharedResource>>();
        localizer.Setup(value => value[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
        var model = new LoginModel(lifecycle.Object, identity.SignInManager.Object, identity.UserManager.Object,
            login.Object, Mock.Of<ITurnstileService>(), Mock.Of<ILoginHistoryService>(), Mock.Of<INotificationService>(),
            policy.Object, CreateEventPublisher().Object,
            Microsoft.Extensions.Options.Options.Create(new Core.Application.Options.TurnstileOptions()),
            Mock.Of<ILogger<LoginModel>>(), localizer.Object, Mock.Of<ILocalizationService>(),
            Microsoft.Extensions.Options.Options.Create(new global::Web.IdP.Options.LoginNoticesOptions()),
            Mock.Of<ITurnstileStateService>(), Mock.Of<ISettingsService>(), CreatePasskeyService().Object,
            CreateUserManagementService().Object, Mock.Of<OpenIddict.Abstractions.IOpenIddictApplicationManager>(),
            CreateMigrationGuard(user.Id, true).Object,
            new global::Infrastructure.Services.ForgotPasswordRoutingEvaluator(
                Microsoft.Extensions.Options.Options.Create(new global::Infrastructure.Options.ForgotPasswordRecoveryOptions())))
        { Input = new LoginModel.InputModel { Login = "user", Password = "${MIGRATION_TEST_001}" } };
        SetHttpContext(model);
        var url = new Mock<IUrlHelper>();
        url.Setup(helper => helper.IsLocalUrl(It.IsAny<string>())).Returns(true);
        model.Url = url.Object;
        var result = await model.OnPostAsync("/continue");
        if (allowed) Assert.IsType<RedirectResult>(result);
        else Assert.IsType<PageResult>(result);
        identity.SignInManager.Verify(manager => manager.SignInWithClaimsAsync(user, false,
            It.IsAny<IEnumerable<System.Security.Claims.Claim>>()), allowed ? Times.Once() : Times.Never());
        lifecycle.Verify(service => service.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>()), Times.Exactly(2));
        login.Verify(service => service.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Registration_ShouldPreserveCreationAndGateOnlyFullCookie(bool allowed)
    {
        await using var database = CreateDatabase();
        var identity = CreateIdentity(CreateUser());
        identity.UserManager.Setup(manager => manager.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        identity.UserManager.Setup(manager => manager.AddToRoleAsync(It.IsAny<ApplicationUser>(), "User"))
            .ReturnsAsync(IdentityResult.Success);
        var lifecycle = new Mock<ICurrentUserLifecycleEligibility>();
        lifecycle.Setup(service => service.IsEligibleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(allowed);
        var model = new RegisterModel(lifecycle.Object, identity.UserManager.Object, identity.SignInManager.Object,
            Mock.Of<ITurnstileService>(), Microsoft.Extensions.Options.Options.Create(new Core.Application.Options.TurnstileOptions()),
            Mock.Of<ILogger<RegisterModel>>(), database, Mock.Of<IAuditService>(), Mock.Of<ISettingsService>(),
            Mock.Of<ITurnstileStateService>(), Mock.Of<ISecurityPolicyService>(), Mock.Of<IStringLocalizer<SharedResource>>())
        { Input = new RegisterModel.InputModel { Email = "new@example.invalid", Password = "${MIGRATION_TEST_002}", ConfirmPassword = "${MIGRATION_TEST_002}" } };
        SetHttpContext(model);
        var url = new Mock<IUrlHelper>();
        url.Setup(helper => helper.IsLocalUrl(It.IsAny<string>())).Returns(true);
        model.Url = url.Object;
        var result = await model.OnPostAsync("/continue");
        if (allowed) Assert.IsType<RedirectResult>(result);
        else Assert.IsType<RedirectToPageResult>(result);
        identity.UserManager.Verify(manager => manager.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Once);
        identity.SignInManager.Verify(manager => manager.SignInWithClaimsAsync(It.IsAny<ApplicationUser>(), false,
            It.IsAny<IEnumerable<System.Security.Claims.Claim>>()), allowed ? Times.Once() : Times.Never());
        Assert.Single(await database.Persons.ToListAsync());
    }

    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(true, 7, true)]
    [InlineData(false, 0, true)]
    [InlineData(true, 0, false)]
    public async Task Registration_ShouldEnforceMandatoryEnrollmentAndPreserveGrace(bool mandatory, int graceDays, bool saved)
    {
        await using var database = CreateDatabase();
        var identity = CreateIdentity(CreateUser());
        identity.UserManager.Setup(manager => manager.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .Callback<ApplicationUser, string>((created, _) => created.SecurityStamp = Guid.NewGuid().ToString())
            .ReturnsAsync(IdentityResult.Success);
        identity.UserManager.Setup(manager => manager.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(saved ? IdentityResult.Success : IdentityResult.Failed(new IdentityError { Code = "failed" }));
        var policy = new Mock<ISecurityPolicyService>();
        policy.Setup(service => service.GetCurrentPolicyAsync()).ReturnsAsync(new Core.Domain.Entities.SecurityPolicy
        { EnforceMandatoryMfaEnrollment = mandatory, MfaEnforcementGracePeriodDays = graceDays });
        var lifecycle = new Mock<ICurrentUserLifecycleEligibility>();
        lifecycle.Setup(service => service.IsEligibleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var model = new RegisterModel(lifecycle.Object, identity.UserManager.Object, identity.SignInManager.Object,
            Mock.Of<ITurnstileService>(), Microsoft.Extensions.Options.Options.Create(new Core.Application.Options.TurnstileOptions()),
            Mock.Of<ILogger<RegisterModel>>(), database, Mock.Of<IAuditService>(), Mock.Of<ISettingsService>(),
            Mock.Of<ITurnstileStateService>(), policy.Object, Mock.Of<IStringLocalizer<SharedResource>>())
        { Input = new RegisterModel.InputModel { Email = "new@example.invalid", Password = "${REGISTRATION_TEST_001}", ConfirmPassword = "${REGISTRATION_TEST_001}" } };
        SetHttpContext(model);
        var authentication = new Mock<IAuthenticationService>();
        using var services = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider();
        model.HttpContext.RequestServices = services;
        var url = new Mock<IUrlHelper>();
        url.Setup(helper => helper.IsLocalUrl(It.IsAny<string>())).Returns(true);
        model.Url = url.Object;
        var result = await model.OnPostAsync("/continue");
        var fullSignIn = !mandatory || (saved && graceDays > 0);
        identity.SignInManager.Verify(manager => manager.SignInWithClaimsAsync(It.IsAny<ApplicationUser>(), false,
            It.IsAny<IEnumerable<System.Security.Claims.Claim>>()), fullSignIn ? Times.Once() : Times.Never());
        identity.SignInManager.Verify(manager => manager.SignInAsync(It.IsAny<ApplicationUser>(), false, null), Times.Never());
        if (mandatory)
            identity.UserManager.Verify(manager => manager.UpdateAsync(It.Is<ApplicationUser>(user => user.MfaRequirementNotifiedAt != null)), Times.Once());
        if (fullSignIn) Assert.IsType<RedirectResult>(result);
        else Assert.Equal(saved ? "./MfaSetup" : "./Login", Assert.IsType<RedirectToPageResult>(result).PageName);
        authentication.Verify(service => service.SignInAsync(It.IsAny<HttpContext>(), IdentityConstants.TwoFactorUserIdScheme,
            It.Is<ClaimsPrincipal>(principal => principal.HasClaim(claim => claim.Type == MfaEnrollmentSession.InitialPurposeClaim)),
            It.IsAny<AuthenticationProperties>()), mandatory && saved && graceDays == 0 ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task LoginTotp_ShouldDeny_WhenLifecycleExpiresDuringMigrationGuard()
    {
        var user = CreateUser();
        var identity = CreateIdentity(user);
        identity.UserManager.Setup(manager => manager.IsLockedOutAsync(user)).ReturnsAsync(false);
        var mfaService = new Mock<IMfaService>();
        mfaService.Setup(service => service.ValidateTotpCodeAsync(user, "123456")).ReturnsAsync(true);
        var lifecycle = CreateLifecycleEligibility(user.Id, eligible: true);
        var guard = CreateMigrationGuard(user.Id, allowed: true);
        lifecycle.SetupSequence(service => service.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true).ReturnsAsync(false);
        var userManagement = CreateUserManagementService();
        var publisher = CreateEventPublisher();
        var model = new LoginTotpModel(
            identity.SignInManager.Object,
            identity.UserManager.Object,
            mfaService.Object,
            userManagement.Object,
            publisher.Object,
            Mock.Of<ILogger<LoginTotpModel>>(),
            Mock.Of<IStringLocalizer<SharedResource>>(),
            guard.Object,
            lifecycle.Object)
        {
            Input = new LoginTotpModel.InputModel { TotpCode = "123456" },
            RememberMe = true,
            ReturnUrl = "/continue"
        };
        SetMfaHttpContext(model, user);

        var result = await model.OnPostAsync();

        Assert.IsType<RedirectToPageResult>(result);
        identity.SignInManager.Verify(
            manager => manager.SignInWithClaimsAsync(
                user,
                true,
                It.IsAny<IEnumerable<System.Security.Claims.Claim>>()),
            Times.Never);
    }

    [Fact]
    public async Task LoginTotp_WithEligibleCurrentUserAndFinalizedMigration_IssuesFullCookie()
    {
        var user = CreateUser();
        var identity = CreateIdentity(user);
        identity.UserManager.Setup(manager => manager.IsLockedOutAsync(user)).ReturnsAsync(false);
        var mfaService = new Mock<IMfaService>();
        mfaService.Setup(service => service.ValidateTotpCodeAsync(user, "123456")).ReturnsAsync(true);
        var lifecycle = CreateLifecycleEligibility(user.Id, eligible: true);
        var guard = CreateMigrationGuard(user.Id, allowed: true);
        var sequence = new MockSequence();
        lifecycle.InSequence(sequence)
            .Setup(service => service.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        guard.InSequence(sequence)
            .Setup(service => service.CanIssueAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        identity.SignInManager.InSequence(sequence)
            .Setup(manager => manager.SignInWithClaimsAsync(
                user,
                true,
                It.IsAny<IEnumerable<System.Security.Claims.Claim>>()))
            .Returns(Task.CompletedTask);
        var userManagement = CreateUserManagementService();
        var publisher = CreateEventPublisher();
        var model = new LoginTotpModel(
            identity.SignInManager.Object,
            identity.UserManager.Object,
            mfaService.Object,
            userManagement.Object,
            publisher.Object,
            Mock.Of<ILogger<LoginTotpModel>>(),
            Mock.Of<IStringLocalizer<SharedResource>>(),
            guard.Object,
            lifecycle.Object)
        {
            Input = new LoginTotpModel.InputModel { TotpCode = "123456" },
            RememberMe = true,
            ReturnUrl = "/continue"
        };
        SetMfaHttpContext(model, user);

        var result = await model.OnPostAsync();

        Assert.IsType<RedirectResult>(result);
        identity.SignInManager.Verify(
            manager => manager.SignInWithClaimsAsync(
                user,
                true,
                It.IsAny<IEnumerable<System.Security.Claims.Claim>>()),
            Times.Once);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task LoginTotp_ShouldConsumeCurrentRecoveryCodesAndPreserveNativeCompatibility(
        bool customCodeAccepted, bool nativeCodeAccepted)
    {
        var user = CreateUser();
        user.TwoFactorEnabled = true;
        var identity = CreateIdentity(user);
        identity.UserManager.Setup(manager => manager.AccessFailedAsync(user)).ReturnsAsync(IdentityResult.Success);
        var mfa = new Mock<IMfaService>();
        mfa.Setup(service => service.ValidateRecoveryCodeAsync(user, "ABCDE12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(customCodeAccepted);
        mfa.Setup(service => service.ValidateNativeRecoveryCodeAsync(user, "ABCDE12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(nativeCodeAccepted);
        var localizer = new Mock<IStringLocalizer<SharedResource>>();
        localizer.Setup(candidate => candidate[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
        var model = new LoginTotpModel(identity.SignInManager.Object, identity.UserManager.Object,
            mfa.Object, CreateUserManagementService().Object, CreateEventPublisher().Object,
            Mock.Of<ILogger<LoginTotpModel>>(), localizer.Object,
            CreateMigrationGuard(user.Id, true).Object, CreateLifecycleEligibility(user.Id, true).Object)
        {
            Input = new LoginTotpModel.InputModel { RecoveryCode = "ABCDE-12345" },
            ReturnUrl = "/continue"
        };
        SetMfaHttpContext(model, user);

        var result = await model.OnPostAsync();

        if (customCodeAccepted || nativeCodeAccepted) Assert.IsType<RedirectResult>(result);
        else Assert.IsType<PageResult>(result);
        mfa.Verify(service => service.ValidateRecoveryCodeAsync(user, "ABCDE12345", It.IsAny<CancellationToken>()), Times.Once);
        mfa.Verify(service => service.ValidateNativeRecoveryCodeAsync(user, "ABCDE12345", It.IsAny<CancellationToken>()),
            customCodeAccepted ? Times.Never() : Times.Once());
        identity.SignInManager.Verify(manager => manager.SignInWithClaimsAsync(user, false, It.IsAny<IEnumerable<Claim>>()),
            customCodeAccepted || nativeCodeAccepted ? Times.Once() : Times.Never());
        identity.UserManager.Verify(manager => manager.AccessFailedAsync(user),
            customCodeAccepted || nativeCodeAccepted ? Times.Never() : Times.Once());
    }

    [Fact]
    public async Task LoginTotp_WithPreexistingPartialAuthenticationAndIncompleteMigration_DeniesFullCookie()
    {
        var user = CreateUser();
        var identity = CreateIdentity(user);
        identity.UserManager.Setup(manager => manager.IsLockedOutAsync(user)).ReturnsAsync(false);
        var mfaService = new Mock<IMfaService>();
        mfaService.Setup(service => service.ValidateTotpCodeAsync(user, "123456")).ReturnsAsync(true);
        var lifecycle = CreateLifecycleEligibility(user.Id, eligible: true);
        var guard = CreateMigrationGuard(user.Id, allowed: false);
        var userManagement = CreateUserManagementService();
        var publisher = CreateEventPublisher();
        var model = new LoginTotpModel(
            identity.SignInManager.Object,
            identity.UserManager.Object,
            mfaService.Object,
            userManagement.Object,
            publisher.Object,
            Mock.Of<ILogger<LoginTotpModel>>(),
            Mock.Of<IStringLocalizer<SharedResource>>(),
            guard.Object,
            lifecycle.Object)
        {
            Input = new LoginTotpModel.InputModel { TotpCode = "123456" },
            ReturnUrl = "/continue"
        };
        SetMfaHttpContext(model, user);

        var result = await model.OnPostAsync();

        Assert.IsType<RedirectToPageResult>(result);
        identity.SignInManager.Verify(
            manager => manager.SignInWithClaimsAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<bool>(),
                It.IsAny<IEnumerable<System.Security.Claims.Claim>>()),
            Times.Never);
        userManagement.Verify(
            service => service.UpdateLastLoginAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        publisher.Verify(service => service.PublishAsync(It.IsAny<LoginAttemptEvent>()), Times.Never);
    }

    [Fact]
    public async Task LoginMfa_WithEligibleCurrentUserAndFinalizedMigration_IssuesFullCookie()
    {
        var user = CreateUser();
        var identity = CreateIdentity(user);
        identity.UserManager.Setup(manager => manager.IsLockedOutAsync(user)).ReturnsAsync(false);
        var mfaService = new Mock<IMfaService>();
        mfaService.Setup(service => service.ValidateTotpCodeAsync(user, "123456")).ReturnsAsync(true);
        var lifecycle = CreateLifecycleEligibility(user.Id, eligible: true);
        var guard = CreateMigrationGuard(user.Id, allowed: true);
        var sequence = new MockSequence();
        lifecycle.InSequence(sequence)
            .Setup(service => service.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        guard.InSequence(sequence)
            .Setup(service => service.CanIssueAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        identity.SignInManager.InSequence(sequence)
            .Setup(manager => manager.SignInWithClaimsAsync(
                user,
                true,
                It.IsAny<IEnumerable<System.Security.Claims.Claim>>()))
            .Returns(Task.CompletedTask);
        var userManagement = CreateUserManagementService();
        var model = new LoginMfaModel(
            identity.SignInManager.Object,
            identity.UserManager.Object,
            mfaService.Object,
            userManagement.Object,
            CreatePasskeyService().Object,
            CreateEventPublisher().Object,
            Mock.Of<ILogger<LoginMfaModel>>(),
            Mock.Of<IStringLocalizer<SharedResource>>(),
            guard.Object,
            lifecycle.Object)
        {
            Input = new LoginMfaModel.InputModel { TotpCode = "123456" },
            RememberMe = true,
            ReturnUrl = "/continue"
        };
        SetMfaHttpContext(model, user);

        var result = await model.OnPostAsync();

        Assert.IsType<RedirectResult>(result);
        identity.SignInManager.Verify(
            manager => manager.SignInWithClaimsAsync(
                user,
                true,
                It.IsAny<IEnumerable<System.Security.Claims.Claim>>()),
            Times.Once);
    }

    [Fact]
    public async Task LoginMfa_WhenCurrentPersonIsIneligibleAfterPartialAuthentication_DeniesFullCookie()
    {
        var user = CreateUser();
        var identity = CreateIdentity(user);
        identity.UserManager.Setup(manager => manager.IsLockedOutAsync(user)).ReturnsAsync(false);
        var mfaService = new Mock<IMfaService>();
        mfaService.Setup(service => service.ValidateTotpCodeAsync(user, "123456")).ReturnsAsync(true);
        var lifecycle = CreateLifecycleEligibility(user.Id, eligible: false);
        var guard = CreateMigrationGuard(user.Id, allowed: true);
        var model = new LoginMfaModel(
            identity.SignInManager.Object,
            identity.UserManager.Object,
            mfaService.Object,
            CreateUserManagementService().Object,
            CreatePasskeyService().Object,
            CreateEventPublisher().Object,
            Mock.Of<ILogger<LoginMfaModel>>(),
            Mock.Of<IStringLocalizer<SharedResource>>(),
            guard.Object,
            lifecycle.Object)
        {
            Input = new LoginMfaModel.InputModel { TotpCode = "123456" },
            ReturnUrl = "/continue"
        };
        SetMfaHttpContext(model, user);

        var result = await model.OnPostAsync();

        Assert.IsType<RedirectToPageResult>(result);
        guard.Verify(
            service => service.CanIssueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        identity.SignInManager.Verify(
            manager => manager.SignInWithClaimsAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<bool>(),
                It.IsAny<IEnumerable<System.Security.Claims.Claim>>()),
            Times.Never);
    }

    [Fact]
    public async Task LoginEmailOtp_WithEligibleCurrentUserAndFinalizedMigration_IssuesFullCookie()
    {
        var user = CreateUser();
        var identity = CreateIdentity(user);
        var mfaService = new Mock<IMfaService>();
        mfaService.Setup(service => service.VerifyEmailMfaCodeAsync(user, "123456")).ReturnsAsync(true);
        var lifecycle = CreateLifecycleEligibility(user.Id, eligible: true);
        var guard = CreateMigrationGuard(user.Id, allowed: true);
        var sequence = new MockSequence();
        lifecycle.InSequence(sequence)
            .Setup(service => service.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        guard.InSequence(sequence)
            .Setup(service => service.CanIssueAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        identity.SignInManager.InSequence(sequence)
            .Setup(manager => manager.SignInWithClaimsAsync(
                user,
                true,
                It.IsAny<IEnumerable<System.Security.Claims.Claim>>()))
            .Returns(Task.CompletedTask);
        var userManagement = CreateUserManagementService();
        var model = new LoginEmailOtpModel(
            identity.SignInManager.Object,
            identity.UserManager.Object,
            mfaService.Object,
            userManagement.Object,
            CreateEventPublisher().Object,
            Mock.Of<ILogger<LoginEmailOtpModel>>(),
            Mock.Of<IStringLocalizer<SharedResource>>(),
            guard.Object,
            lifecycle.Object)
        {
            Input = new LoginEmailOtpModel.InputModel { EmailCode = "123456" },
            RememberMe = true,
            ReturnUrl = "/continue"
        };
        SetMfaHttpContext(model, user);

        var result = await model.OnPostAsync();

        Assert.IsType<RedirectResult>(result);
        identity.SignInManager.Verify(
            manager => manager.SignInWithClaimsAsync(
                user,
                true,
                It.IsAny<IEnumerable<System.Security.Claims.Claim>>()),
            Times.Once);
    }

    [Fact]
    public async Task LoginEmailOtp_WhenCurrentUserIsDeletedAfterPartialAuthentication_DeniesFullCookie()
    {
        var user = CreateUser();
        var identity = CreateIdentity(user);
        var mfaService = new Mock<IMfaService>();
        mfaService.Setup(service => service.VerifyEmailMfaCodeAsync(user, "123456")).ReturnsAsync(true);
        var lifecycle = CreateLifecycleEligibility(user.Id, eligible: false);
        var model = new LoginEmailOtpModel(
            identity.SignInManager.Object,
            identity.UserManager.Object,
            mfaService.Object,
            CreateUserManagementService().Object,
            CreateEventPublisher().Object,
            Mock.Of<ILogger<LoginEmailOtpModel>>(),
            Mock.Of<IStringLocalizer<SharedResource>>(),
            CreateMigrationGuard(user.Id, allowed: true).Object,
            lifecycle.Object)
        {
            Input = new LoginEmailOtpModel.InputModel { EmailCode = "123456" },
            ReturnUrl = "/continue"
        };
        SetMfaHttpContext(model, user);

        var result = await model.OnPostAsync();

        Assert.IsType<RedirectToPageResult>(result);
        identity.SignInManager.Verify(
            manager => manager.SignInWithClaimsAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<bool>(),
                It.IsAny<IEnumerable<System.Security.Claims.Claim>>()),
            Times.Never);
    }

    [Fact]
    public async Task MfaSetup_WithEligibleCurrentUserAndFinalizedMigration_IssuesFullCookie()
    {
        var user = CreateUser();
        var identity = CreateIdentity(user);
        var lifecycle = CreateLifecycleEligibility(user.Id, eligible: true);
        var guard = CreateMigrationGuard(user.Id, allowed: true);
        var sequence = new MockSequence();
        lifecycle.InSequence(sequence)
            .Setup(service => service.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        guard.InSequence(sequence)
            .Setup(service => service.CanIssueAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        identity.SignInManager.InSequence(sequence)
            .Setup(manager => manager.SignInWithClaimsAsync(user, false, It.IsAny<IEnumerable<Claim>>()))
            .Returns(Task.CompletedTask);
        var policy = new Mock<ISecurityPolicyService>();
        policy.Setup(service => service.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy());
        var model = new MfaSetupModel(
            identity.SignInManager.Object,
            identity.UserManager.Object,
            Mock.Of<IStringLocalizer<SharedResource>>(),
            policy.Object,
            guard.Object,
            lifecycle.Object, CreatePasskeyService().Object)
        {
            ReturnUrl = "/continue"
        };
        SetMfaHttpContext(model, user);

        var result = await model.OnPostSkipAsync();

        Assert.IsType<RedirectResult>(result);
        identity.SignInManager.Verify(manager => manager.SignInWithClaimsAsync(user, false, It.IsAny<IEnumerable<Claim>>()), Times.Once);
    }

    [Theory]
    [InlineData("challenge")]
    [InlineData("factor")]
    [InlineData("acr")]
    [InlineData("expired")]
    public async Task MfaSetup_ShouldDenySkipWithoutEligibleInitialEnrollment(string scenario)
    {
        var user = CreateUser();
        var identity = CreateIdentity(user);
        var policy = new Mock<ISecurityPolicyService>();
        policy.Setup(s => s.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy
        {
            EnforceMandatoryMfaEnrollment = scenario == "expired", MfaEnforcementGracePeriodDays = 0
        });
        var model = new MfaSetupModel(identity.SignInManager.Object, identity.UserManager.Object,
            Mock.Of<IStringLocalizer<SharedResource>>(), policy.Object,
            CreateMigrationGuard(user.Id, true).Object, CreateLifecycleEligibility(user.Id, true).Object,
            CreatePasskeyService().Object);
        SetMfaHttpContext(model, user);
        if (scenario == "challenge") MfaEnrollmentSession.Consume(model.HttpContext.Session);
        if (scenario == "factor") user.TwoFactorEnabled = true;
        if (scenario == "acr") model.HttpContext.Session.SetString("MfaEnforcedByAcr", "true");
        if (scenario == "expired") user.MfaRequirementNotifiedAt = DateTime.UtcNow.AddDays(-1);
        var result = await model.OnPostSkipAsync();
        Assert.True(result is StatusCodeResult { StatusCode: 403 } or PageResult);
        identity.SignInManager.Verify(s => s.SignInWithClaimsAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>(), It.IsAny<IEnumerable<Claim>>()), Times.Never);
    }

    [Fact]
    public async Task MfaSetup_WhenMigrationGuardCannotResolveState_DeniesFullCookie()
    {
        var user = CreateUser();
        var identity = CreateIdentity(user);
        var lifecycle = CreateLifecycleEligibility(user.Id, eligible: true);
        var guard = CreateMigrationGuard(user.Id, allowed: false);
        var policy = new Mock<ISecurityPolicyService>();
        policy.Setup(service => service.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy());
        var model = new MfaSetupModel(
            identity.SignInManager.Object,
            identity.UserManager.Object,
            Mock.Of<IStringLocalizer<SharedResource>>(),
            policy.Object,
            guard.Object,
            lifecycle.Object, CreatePasskeyService().Object)
        {
            ReturnUrl = "/continue"
        };
        SetMfaHttpContext(model, user);

        var result = await model.OnPostSkipAsync();

        Assert.IsType<RedirectToPageResult>(result);
        identity.SignInManager.Verify(manager => manager.SignInWithClaimsAsync(user, false, It.IsAny<IEnumerable<Claim>>()), Times.Never);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task CurrentUserLifecycleEligibility_WhenCurrentUserIsInactiveOrDeleted_Denies(
        bool isDeleted,
        bool isActive)
    {
        await using var database = CreateDatabase();
        var user = CreateUser();
        user.IsDeleted = isDeleted;
        user.IsActive = isActive;
        database.Users.Add(user);
        await database.SaveChangesAsync();

        var eligible = await new CurrentUserLifecycleEligibility(database).IsEligibleAsync(user.Id);

        Assert.False(eligible);
    }

    [Fact]
    public async Task CurrentUserLifecycleEligibility_WhenCurrentPersonCannotAuthenticate_Denies()
    {
        await using var database = CreateDatabase();
        var person = new Person
        {
            Id = Guid.NewGuid(),
            Status = PersonStatus.Suspended
        };
        var user = CreateUser();
        user.PersonId = person.Id;
        database.AddRange(person, user);
        await database.SaveChangesAsync();

        var eligible = await new CurrentUserLifecycleEligibility(database).IsEligibleAsync(user.Id);

        Assert.False(eligible);
    }

    private static (Mock<UserManager<ApplicationUser>> UserManager, Mock<SignInManager<ApplicationUser>> SignInManager)
        CreateIdentity(ApplicationUser user)
    {
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
        var signInManager = new Mock<SignInManager<ApplicationUser>>(
            userManager.Object,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            null,
            null,
            null,
            null);
        signInManager
            .Setup(manager => manager.GetTwoFactorAuthenticationUserAsync())
            .ReturnsAsync(user);
        userManager.Setup(manager => manager.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        return (userManager, signInManager);
    }

    private static Mock<ICurrentUserLifecycleEligibility> CreateLifecycleEligibility(Guid userId, bool eligible)
    {
        var lifecycle = new Mock<ICurrentUserLifecycleEligibility>();
        lifecycle
            .Setup(service => service.IsEligibleAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(eligible);
        return lifecycle;
    }

    private static Mock<IMigrationIssuanceGuard> CreateMigrationGuard(Guid userId, bool allowed)
    {
        var guard = new Mock<IMigrationIssuanceGuard>();
        guard
            .Setup(service => service.CanIssueAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(allowed);
        return guard;
    }

    private static Mock<IUserManagementService> CreateUserManagementService()
    {
        var service = new Mock<IUserManagementService>();
        service
            .Setup(candidate => candidate.UpdateLastLoginAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return service;
    }

    private static Mock<IDomainEventPublisher> CreateEventPublisher()
    {
        var publisher = new Mock<IDomainEventPublisher>();
        publisher
            .Setup(candidate => candidate.PublishAsync(It.IsAny<LoginAttemptEvent>()))
            .Returns(Task.CompletedTask);
        return publisher;
    }

    private static Mock<IPasskeyService> CreatePasskeyService()
    {
        var service = new Mock<IPasskeyService>();
        service
            .Setup(candidate => candidate.GetUserPasskeysAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return service;
    }

    private static ApplicationUser CreateUser() => new()
    {
        Id = Guid.NewGuid(),
        UserName = "mfa-user",
        NormalizedUserName = "MFA-USER",
        IsActive = true,
        SecurityStamp = Guid.NewGuid().ToString()
    };

    private static void SetMfaHttpContext(PageModel model, ApplicationUser user)
    {
        SetHttpContext(model);
        var nonce = MfaEnrollmentSession.BeginInitial(model.HttpContext.Session, user.Id, securityStamp: user.SecurityStamp);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp!), nonce], IdentityConstants.TwoFactorUserIdScheme));
        var authentication = new Mock<IAuthenticationService>();
        authentication.Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.NoResult());
        authentication.Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.TwoFactorUserIdScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, IdentityConstants.TwoFactorUserIdScheme)));
        model.HttpContext.RequestServices = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider();
    }

    private static void SetHttpContext(PageModel model)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new TestSessionFeature
        {
            Session = new MemorySession()
        });
        model.PageContext = new PageContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));
    }

    private static ApplicationDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class TestSessionFeature : ISessionFeature
    {
        public required ISession Session { get; set; }
    }
}
