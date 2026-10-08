using System.Security.Claims;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Core.Application;
using Core.Domain;
using Web.IdP.Services;
using OpenIddict.Validation.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP.Helpers;

namespace Tests.Web.IdP.UnitTests.Helpers;

public class MfaEnrollmentSessionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SeedCreation_ShouldCarryOnlyTheAuthorizedStampTransition(bool partial, bool revoked)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "before-seed" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        users.Setup(manager => manager.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        var session = new MemorySession();
        AuthenticationMethodSession.Replace(session, user, "ext");
        var identity = TwoFactorAuthenticationSession.CreateIdentity(user, users.Object);
        if (partial) identity.AddClaim(MfaEnrollmentSession.BeginInitial(session, user.Id, securityStamp: user.SecurityStamp));
        else
        {
            identity.AddClaim(new Claim("amr", "mfa"));
            MfaEnrollmentSession.Begin(session, user.Id, true, securityStamp: user.SecurityStamp);
            Assert.True(MfaEnrollmentSession.CompletePending(session, new ClaimsPrincipal(identity)));
        }
        var scheme = partial ? IdentityConstants.TwoFactorUserIdScheme : IdentityConstants.ApplicationScheme;
        var principal = new ClaimsPrincipal(identity);
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>())).ReturnsAsync(AuthenticateResult.NoResult());
        auth.Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), scheme))
            .ReturnsAsync(() => AuthenticateResult.Success(new AuthenticationTicket(principal, scheme)));
        auth.Setup(service => service.SignInAsync(It.IsAny<HttpContext>(), scheme, It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()))
            .Callback<HttpContext, string, ClaimsPrincipal, AuthenticationProperties>((_, _, updated, _) => principal = updated)
            .Returns(Task.CompletedTask);
        var context = new DefaultHttpContext { Session = session, RequestServices = new ServiceCollection()
            .AddSingleton(auth.Object).AddSingleton(users.Object).AddSingleton<IApplicationDbContext>(db).BuildServiceProvider() };
        user.SecurityStamp = "after-seed";
        await db.SaveChangesAsync();
        Assert.Equal(!revoked, await MfaEnrollmentSession.CarryAuthorizedStampAsync(context, user,
            revoked ? "unrelated-reset" : "before-seed", default));
        if (revoked) Assert.Empty(AuthenticationMethodSession.Get(session, user));
        else Assert.Equal(["ext"], AuthenticationMethodSession.Get(session, user));
        var passkeys = new Mock<Core.Application.Interfaces.IPasskeyService>();
        passkeys.Setup(service => service.GetUserPasskeysAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        Assert.Equal(!revoked, await MfaEnrollmentSession.IsAuthorizedAsync(context, user, passkeys.Object));
        auth.Verify(service => service.SignInAsync(It.IsAny<HttpContext>(), scheme, It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()),
            revoked ? Times.Never() : Times.Once());
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("stamp", false)]
    [InlineData("unstamped", false)]
    [InlineData("session-stamp", false)]
    [InlineData("nonce", false)]
    public async Task InitialEnrollment_ShouldRequireCurrentStampInCookieAndSession(string condition, bool allowed)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "initial-stamp" };
        var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        users.Setup(manager => manager.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        var session = new MemorySession();
        var identity = TwoFactorAuthenticationSession.CreateIdentity(user, users.Object);
        identity.AddClaim(MfaEnrollmentSession.BeginInitial(session, user.Id,
            securityStamp: condition == "session-stamp" ? "old-stamp" : user.SecurityStamp));
        if (condition == "nonce") identity.RemoveClaim(identity.FindFirst(MfaEnrollmentSession.InitialPurposeClaim)!);
        if (condition == "unstamped") identity.RemoveClaim(identity.FindFirst(users.Object.Options.ClaimsIdentity.SecurityStampClaimType)!);
        if (condition == "stamp") user.SecurityStamp = "reset-stamp";
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme)).ReturnsAsync(AuthenticateResult.NoResult());
        auth.Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.TwoFactorUserIdScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), IdentityConstants.TwoFactorUserIdScheme)));
        var context = new DefaultHttpContext { Session = session, RequestServices = new ServiceCollection()
            .AddSingleton(auth.Object).AddSingleton(users.Object).BuildServiceProvider() };
        var passkeys = new Mock<Core.Application.Interfaces.IPasskeyService>();
        passkeys.Setup(service => service.GetUserPasskeysAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        Assert.Equal(allowed, await MfaEnrollmentSession.IsAuthorizedAsync(context, user, passkeys.Object));
        Assert.Equal(condition is not ("stamp" or "unstamped"), await TwoFactorAuthenticationSession.GetUserAsync(context, users.Object) != null);
    }

    [Theory]
    [InlineData("cookie", "valid", true)]
    [InlineData("bearer", "valid", true)]
    [InlineData("mixed", "valid", true)]
    [InlineData("bearer", "password", false)]
    [InlineData("cookie", "hardware", false)]
    [InlineData("bearer", "expired", false)]
    [InlineData("cookie", "stamp", false)]
    [InlineData("bearer", "subject", false)]
    [InlineData("mixed", "mixed-subject", false)]
    [InlineData("mixed", "mixed-password", false)]
    [InlineData("cookie", "initial", false)]
    [InlineData("cookie", "ineligible", false)]
    [InlineData("bearer", "migration", false)]
    [InlineData("cookie", "consumed", false)]
    public async Task IsRemovalAuthorizedAsync_ShouldRequireFreshSubjectStampBoundPerformedMfa(string scheme, string condition, bool allowed)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "current-stamp", IsActive = true };
        var session = new MemorySession();
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var completion = CreatePrincipal(user.Id);
        ((ClaimsIdentity)completion.Identity!).AddClaim(new Claim("amr", "mfa"));
        ((ClaimsIdentity)completion.Identity!).AddClaim(new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp));
        MfaEnrollmentSession.Begin(session, user.Id, requiresMfa: true, timeProvider: time, securityStamp: user.SecurityStamp);
        Assert.True(MfaEnrollmentSession.CompletePending(session, completion, time));
        if (condition == "expired") time.Advance(TimeSpan.FromMinutes(6));
        if (condition == "stamp") user.SecurityStamp = "rotated";
        if (condition == "subject") user.Id = Guid.NewGuid();
        if (condition == "initial") MfaEnrollmentSession.BeginInitial(session, user.Id, time);
        if (condition == "consumed") MfaEnrollmentSession.Consume(session);

        var auth = new Mock<IAuthenticationService>();
        auth.Setup(s => s.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>())).ReturnsAsync(AuthenticateResult.NoResult());
        foreach (var identityScheme in new[] { IdentityConstants.ApplicationScheme, OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme })
        {
            var cookie = identityScheme == IdentityConstants.ApplicationScheme;
            if (scheme == "cookie" && !cookie || scheme == "bearer" && cookie) continue;
            var principal = CreatePrincipal(condition == "mixed-subject" && !cookie ? Guid.NewGuid() : user.Id);
            ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("amr",
                condition is "password" || condition == "mixed-password" && !cookie ? "pwd" : condition == "hardware" ? "hwk" : "mfa"));
            // No auth_time claim: actual access JWTs legitimately omit it.
            auth.Setup(s => s.AuthenticateAsync(It.IsAny<HttpContext>(), identityScheme))
                .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, identityScheme)));
        }
        var eligibility = new Mock<ICurrentUserLifecycleEligibility>();
        eligibility.Setup(s => s.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(condition != "ineligible");
        var migration = new Mock<IMigrationIssuanceGuard>();
        migration.Setup(s => s.CanIssueAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(condition != "migration");
        var context = new DefaultHttpContext { Session = session, RequestServices = new ServiceCollection()
            .AddSingleton(auth.Object).AddSingleton(eligibility.Object).AddSingleton(migration.Object).BuildServiceProvider() };

        Assert.Equal(allowed, await MfaEnrollmentSession.IsRemovalAuthorizedAsync(context, user, timeProvider: time));
    }

    [Fact]
    public void CompletePending_ShouldRejectRotatedStampAndPasswordOnlyCompletion()
    {
        var session = new MemorySession();
        var user = Guid.NewGuid();
        var principal = CreatePrincipal(user);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("AspNet.Identity.SecurityStamp", "new-stamp"));
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("amr", "mfa"));
        MfaEnrollmentSession.Begin(session, user, true, securityStamp: "old-stamp");
        Assert.False(MfaEnrollmentSession.CompletePending(session, principal));
        Assert.False(MfaEnrollmentSession.HasFreshProof(session, user));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IsAuthorizedAsync_ShouldRequireVerifiedMfaForExistingFactor(bool userVerified)
    {
        var userId = Guid.NewGuid();
        var principal = CreatePrincipal(userId);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("amr", "hwk"));
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("amr", "user"));
        if (userVerified) ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("amr", "mfa"));
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(s => s.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, IdentityConstants.ApplicationScheme)));
        var context = new DefaultHttpContext { Session = new MemorySession(),
            RequestServices = new ServiceCollection().AddSingleton(auth.Object).BuildServiceProvider() };

        Assert.Equal(userVerified, await MfaEnrollmentSession.IsAuthorizedAsync(context, userId,
            hasExistingFactor: true, requireFreshProof: false));
        MfaEnrollmentSession.Begin(context.Session, userId, requiresMfa: true);
        Assert.Equal(userVerified, MfaEnrollmentSession.CompletePending(context.Session, principal));
    }

    [Fact]
    public async Task IsAuthorizedAsync_ShouldBindInitialEnrollmentToPurposeUserExpiryAndConsumption()
    {
        var userId = Guid.NewGuid();
        var session = new MemorySession();
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var purpose = MfaEnrollmentSession.BeginInitial(session, userId, time);
        var principal = CreatePrincipal(userId);
        ((ClaimsIdentity)principal.Identity!).AddClaim(purpose);
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(s => s.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.NoResult());
        auth.Setup(s => s.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.TwoFactorUserIdScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, IdentityConstants.TwoFactorUserIdScheme)));
        var context = new DefaultHttpContext { Session = session,
            RequestServices = new ServiceCollection().AddSingleton(auth.Object).BuildServiceProvider() };

        Assert.True(await MfaEnrollmentSession.IsAuthorizedAsync(context, userId, time));
        Assert.False(await MfaEnrollmentSession.IsAuthorizedAsync(context, Guid.NewGuid(), time));
        Assert.False(await MfaEnrollmentSession.IsAuthorizedAsync(context, userId, time, hasExistingFactor: true));
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.False(await MfaEnrollmentSession.IsAuthorizedAsync(context, userId, time));
        ((ClaimsIdentity)principal.Identity!).RemoveClaim(principal.FindFirst(MfaEnrollmentSession.InitialPurposeClaim)!);
        ((ClaimsIdentity)principal.Identity!).AddClaim(MfaEnrollmentSession.BeginInitial(session, userId, time));
        MfaEnrollmentSession.Consume(session);
        Assert.False(await MfaEnrollmentSession.IsAuthorizedAsync(context, userId, time));
    }

    [Fact]
    public void CompletePending_ShouldRequireIntendedUserAndCompletedMfa()
    {
        var session = new MemorySession();
        var userId = Guid.NewGuid();
        MfaEnrollmentSession.Begin(session, userId, requiresMfa: true);
        Assert.False(MfaEnrollmentSession.CompletePending(session, CreatePrincipal(Guid.NewGuid())));
        MfaEnrollmentSession.Begin(session, userId, requiresMfa: true);
        Assert.False(MfaEnrollmentSession.CompletePending(session, CreatePrincipal(userId)));
        var principal = CreatePrincipal(userId);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("amr", "mfa"));
        MfaEnrollmentSession.Begin(session, userId, requiresMfa: true);
        Assert.True(MfaEnrollmentSession.CompletePending(session, principal));
        Assert.True(MfaEnrollmentSession.HasFreshProof(session, userId));
    }

    [Fact]
    public async Task IsAuthorizedAsync_ShouldRejectGenericFactorChallengeCookie()
    {
        var userId = Guid.NewGuid();
        var principal = CreatePrincipal(userId);
        var authentication = new Mock<IAuthenticationService>();
        authentication.Setup(service => service.AuthenticateAsync(
            It.IsAny<HttpContext>(), IdentityConstants.TwoFactorUserIdScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(
                principal, IdentityConstants.TwoFactorUserIdScheme)));
        authentication.Setup(service => service.AuthenticateAsync(
            It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.NoResult());
        var context = new DefaultHttpContext
        {
            Session = new MemorySession(),
            RequestServices = new ServiceCollection().AddSingleton(authentication.Object)
                .BuildServiceProvider()
        };

        Assert.False(await MfaEnrollmentSession.IsAuthorizedAsync(context, userId));
    }

    [Fact]
    public void CompletePending_BindsFreshProofToAuthenticatedUser()
    {
        var session = new MemorySession();
        var userId = Guid.NewGuid();
        var timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero));
        var principal = CreatePrincipal(userId);

        MfaEnrollmentSession.Begin(session, userId, timeProvider: timeProvider);

        Assert.True(MfaEnrollmentSession.CompletePending(session, principal, timeProvider));
        Assert.True(MfaEnrollmentSession.HasFreshProof(session, userId, timeProvider));
        Assert.False(MfaEnrollmentSession.HasFreshProof(session, Guid.NewGuid(), timeProvider));
    }

    [Fact]
    public void HasPending_ReturnsTrueOnlyWhileReauthenticationAttemptIsActive()
    {
        var session = new MemorySession();
        var userId = Guid.NewGuid();
        var timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero));

        Assert.False(MfaEnrollmentSession.HasPending(session, timeProvider));

        MfaEnrollmentSession.Begin(session, userId, timeProvider: timeProvider);
        Assert.True(MfaEnrollmentSession.HasPending(session, timeProvider));

        timeProvider.Advance(TimeSpan.FromMinutes(6));
        Assert.False(MfaEnrollmentSession.HasPending(session, timeProvider));
    }

    [Fact]
    public void CompletePending_RejectsExpiredReauthenticationAttempt()
    {
        var session = new MemorySession();
        var userId = Guid.NewGuid();
        var timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero));

        MfaEnrollmentSession.Begin(session, userId, timeProvider: timeProvider);
        timeProvider.Advance(TimeSpan.FromMinutes(6));

        Assert.False(
            MfaEnrollmentSession.CompletePending(
                session,
                CreatePrincipal(userId),
                timeProvider));
        Assert.False(MfaEnrollmentSession.HasFreshProof(session, userId, timeProvider));
    }

    [Fact]
    public void HasFreshProof_RejectsExpiredOrConsumedProof()
    {
        var session = new MemorySession();
        var userId = Guid.NewGuid();
        var timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero));

        MfaEnrollmentSession.Begin(session, userId, timeProvider: timeProvider);
        Assert.True(
            MfaEnrollmentSession.CompletePending(
                session,
                CreatePrincipal(userId),
                timeProvider));

        MfaEnrollmentSession.Consume(session);
        Assert.False(MfaEnrollmentSession.HasFreshProof(session, userId, timeProvider));

        MfaEnrollmentSession.Begin(session, userId, timeProvider: timeProvider);
        Assert.True(
            MfaEnrollmentSession.CompletePending(
                session,
                CreatePrincipal(userId),
                timeProvider));
        timeProvider.Advance(TimeSpan.FromMinutes(6));

        Assert.False(MfaEnrollmentSession.HasFreshProof(session, userId, timeProvider));
    }

    private static ClaimsPrincipal CreatePrincipal(Guid userId)
    {
        return new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                "test"));
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
