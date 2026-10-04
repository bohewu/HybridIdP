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
        SetHttpContext(model);

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
        SetHttpContext(model);

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
        SetHttpContext(model);

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
        SetHttpContext(model);

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
        SetHttpContext(model);

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
        SetHttpContext(model);

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
            .Setup(manager => manager.SignInAsync(user, false, null))
            .Returns(Task.CompletedTask);
        var policy = new Mock<ISecurityPolicyService>();
        policy.Setup(service => service.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy());
        var model = new MfaSetupModel(
            identity.SignInManager.Object,
            identity.UserManager.Object,
            Mock.Of<IStringLocalizer<SharedResource>>(),
            policy.Object,
            guard.Object,
            lifecycle.Object)
        {
            ReturnUrl = "/continue"
        };
        SetHttpContext(model);

        var result = await model.OnPostSkipAsync();

        Assert.IsType<RedirectResult>(result);
        identity.SignInManager.Verify(manager => manager.SignInAsync(user, false, null), Times.Once);
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
            lifecycle.Object)
        {
            ReturnUrl = "/continue"
        };
        SetHttpContext(model);

        var result = await model.OnPostSkipAsync();

        Assert.IsType<RedirectToPageResult>(result);
        identity.SignInManager.Verify(manager => manager.SignInAsync(user, false, null), Times.Never);
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
