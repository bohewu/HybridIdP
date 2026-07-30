using System.Security.Claims;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Interfaces;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP.Helpers;
using Web.IdP.Services;

namespace Tests.Web.IdP.UnitTests.Services;

public class ExternalSignInCoordinatorTests
{
    [Fact]
    public async Task CompleteAsync_LifecycleDenied_DoesNotCreateAnyCookie()
    {
        var user = CreateUser();
        var harness = new CoordinatorHarness(user);
        harness.LoginService
            .Setup(service => service.ValidateExternalUserSignInAsync(
                user,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginResult.UserInactive());

        var result = await harness.Coordinator.CompleteAsync(harness.HttpContext, user);

        Assert.Equal(ExternalSignInCompletionStatus.Blocked, result.Status);
        harness.VerifyNoCookieCreated();
        harness.SignInManager.Verify(
            manager => manager.CanSignInAsync(It.IsAny<ApplicationUser>()),
            Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_IdentityPolicyDenied_DoesNotCreateAnyCookie()
    {
        var user = CreateUser();
        var harness = new CoordinatorHarness(user);
        harness.SignInManager
            .Setup(manager => manager.CanSignInAsync(user))
            .ReturnsAsync(false);

        var result = await harness.Coordinator.CompleteAsync(harness.HttpContext, user);

        Assert.Equal(ExternalSignInCompletionStatus.Blocked, result.Status);
        harness.VerifyNoCookieCreated();
    }

    [Fact]
    public async Task CompleteAsync_EligibleUser_IssuesOnlyTrustedExternalAmr()
    {
        var user = CreateUser();
        var harness = new CoordinatorHarness(user);

        var result = await harness.Coordinator.CompleteAsync(harness.HttpContext, user);

        Assert.True(result.IsSucceeded);
        var methods = Assert.IsAssignableFrom<IEnumerable<Claim>>(harness.FullSignInClaims)
            .Where(claim => claim.Type == AuthConstants.ClaimTypes.Amr)
            .Select(claim => claim.Value)
            .ToList();
        Assert.Equal([AuthConstants.Amr.External], methods);
        Assert.DoesNotContain(AuthConstants.Amr.Password, methods);
        harness.AuthenticationService.Verify(
            service => service.SignInAsync(
                It.IsAny<HttpContext>(),
                It.IsAny<string>(),
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<AuthenticationProperties>()),
            Times.Never);
    }

    [Theory]
    [InlineData(true, false, ExternalSignInCompletionStatus.TotpRequired)]
    [InlineData(false, true, ExternalSignInCompletionStatus.EmailOtpRequired)]
    public async Task CompleteAsync_LocalMfaEnabled_IssuesOnlyPartialCookie(
        bool totpEnabled,
        bool emailMfaEnabled,
        ExternalSignInCompletionStatus expectedStatus)
    {
        var user = CreateUser();
        user.TwoFactorEnabled = totpEnabled;
        user.EmailMfaEnabled = emailMfaEnabled;
        var harness = new CoordinatorHarness(user);

        var result = await harness.Coordinator.CompleteAsync(harness.HttpContext, user);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(
            [AuthConstants.Amr.External],
            AuthenticationMethodSession.Get(harness.Session));
        harness.SignInManager.Verify(
            manager => manager.SignInWithClaimsAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<bool>(),
                It.IsAny<IEnumerable<Claim>>()),
            Times.Never);
        harness.AuthenticationService.Verify(
            service => service.SignInAsync(
                harness.HttpContext,
                IdentityConstants.TwoFactorUserIdScheme,
                It.Is<ClaimsPrincipal>(principal =>
                    principal.FindFirstValue(ClaimTypes.NameIdentifier) == user.Id.ToString()),
                It.IsAny<AuthenticationProperties>()),
            Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_MandatoryMfaGraceExpired_RequiresEnrollmentWithoutFullCookie()
    {
        var now = new DateTimeOffset(2026, 7, 30, 3, 0, 0, TimeSpan.Zero);
        var user = CreateUser();
        user.MfaRequirementNotifiedAt = now.UtcDateTime.AddDays(-4);
        var harness = new CoordinatorHarness(user, now);
        harness.Policy.EnforceMandatoryMfaEnrollment = true;
        harness.Policy.MfaEnforcementGracePeriodDays = 3;

        var result = await harness.Coordinator.CompleteAsync(harness.HttpContext, user);

        Assert.Equal(ExternalSignInCompletionStatus.MfaEnrollmentRequired, result.Status);
        harness.SignInManager.Verify(
            manager => manager.SignInWithClaimsAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<bool>(),
                It.IsAny<IEnumerable<Claim>>()),
            Times.Never);
        harness.AuthenticationService.Verify(
            service => service.SignInAsync(
                harness.HttpContext,
                IdentityConstants.TwoFactorUserIdScheme,
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<AuthenticationProperties>()),
            Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_FirstMandatoryMfaNotice_PersistsNoticeAndAllowsGraceLogin()
    {
        var now = new DateTimeOffset(2026, 7, 30, 3, 0, 0, TimeSpan.Zero);
        var user = CreateUser();
        var harness = new CoordinatorHarness(user, now);
        harness.Policy.EnforceMandatoryMfaEnrollment = true;
        harness.Policy.MfaEnforcementGracePeriodDays = 3;

        var result = await harness.Coordinator.CompleteAsync(harness.HttpContext, user);

        Assert.True(result.IsSucceeded);
        Assert.Equal(now.UtcDateTime, user.MfaRequirementNotifiedAt);
        harness.UserManager.Verify(manager => manager.UpdateAsync(user), Times.Once);
        Assert.NotNull(harness.FullSignInClaims);
    }

    [Fact]
    public async Task CompleteAsync_MfaNoticePersistenceFails_FailsClosedWithoutCookie()
    {
        var user = CreateUser();
        var harness = new CoordinatorHarness(user);
        harness.Policy.EnforceMandatoryMfaEnrollment = true;
        harness.UserManager
            .Setup(manager => manager.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "WriteFailed" }));

        var result = await harness.Coordinator.CompleteAsync(harness.HttpContext, user);

        Assert.Equal(ExternalSignInCompletionStatus.Blocked, result.Status);
        Assert.Empty(AuthenticationMethodSession.Get(harness.Session));
        harness.VerifyNoCookieCreated();
    }

    private static ApplicationUser CreateUser() =>
        new()
        {
            Id = Guid.NewGuid(),
            UserName = "external-user",
            IsActive = true
        };

    private sealed class CoordinatorHarness
    {
        public CoordinatorHarness(ApplicationUser user, DateTimeOffset? now = null)
        {
            var userStore = new Mock<IUserStore<ApplicationUser>>();
            UserManager = new Mock<UserManager<ApplicationUser>>(
                userStore.Object,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
            UserManager
                .Setup(manager => manager.UpdateAsync(It.IsAny<ApplicationUser>()))
                .ReturnsAsync(IdentityResult.Success);

            SignInManager = new Mock<SignInManager<ApplicationUser>>(
                UserManager.Object,
                Mock.Of<IHttpContextAccessor>(),
                Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
                null,
                null,
                null,
                null);
            SignInManager
                .Setup(manager => manager.CanSignInAsync(user))
                .ReturnsAsync(true);
            SignInManager
                .Setup(manager => manager.SignInWithClaimsAsync(
                    user,
                    false,
                    It.IsAny<IEnumerable<Claim>>()))
                .Callback<ApplicationUser, bool, IEnumerable<Claim>>(
                    (_, _, claims) => FullSignInClaims = claims.ToList())
                .Returns(Task.CompletedTask);

            LoginService = new Mock<ILoginService>();
            LoginService
                .Setup(service => service.ValidateExternalUserSignInAsync(
                    user,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(LoginResult.Success(user));

            Policy = new SecurityPolicy();
            var securityPolicyService = new Mock<ISecurityPolicyService>();
            securityPolicyService
                .Setup(service => service.GetCurrentPolicyAsync())
                .ReturnsAsync(Policy);

            var passkeyService = new Mock<IPasskeyService>();
            passkeyService
                .Setup(service => service.GetUserPasskeysAsync(
                    user.Id,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            AuthenticationService = new Mock<IAuthenticationService>();
            AuthenticationService
                .Setup(service => service.SignInAsync(
                    It.IsAny<HttpContext>(),
                    It.IsAny<string>(),
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<AuthenticationProperties>()))
                .Returns(Task.CompletedTask);

            var services = new ServiceCollection()
                .AddSingleton(AuthenticationService.Object)
                .BuildServiceProvider();
            Session = new MemorySession();
            HttpContext = new DefaultHttpContext
            {
                RequestServices = services
            };
            HttpContext.Features.Set<ISessionFeature>(new TestSessionFeature
            {
                Session = Session
            });

            Coordinator = new ExternalSignInCoordinator(
                SignInManager.Object,
                UserManager.Object,
                LoginService.Object,
                securityPolicyService.Object,
                passkeyService.Object,
                Mock.Of<ILogger<ExternalSignInCoordinator>>(),
                new FixedTimeProvider(now ?? new DateTimeOffset(2026, 7, 30, 3, 0, 0, TimeSpan.Zero)));
        }

        public Mock<UserManager<ApplicationUser>> UserManager { get; }

        public Mock<SignInManager<ApplicationUser>> SignInManager { get; }

        public Mock<ILoginService> LoginService { get; }

        public Mock<IAuthenticationService> AuthenticationService { get; }

        public SecurityPolicy Policy { get; }

        public MemorySession Session { get; }

        public DefaultHttpContext HttpContext { get; }

        public ExternalSignInCoordinator Coordinator { get; }

        public IReadOnlyList<Claim>? FullSignInClaims { get; private set; }

        public void VerifyNoCookieCreated()
        {
            SignInManager.Verify(
                manager => manager.SignInWithClaimsAsync(
                    It.IsAny<ApplicationUser>(),
                    It.IsAny<bool>(),
                    It.IsAny<IEnumerable<Claim>>()),
                Times.Never);
            AuthenticationService.Verify(
                service => service.SignInAsync(
                    It.IsAny<HttpContext>(),
                    It.IsAny<string>(),
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<AuthenticationProperties>()),
                Times.Never);
        }
    }

    private sealed class TestSessionFeature : ISessionFeature
    {
        public required ISession Session { get; set; }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
