using System.Security.Claims;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Interfaces;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Events;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using Web.IdP.Controllers.Account;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP.Helpers;
using Web.IdP.Pages.Account;
using Web.IdP.Services;

namespace Tests.Web.IdP.UnitTests.Helpers;

public class PendingExternalLoginLinkTests
{
    [Fact]
    public async Task AutoLink_ShouldRejectCandidateAndOldMfaCookieWhenNoEstablishedFactorWasVerified()
    {
        using var fixture = new Fixture("none");
        fixture.Http.User = fixture.Principal();
        var result = await fixture.Coordinator.LinkAsync(fixture.Http, fixture.User, new UserLoginInfo("Google", "candidate", "Google"));
        Assert.Equal(ExternalSignInCompletionStatus.Blocked, result.Status);
        Assert.Empty(fixture.Links);
    }

    [Fact]
    public async Task PendingLink_ShouldRejectNewEnrollmentAndNewPasskeyUntilExistingTotpCompletes()
    {
        using var fixture = new Fixture();
        await fixture.BeginAsync();
        await PendingExternalLoginLink.MarkMfaCompletionAsync(fixture.Http, fixture.User, "enrollment");
        await PendingExternalLoginLink.CompleteAsync(fixture.Http, fixture.Principal());
        await PendingExternalLoginLink.MarkMfaCompletionAsync(fixture.Http, fixture.User, "passkey", "new-key");
        await PendingExternalLoginLink.CompleteAsync(fixture.Http, fixture.Principal());
        Assert.Empty(fixture.Links);
        await PendingExternalLoginLink.MarkMfaCompletionAsync(fixture.Http, fixture.User, "totp");
        Assert.True(await PendingExternalLoginLink.CompleteAsync(fixture.Http, fixture.Principal()));
        Assert.Single(fixture.Links);
    }

    [Fact]
    public async Task ProfileLink_ShouldConsumeFreshProviderBoundProofAndPreserveSession()
    {
        using var fixture = new Fixture();
        fixture.Http.User = fixture.Principal();
        var auth = Mock.Get(fixture.Http.RequestServices.GetRequiredService<IAuthenticationService>());
        auth.Setup(value => value.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(fixture.Http.User, IdentityConstants.ApplicationScheme)));
        var nonce = await AccountSecurityOperationSession.BeginAsync(fixture.Http, fixture.User,
            AccountSecurityOperationSession.ExternalLinkPurpose, "Google");
        AccountSecurityOperationSession.MarkVerified(fixture.Http, fixture.User, "totp");
        var info = new ExternalLoginInfo(new ClaimsPrincipal(), "Google", "fresh-key", "Google")
            { AuthenticationProperties = new AuthenticationProperties() };
        info.AuthenticationProperties.Items[AccountSecurityOperationSession.CorrelationProperty] = nonce;
        Assert.True((await fixture.Coordinator.LinkAsync(fixture.Http, fixture.User, info)).IsSucceeded);
        Assert.Single(fixture.Links);
        Assert.Equal(0, fixture.FullCookies);
        Assert.False(await AccountSecurityOperationSession.IsAuthorizedAsync(fixture.Http, fixture.User,
            AccountSecurityOperationSession.ExternalLinkPurpose, "Google", nonce));
    }

    [Theory]
    [InlineData("totp", ExternalSignInCompletionStatus.TotpRequired)]
    [InlineData("email", ExternalSignInCompletionStatus.EmailOtpRequired)]
    [InlineData("passkey", ExternalSignInCompletionStatus.PasskeyRequired)]
    [InlineData("enrollment", ExternalSignInCompletionStatus.MfaEnrollmentRequired)]
    public async Task LinkAsync_ShouldNotPersistUntilRequiredMfaCompletes(string method, ExternalSignInCompletionStatus status)
    {
        using var fixture = new Fixture(method);
        var completion = await fixture.BeginAsync();
        Assert.Equal(status, completion.Status);
        Assert.Empty(fixture.Links);
        Assert.NotNull(fixture.Partial!.FindFirst(PendingExternalLoginLink.PurposeClaim));
        Assert.Equal(0, fixture.FullCookies);

        await PendingExternalLoginLink.MarkMfaCompletionAsync(fixture.Http, fixture.User, method, "AQ");
        Assert.True(await PendingExternalLoginLink.CompleteAsync(fixture.Http, fixture.Principal()));
        Assert.Equal((fixture.User.Id, "Google", "original-key"), Assert.Single(fixture.Links));
        // A duplicate hook or later sign-in cannot add the association again.
        Assert.True(await PendingExternalLoginLink.CompleteAsync(fixture.Http, fixture.Principal()));
        Assert.Single(fixture.Links);
    }

    [Fact]
    public async Task LinkAsync_ShouldLinkWithoutMfaWhenNoRequirementIsOutstanding()
    {
        using var fixture = new Fixture("none");
        Assert.True((await fixture.BeginAsync()).IsSucceeded);
        Assert.Single(fixture.Links);
        Assert.Equal(1, fixture.FullCookies);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("stamp")]
    [InlineData("subject")]
    [InlineData("nonce")]
    [InlineData("no-purpose")]
    [InlineData("unauthenticated")]
    [InlineData("hardware-only")]
    [InlineData("collision")]
    [InlineData("limit")]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("password-change")]
    [InlineData("lifecycle")]
    [InlineData("migration")]
    [InlineData("identity-policy")]
    [InlineData("lockout")]
    public async Task CompleteAsync_ShouldLeaveNoLinkWhenIntentOrCurrentEligibilityFails(string condition)
    {
        using var fixture = new Fixture();
        await fixture.BeginAsync();
        if (condition == "stamp") fixture.User.SecurityStamp = "changed";
        if (condition == "subject") fixture.Partial = fixture.Principal(Guid.NewGuid());
        if (condition is "nonce" or "no-purpose")
        {
            var identity = (ClaimsIdentity)fixture.Partial!.Identity!;
            identity.RemoveClaim(identity.FindFirst(PendingExternalLoginLink.PurposeClaim)!);
            if (condition == "nonce") identity.AddClaim(new Claim(PendingExternalLoginLink.PurposeClaim, "unrelated"));
        }
        if (condition == "collision") fixture.Users.Setup(x => x.FindByLoginAsync("Google", "original-key")).ReturnsAsync(new ApplicationUser());
        if (condition == "limit") fixture.Login.Setup(x => x.CanLinkExternalLoginAsync(fixture.User, "Google", It.IsAny<CancellationToken>())).ReturnsAsync((false, "limit"));
        if (condition == "inactive") fixture.User.IsActive = false;
        if (condition == "deleted") fixture.User.IsDeleted = true;
        if (condition == "password-change") fixture.User.RequiresPasswordChange = true;
        if (condition == "lifecycle") fixture.Lifecycle.Setup(x => x.IsEligibleAsync(fixture.User.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        if (condition == "migration") fixture.Migration.Setup(x => x.CanIssueAsync(fixture.User.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        if (condition == "identity-policy") fixture.SignIn.Setup(x => x.CanSignInAsync(fixture.User)).ReturnsAsync(false);
        if (condition == "lockout") fixture.Login.Setup(x => x.ValidateExternalUserSignInAsync(fixture.User, It.IsAny<CancellationToken>())).ReturnsAsync(LoginResult.InvalidCredentials());
        await PendingExternalLoginLink.MarkMfaCompletionAsync(fixture.Http, fixture.User, "totp");
        var principal = fixture.Principal(mfa: condition != "hardware-only");
        if (condition == "unauthenticated") principal = new ClaimsPrincipal(new ClaimsIdentity(principal.Claims));
        await PendingExternalLoginLink.CompleteAsync(fixture.Http, principal,
            condition == "expired" ? new FixedTimeProvider(DateTimeOffset.UtcNow.AddMinutes(6)) : null);
        Assert.Empty(fixture.Links);
    }

    [Theory]
    [InlineData("generic-signin")]
    [InlineData("normal-login")]
    [InlineData("factor-management")]
    [InlineData("other-external-login")]
    public async Task AbandonedIntent_ShouldNotBeConsumedByUnrelatedSameSubjectAuthentication(string nextFlow)
    {
        using var fixture = new Fixture();
        await fixture.BeginAsync();
        if (nextFlow == "normal-login") PendingExternalLoginLink.Cancel(fixture.Http);
        if (nextFlow == "factor-management") MfaEnrollmentSession.Begin(fixture.Http.Session, fixture.User.Id, true, securityStamp: fixture.User.SecurityStamp);
        if (nextFlow == "other-external-login") await fixture.Coordinator.CompleteAsync(fixture.Http, fixture.User);
        if (nextFlow != "generic-signin") await PendingExternalLoginLink.MarkMfaCompletionAsync(fixture.Http, fixture.User, "totp");
        Assert.True(await PendingExternalLoginLink.CompleteAsync(fixture.Http, fixture.Principal()));
        Assert.Empty(fixture.Links);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitialTotpSeed_ShouldCarryOnlyItsOwnAuthorizedStampTransition(bool staleBeforeSetup)
    {
        using var fixture = new Fixture("enrollment");
        await fixture.BeginAsync();
        var previousStamp = staleBeforeSetup ? "revoked-stamp" : fixture.User.SecurityStamp;
        fixture.User.SecurityStamp = "initial-seed-stamp";
        await fixture.Database.SaveChangesAsync();
        await PendingExternalLoginLink.CarryInitialEnrollmentStampAsync(fixture.Http, fixture.User, previousStamp);
        await PendingExternalLoginLink.MarkMfaCompletionAsync(fixture.Http, fixture.User);
        await PendingExternalLoginLink.CompleteAsync(fixture.Http, fixture.Principal());
        Assert.Equal(staleBeforeSetup ? 0 : 1, fixture.Links.Count);
    }

    [Theory]
    [InlineData("totp", true)]
    [InlineData("native-recovery", true)]
    [InlineData("email", true)]
    [InlineData("selector-totp", true)]
    [InlineData("custom-recovery", true)]
    [InlineData("totp", false)]
    [InlineData("native-recovery", false)]
    [InlineData("email", false)]
    [InlineData("custom-recovery", false)]
    public async Task ActualMfaPages_ShouldCommitOnlyAfterSuccessfulBoundProof(string method, bool valid)
    {
        using var fixture = new Fixture(method == "email" ? "email" : "totp");
        await fixture.BeginAsync();
        var mfa = new Mock<IMfaService>();
        mfa.Setup(x => x.ValidateTotpCodeAsync(fixture.User, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(valid);
        mfa.Setup(x => x.ValidateNativeRecoveryCodeAsync(fixture.User, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(valid);
        mfa.Setup(x => x.ValidateRecoveryCodeAsync(fixture.User, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(valid);
        mfa.Setup(x => x.VerifyEmailMfaCodeAsync(fixture.User, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(valid);
        var localizer = new Mock<IStringLocalizer<global::Web.IdP.SharedResource>>();
        localizer.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
        var users = Mock.Of<IUserManagementService>();
        var events = Mock.Of<IDomainEventPublisher>();
        if (method == "email")
        {
            var page = new LoginEmailOtpModel(fixture.SignIn.Object, fixture.Users.Object, mfa.Object, users, events,
                Mock.Of<ILogger<LoginEmailOtpModel>>(), localizer.Object, fixture.Migration.Object, fixture.Lifecycle.Object)
            { PageContext = new PageContext { HttpContext = fixture.Http }, ReturnUrl = "/", Input = new() { EmailCode = "123456" } };
            await page.OnPostAsync();
        }
        else if (method is "custom-recovery" or "selector-totp")
        {
            var page = new LoginMfaModel(fixture.SignIn.Object, fixture.Users.Object, mfa.Object, users, fixture.Passkeys.Object,
                events, Mock.Of<ILogger<LoginMfaModel>>(), localizer.Object, fixture.Migration.Object, fixture.Lifecycle.Object)
            { PageContext = new PageContext { HttpContext = fixture.Http }, ReturnUrl = "/", Input = new()
                { RecoveryCode = method == "custom-recovery" ? "recovery" : null, TotpCode = method == "selector-totp" ? "123456" : null } };
            await page.OnPostAsync();
        }
        else
        {
            var page = new LoginTotpModel(fixture.SignIn.Object, fixture.Users.Object, mfa.Object, users, events,
                Mock.Of<ILogger<LoginTotpModel>>(), localizer.Object, fixture.Migration.Object, fixture.Lifecycle.Object)
            { PageContext = new PageContext { HttpContext = fixture.Http }, ReturnUrl = "/", Input = new()
                { RecoveryCode = method == "native-recovery" ? "recovery" : null, TotpCode = method == "totp" ? "123456" : null } };
            await page.OnPostAsync();
        }
        Assert.Equal(valid ? 1 : 0, fixture.Links.Count);
        Assert.Equal(valid ? 1 : 0, fixture.FullCookies);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Theory]
    [InlineData("totp", true)]
    [InlineData("email", true)]
    [InlineData("totp", false)]
    [InlineData("email", false)]
    public async Task ActualInitialEnrollment_ShouldCarryLinkOnlyAfterVerifiedFactor(string method, bool valid)
    {
        using var fixture = new Fixture("enrollment");
        await fixture.BeginAsync();
        var mfa = new Mock<IMfaService>();
        mfa.Setup(x => x.VerifyAndEnableTotpAsync(fixture.User, "123456", It.IsAny<CancellationToken>())).ReturnsAsync(valid);
        mfa.Setup(x => x.VerifyAndEnableEmailMfaAsync(fixture.User, "123456", It.IsAny<CancellationToken>())).ReturnsAsync(valid);
        mfa.Setup(x => x.GenerateRecoveryCodesAsync(fixture.User, 10, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { "recovery" });
        var policy = new Mock<ISecurityPolicyService>();
        policy.Setup(x => x.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy { EnableTotpMfa = true, EnableEmailMfa = true });
        var controller = new MfaSetupApiController(mfa.Object, policy.Object, fixture.Users.Object, fixture.SignIn.Object,
            Mock.Of<IAuditService>(), fixture.Passkeys.Object, Mock.Of<ILogger<MfaSetupApiController>>(), fixture.Migration.Object, fixture.Lifecycle.Object)
        { ControllerContext = new ControllerContext { HttpContext = fixture.Http } };
        if (method == "totp") await controller.VerifyTotp(new MfaSetupVerifyRequest { Code = "123456" }, default);
        else await controller.VerifyEmailMfaCode(new MfaSetupVerifyRequest { Code = "123456" }, default);
        Assert.Equal(valid ? 1 : 0, fixture.Links.Count);
        Assert.Equal(valid ? 1 : 0, fixture.FullCookies);
    }

    [Theory]
    [InlineData("https://attacker.test", "/")]
    [InlineData("//attacker.test", "/")]
    [InlineData("/\\attacker.test", "/")]
    [InlineData("\\\\attacker.test", "/")]
    [InlineData("/\r\n//attacker.test", "/")]
    [InlineData(null, "/")]
    [InlineData("/Account/Profile", "/Account/Profile")]
    [InlineData("/connect/authorize?request_uri=urn:ietf:params:oauth:request_uri:test", "/connect/authorize?request_uri=urn:ietf:params:oauth:request_uri:test")]
    public async Task MfaSetup_ShouldExposeOnlyNormalizedLocalReturnForAllMethodsAndSkip(string? input, string expected)
    {
        using var fixture = new Fixture("none");
        fixture.Partial = fixture.Principal();
        ((ClaimsIdentity)fixture.Partial.Identity!).AddClaim(MfaEnrollmentSession.BeginInitial(fixture.Http.Session, fixture.User.Id,
            securityStamp: fixture.User.SecurityStamp));
        var policy = new Mock<ISecurityPolicyService>();
        policy.Setup(x => x.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy());
        fixture.SignIn.Object.Context = fixture.Http;
        var page = new MfaSetupModel(fixture.SignIn.Object, fixture.Users.Object,
            Mock.Of<IStringLocalizer<global::Web.IdP.SharedResource>>(), policy.Object,
            fixture.Migration.Object, fixture.Lifecycle.Object, fixture.Passkeys.Object)
        { PageContext = new PageContext { HttpContext = fixture.Http }, ReturnUrl = input };
        Assert.IsType<PageResult>(await page.OnGetAsync());
        Assert.Equal(expected, page.ReturnUrl);
        page.ReturnUrl = input; // Model binding runs again on a direct skip POST.
        Assert.Equal(expected, Assert.IsType<RedirectResult>(await page.OnPostSkipAsync()).Url);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ActualPasskeyCompletion_ShouldRequireUserVerificationForLink(bool enrollment, bool userVerified)
    {
        using var fixture = new Fixture(enrollment ? "enrollment" : "passkey");
        fixture.User.Person = new Person { Id = Guid.NewGuid() };
        fixture.User.PersonId = fixture.User.Person.Id;
        await fixture.BeginAsync();
        var policy = new Mock<ISecurityPolicyService>();
        policy.Setup(x => x.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy { EnablePasskey = true });
        policy.Setup(x => x.GetCurrentPolicyForPasskeyAuthenticationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityPolicy { EnablePasskey = true });
        fixture.Passkeys.Setup(x => x.VerifyAssertionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, fixture.User, userVerified, (string?)null));
        fixture.Passkeys.Setup(x => x.RegisterCredentialsAsync(fixture.User, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, (string?)null, userVerified));
        fixture.Http.Session.SetString("fido2.assertionOptions", "{}");
        fixture.Http.Session.SetString("fido2.assertionUserId", fixture.User.Id.ToString());
        fixture.Http.Session.SetString("fido2.attestationOptions", "{}");
        var controller = new PasskeyController(fixture.Passkeys.Object, fixture.SignIn.Object, fixture.Users.Object,
            policy.Object, Mock.Of<IUserManagementService>(), null!, Mock.Of<IAuditService>(), Mock.Of<ILogger<PasskeyController>>(),
            fixture.Migration.Object, fixture.Lifecycle.Object)
        { ControllerContext = new ControllerContext { HttpContext = fixture.Http } };
        using var response = System.Text.Json.JsonDocument.Parse("{\"id\":\"AQ\",\"rawId\":\"AQ\"}");
        if (enrollment) await controller.MakeCredential(response.RootElement, default);
        else await controller.MakeAssertion(response.RootElement, default);
        Assert.Equal(userVerified ? 1 : 0, fixture.Links.Count);
    }

    [Fact]
    public async Task AuthenticatedMfaCookie_ShouldRequirePerformedExistingFactorBeforeLinking()
    {
        using var fixture = new Fixture();
        fixture.Http.User = fixture.Principal();
        Assert.Equal(ExternalSignInCompletionStatus.TotpRequired, (await fixture.BeginAsync()).Status);
        Assert.Empty(fixture.Links);
        Assert.Equal(0, fixture.FullCookies);
        Assert.NotNull(fixture.Partial);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _services;
        public ApplicationUser User { get; } = new() { Id = Guid.NewGuid(), UserName = "link-target", Email = "target@example.test", SecurityStamp = "original-stamp", IsActive = true };
        public Mock<UserManager<ApplicationUser>> Users { get; }
        public Mock<SignInManager<ApplicationUser>> SignIn { get; }
        public Mock<ILoginService> Login { get; } = new();
        public Mock<ICurrentUserLifecycleEligibility> Lifecycle { get; } = new();
        public Mock<IMigrationIssuanceGuard> Migration { get; } = new();
        public Mock<IPasskeyService> Passkeys { get; } = new();
        public ClaimsPrincipal? Partial { get; set; }
        public DefaultHttpContext Http { get; }
        public ApplicationDbContext Database { get; }
        public ExternalSignInCoordinator Coordinator { get; }
        public List<(Guid UserId, string Provider, string Key)> Links { get; } = [];
        public int FullCookies { get; private set; }
        private readonly bool _passwordVerified;

        public Fixture(string method = "totp")
        {
            _passwordVerified = method is "none" or "enrollment";
            User.TwoFactorEnabled = method == "totp";
            User.EmailMfaEnabled = method == "email";
            Database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            Database.Users.Add(User);
            if (method == "passkey") Database.UserCredentials.Add(new UserCredential
                { UserId = User.Id, CredentialId = [1], PublicKey = [2] });
            Database.SaveChanges();
            Users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
            Users.Setup(x => x.FindByIdAsync(User.Id.ToString())).ReturnsAsync(User);
            Users.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(User);
            Users.Setup(x => x.UpdateAsync(User)).ReturnsAsync(IdentityResult.Success);
            Users.Setup(x => x.AddLoginAsync(User, It.IsAny<UserLoginInfo>()))
                .Callback<ApplicationUser, UserLoginInfo>((user, info) => Links.Add((user.Id, info.LoginProvider, info.ProviderKey)))
                .ReturnsAsync(IdentityResult.Success);
            SignIn = new Mock<SignInManager<ApplicationUser>>(Users.Object, Mock.Of<IHttpContextAccessor>(),
                Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(), null, null, null, null);
            SignIn.Setup(x => x.CanSignInAsync(User)).ReturnsAsync(true);
            SignIn.Setup(x => x.GetTwoFactorAuthenticationUserAsync()).ReturnsAsync(User);
            Login.Setup(x => x.ValidateExternalUserSignInAsync(User, It.IsAny<CancellationToken>())).ReturnsAsync(LoginResult.Success(User));
            Login.Setup(x => x.CanLinkExternalLoginAsync(User, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((true, null));
            Lifecycle.Setup(x => x.IsEligibleAsync(User.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            Migration.Setup(x => x.CanIssueAsync(User.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            Passkeys.Setup(x => x.GetUserPasskeysAsync(User.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(method == "passkey" ? [new UserCredentialDto()] : []);
            var policy = new Mock<ISecurityPolicyService>();
            policy.Setup(x => x.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy
                { EnforceMandatoryMfaEnrollment = method == "enrollment", MfaEnforcementGracePeriodDays = 0 });
            var auth = new Mock<IAuthenticationService>();
            auth.Setup(x => x.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
                .ReturnsAsync(AuthenticateResult.NoResult());
            auth.Setup(x => x.SignInAsync(It.IsAny<HttpContext>(), IdentityConstants.TwoFactorUserIdScheme,
                    It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()))
                .Callback<HttpContext, string, ClaimsPrincipal, AuthenticationProperties>((_, _, principal, _) => Partial = principal)
                .Returns(Task.CompletedTask);
            auth.Setup(x => x.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.TwoFactorUserIdScheme))
                .ReturnsAsync(() => Partial == null ? AuthenticateResult.NoResult() :
                    AuthenticateResult.Success(new AuthenticationTicket(Partial, IdentityConstants.TwoFactorUserIdScheme)));
            _services = new ServiceCollection().AddSingleton(auth.Object).AddSingleton(Users.Object)
                .AddSingleton<IApplicationDbContext>(Database)
                .AddSingleton(SignIn.Object).AddSingleton(Login.Object).AddSingleton(Lifecycle.Object).AddSingleton(Migration.Object).BuildServiceProvider();
            Http = new DefaultHttpContext { RequestServices = _services, Session = new MemorySession() };
            SignIn.Setup(x => x.SignInWithClaimsAsync(User, It.IsAny<bool>(), It.IsAny<IEnumerable<Claim>>()))
                .Returns<ApplicationUser, bool, IEnumerable<Claim>>(async (_, _, claims) =>
                {
                    var principal = Principal(mfa: false);
                    ((ClaimsIdentity)principal.Identity!).AddClaims(claims);
                    Assert.True(await PendingExternalLoginLink.CompleteAsync(Http, principal));
                    FullCookies++;
                });
            Coordinator = new ExternalSignInCoordinator(Lifecycle.Object, SignIn.Object, Users.Object, Login.Object,
                policy.Object, Passkeys.Object, Migration.Object, Mock.Of<ILogger<ExternalSignInCoordinator>>());
        }

        public Task<ExternalSignInCompletionResult> BeginAsync()
        {
            if (_passwordVerified) PendingExternalLoginLink.MarkPasswordCompletion(Http, User);
            return Coordinator.LinkAsync(Http, User, new UserLoginInfo("Google", "original-key", "Google"));
        }
        public ClaimsPrincipal Principal(Guid? subject = null, bool mfa = true) => new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, (subject ?? User.Id).ToString()),
             new Claim("AspNet.Identity.SecurityStamp", User.SecurityStamp!), new Claim("amr", mfa ? "mfa" : "hwk")], IdentityConstants.ApplicationScheme));
        public void Dispose() { _services.Dispose(); Database.Dispose(); }
    }
}
