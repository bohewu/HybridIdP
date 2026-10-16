using System.Security.Claims;
using System.Text.Json;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Core.Application;
using Core.Domain;
using Web.IdP.Services;
using OpenIddict.Validation.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task IsAuthorizedAsync_ShouldDenyPasswordOnlyEnrollmentWhileAccountCodesRemain(bool partial)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "current", IsActive = true };
        var session = new MemorySession();
        var principal = CreatePrincipal(user.Id);
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.AddClaim(new Claim(new IdentityOptions().ClaimsIdentity.SecurityStampClaimType, user.SecurityStamp));
        if (partial) identity.AddClaim(MfaEnrollmentSession.BeginInitial(session, user.Id, securityStamp: user.SecurityStamp));
        else
        {
            MfaEnrollmentSession.Begin(session, user.Id, securityStamp: user.SecurityStamp);
            Assert.True(MfaEnrollmentSession.CompletePending(session, principal));
        }
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(value => value.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>())).ReturnsAsync(AuthenticateResult.NoResult());
        var scheme = partial ? IdentityConstants.TwoFactorUserIdScheme : IdentityConstants.ApplicationScheme;
        auth.Setup(value => value.AuthenticateAsync(It.IsAny<HttpContext>(), scheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, scheme)));
        var passkeys = Mock.Of<Core.Application.Interfaces.IPasskeyService>(value =>
            value.GetUserPasskeysAsync(user.Id, It.IsAny<CancellationToken>()) == Task.FromResult(new List<Core.Application.DTOs.UserCredentialDto>()));
        var codes = Mock.Of<IAccountRecoveryCodeService>(value => value.GetStatusAsync(user.Id, It.IsAny<CancellationToken>()) ==
            Task.FromResult(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(true, 1)));
        using var services = new ServiceCollection().AddSingleton(auth.Object).AddSingleton(codes).BuildServiceProvider();
        var http = new DefaultHttpContext { User = principal, Session = session, RequestServices = services };
        Assert.False(await MfaEnrollmentSession.IsAuthorizedAsync(http, user, passkeys));
    }

    [Theory]
    [InlineData(null, MfaEnrollmentAction.None, "MfaEnrollmentReauthenticationNotice")]
    [InlineData("totp-add", MfaEnrollmentAction.AddAuthenticator, "MfaReauthenticationAddAuthenticatorNotice")]
    [InlineData("email-enable", MfaEnrollmentAction.EnableEmailOtp, "MfaReauthenticationEnableEmailOtpNotice")]
    [InlineData("passkey-register", MfaEnrollmentAction.RegisterPasskey, "MfaReauthenticationRegisterPasskeyNotice")]
    [InlineData("totp-disable", MfaEnrollmentAction.RemoveAuthenticator, "MfaReauthenticationRemoveAuthenticatorNotice")]
    [InlineData("email-disable", MfaEnrollmentAction.DisableEmailOtp, "MfaReauthenticationDisableEmailOtpNotice")]
    [InlineData("passkey-delete", MfaEnrollmentAction.DeletePasskey, "MfaReauthenticationDeletePasskeyNotice")]
    [InlineData("recovery-codes", MfaEnrollmentAction.RegenerateRecoveryCodes, "MfaReauthenticationRecoveryCodesNotice")]
    public void PendingPurpose_ShouldUseBoundedServerActionAndExpire(string? value, MfaEnrollmentAction expected, string notice)
    {
        Assert.True(MfaEnrollmentSession.TryParseAction(value, out var action));
        Assert.Equal(expected, action);
        var session = new MemorySession();
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        MfaEnrollmentSession.Begin(session, Guid.NewGuid(), timeProvider: time, action: action);

        Assert.Equal(notice, MfaEnrollmentSession.GetPendingNoticeKey(session, time));
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.False(MfaEnrollmentSession.HasPending(session, time));
        Assert.Equal("MfaEnrollmentReauthenticationNotice", MfaEnrollmentSession.GetPendingNoticeKey(session, time));
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("other-user", false)]
    [InlineData("stamp", false)]
    [InlineData("expired", false)]
    [InlineData("consumed", false)]
    [InlineData("pending", false)]
    [InlineData("other-action", false)]
    public void TakeSetupMethod_ShouldRequireCurrentFreshProofAndTakeSelectedSetupOnlyOnce(string condition, bool allowed)
    {
        var session = new MemorySession();
        var user = new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "current-stamp" };
        var principal = CreatePrincipal(user.Id);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp));
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("amr", "mfa"));
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        MfaEnrollmentSession.Begin(session, user.Id, requiresMfa: true, timeProvider: time, securityStamp: user.SecurityStamp,
            action: condition == "other-action" ? MfaEnrollmentAction.EnableEmailOtp : MfaEnrollmentAction.AddAuthenticator);
        if (condition != "pending") Assert.True(MfaEnrollmentSession.CompletePending(session, principal, time));
        if (condition == "other-user") user.Id = Guid.NewGuid();
        if (condition == "stamp") user.SecurityStamp = "rotated-stamp";
        if (condition == "expired") time.Advance(TimeSpan.FromMinutes(5));
        if (condition == "consumed") MfaEnrollmentSession.Consume(session);

        Assert.Equal(allowed ? "totp" : null, MfaEnrollmentSession.TakeSetupMethod(session, user, time));
        Assert.Null(MfaEnrollmentSession.TakeSetupMethod(session, user, time));
        if (allowed) Assert.True(MfaEnrollmentSession.HasFreshProof(session, user.Id, time));
    }

    [Fact]
    public async Task SeedCreation_ShouldPreserveAssuranceAndSourcePrincipalThroughSigningIn()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "before-seed", EmailMfaEnabled = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        var stampType = users.Object.Options.ClaimsIdentity.SecurityStampClaimType;
        var source = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(stampType, user.SecurityStamp),
            new Claim("amr", "pwd"), new Claim("amr", "otp"), new Claim("amr", "mfa"),
            new Claim("auth_time", "1700000000", ClaimValueTypes.Integer64),
            new Claim(AuthorizationAuthenticationSession.AssuranceVersionClaim, AuthorizationAuthenticationSession.AssuranceVersion)
        ], IdentityConstants.ApplicationScheme));
        var originalClaims = source.Claims.Select(claim => (claim.Type, claim.Value)).ToArray();
        var session = new MemorySession();
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-2));
        MfaEnrollmentSession.Begin(session, user.Id, requiresMfa: true, timeProvider: time, securityStamp: user.SecurityStamp);
        Assert.True(MfaEnrollmentSession.CompletePending(session, source, time));
        using var originalProof = JsonDocument.Parse(session.GetString("MfaEnrollment:Proof")!);
        var properties = new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30) };
        var cookieExpiry = properties.ExpiresUtc;
        var principal = source;
        var scheme = new AuthenticationScheme(IdentityConstants.ApplicationScheme, null, typeof(CookieAuthenticationHandler));
        var options = new CookieAuthenticationOptions();
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>())).ReturnsAsync(AuthenticateResult.NoResult());
        auth.Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
            .ReturnsAsync(() => AuthenticateResult.Success(new AuthenticationTicket(principal, properties, IdentityConstants.ApplicationScheme)));
        auth.Setup(service => service.SignInAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme,
                It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()))
            .Callback<HttpContext, string, ClaimsPrincipal, AuthenticationProperties>((http, _, updated, updatedProperties) =>
            {
                var signingIn = new CookieSigningInContext(http, scheme, options, updated, updatedProperties, new CookieOptions());
                AuthorizationAuthenticationSession.OnSigningIn(signingIn);
                principal = signingIn.Principal!;
            })
            .Returns(Task.CompletedTask);
        var lifecycle = new Mock<ICurrentUserLifecycleEligibility>();
        lifecycle.Setup(service => service.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var migration = new Mock<IMigrationIssuanceGuard>();
        migration.Setup(service => service.CanIssueAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        using var services = new ServiceCollection().AddSingleton(auth.Object).AddSingleton(users.Object)
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton(new ApplicationCookieCurrentStateValidator(lifecycle.Object, migration.Object)).AddSingleton<Core.Application.IAccountRecoveryCodeService>(Moq.Mock.Of<Core.Application.IAccountRecoveryCodeService>(codes => codes.GetStatusAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<CancellationToken>()) == Task.FromResult(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(false, 0)))).BuildServiceProvider();
        var context = new DefaultHttpContext { Session = session, User = source, RequestServices = services };
        user.SecurityStamp = "after-seed";
        await db.SaveChangesAsync();

        Assert.True(await MfaEnrollmentSession.CarryAuthorizedStampAsync(context, user, "before-seed", default));

        Assert.Equal(originalClaims, source.Claims.Select(claim => (claim.Type, claim.Value)).ToArray());
        Assert.NotSame(source.Identity, principal.Identity);
        Assert.Equal(["pwd", "otp", "mfa"], principal.FindAll("amr").Select(claim => claim.Value));
        Assert.Equal("1700000000", principal.FindFirstValue("auth_time"));
        Assert.True(AuthorizationAuthenticationSession.HasCurrentAssuranceVersion(principal));
        Assert.Equal(user.SecurityStamp, principal.FindFirstValue(stampType));
        Assert.Equal(cookieExpiry, properties.ExpiresUtc);
        using var updatedProof = JsonDocument.Parse(session.GetString("MfaEnrollment:Proof")!);
        Assert.Equal(user.SecurityStamp, updatedProof.RootElement.GetProperty("SecurityStamp").GetString());
        Assert.Equal(originalProof.RootElement.GetProperty("ExpiresUtc").GetString(), updatedProof.RootElement.GetProperty("ExpiresUtc").GetString());
        Assert.True(updatedProof.RootElement.GetProperty("MfaCompleted").GetBoolean());
        var nextRequest = new DefaultHttpContext { Session = session, User = principal, RequestServices = services };
        var validation = new CookieValidatePrincipalContext(nextRequest, scheme, options,
            new AuthenticationTicket(principal, properties, IdentityConstants.ApplicationScheme));
        await ApplicationCookieCurrentStateValidator.Compose(_ => Task.CompletedTask)(validation);
        Assert.Same(principal, validation.Principal);
        var passkeys = new Mock<Core.Application.Interfaces.IPasskeyService>();
        passkeys.Setup(service => service.GetUserPasskeysAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        Assert.True(await MfaEnrollmentSession.IsAuthorizedAsync(nextRequest, user, passkeys.Object));
        auth.Verify(service => service.SignInAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme,
            It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()), Times.Once);
    }

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
            .AddSingleton(auth.Object).AddSingleton(users.Object).AddSingleton<IApplicationDbContext>(db).AddSingleton<Core.Application.IAccountRecoveryCodeService>(Moq.Mock.Of<Core.Application.IAccountRecoveryCodeService>(codes => codes.GetStatusAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<CancellationToken>()) == Task.FromResult(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(false, 0)))).BuildServiceProvider() };
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
            .AddSingleton(auth.Object).AddSingleton(users.Object).AddSingleton<Core.Application.IAccountRecoveryCodeService>(Moq.Mock.Of<Core.Application.IAccountRecoveryCodeService>(codes => codes.GetStatusAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<CancellationToken>()) == Task.FromResult(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(false, 0)))).BuildServiceProvider() };
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
            .AddSingleton(auth.Object).AddSingleton(eligibility.Object).AddSingleton(migration.Object).AddSingleton<Core.Application.IAccountRecoveryCodeService>(Moq.Mock.Of<Core.Application.IAccountRecoveryCodeService>(codes => codes.GetStatusAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<CancellationToken>()) == Task.FromResult(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(false, 0)))).BuildServiceProvider() };

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
            RequestServices = new ServiceCollection().AddSingleton(auth.Object).AddSingleton<Core.Application.IAccountRecoveryCodeService>(Moq.Mock.Of<Core.Application.IAccountRecoveryCodeService>(codes => codes.GetStatusAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<CancellationToken>()) == Task.FromResult(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(false, 0)))).BuildServiceProvider() };

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
            RequestServices = new ServiceCollection().AddSingleton(auth.Object).AddSingleton<Core.Application.IAccountRecoveryCodeService>(Moq.Mock.Of<Core.Application.IAccountRecoveryCodeService>(codes => codes.GetStatusAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<CancellationToken>()) == Task.FromResult(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(false, 0)))).BuildServiceProvider() };

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
                .AddSingleton<Core.Application.IAccountRecoveryCodeService>(Moq.Mock.Of<Core.Application.IAccountRecoveryCodeService>(codes => codes.GetStatusAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<CancellationToken>()) == Task.FromResult(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(false, 0)))).BuildServiceProvider()
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

    [Theory]
    [InlineData("fresh", true)]
    [InlineData("cookie-only", false)]
    [InlineData("copied-password", false)]
    [InlineData("stale-password", false)]
    [InlineData("password-subject", false)]
    [InlineData("password-stamp", false)]
    [InlineData("totp", false)]
    [InlineData("email", false)]
    [InlineData("active-passkey", false)]
    [InlineData("retired-passkey", false)]
    [InlineData("account-codes", false)]
    [InlineData("legacy-codes", false)]
    [InlineData("exhausted", false)]
    [InlineData("legacy-history", false)]
    [InlineData("inactive", false)]
    public async Task PasswordContinuation_ShouldBeIssuedOnlyAtGenuineFirstPasswordBoundary(string condition, bool allowed)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "current", IsActive = true };
        var session = new MemorySession();
        var status = new Core.Application.DTOs.AccountRecoveryCodeStatusDto(false, 0);
        if (condition == "copied-password") AuthenticationMethodSession.Add(session, user, "pwd");
        else if (condition != "cookie-only") AuthenticationMethodSession.Replace(session, user, "pwd");
        if (condition == "stale-password")
        {
            var pending = System.Text.Json.Nodes.JsonNode.Parse(session.GetString(AuthenticationMethodSession.SessionKey)!)!;
            pending["ExpiresUtc"] = DateTimeOffset.UtcNow.AddSeconds(-1);
            session.SetString(AuthenticationMethodSession.SessionKey, pending.ToJsonString());
        }
        if (condition == "password-subject") user.Id = Guid.NewGuid();
        if (condition == "password-stamp") user.SecurityStamp = "rotated";
        if (condition == "totp") user.TwoFactorEnabled = true;
        if (condition == "email") user.EmailMfaEnabled = true;
        if (condition == "inactive") user.IsActive = false;
        if (condition == "legacy-history") user.RecoveryCodes = "[]";
        if (condition == "account-codes") status = new(true, 1);
        if (condition == "legacy-codes") status = new(true, 0, 1, true);
        if (condition == "exhausted") status = new(true, 0);
        if (condition is "active-passkey" or "retired-passkey")
        {
            db.UserCredentials.Add(new Core.Domain.Entities.UserCredential { UserId = user.Id,
                DisabledAtUtc = condition == "retired-passkey" ? DateTime.UtcNow : null });
            await db.SaveChangesAsync();
        }
        var codes = Mock.Of<IAccountRecoveryCodeService>(value => value.GetStatusAsync(user.Id, It.IsAny<CancellationToken>()) == Task.FromResult(status));
        using var services = new ServiceCollection().AddSingleton<IApplicationDbContext>(db).AddSingleton(codes).BuildServiceProvider();
        var http = new DefaultHttpContext { Session = session, RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim("amr", "pwd"), new Claim("auth_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())], IdentityConstants.ApplicationScheme)) };

        Assert.Equal(allowed, await MfaEnrollmentSession.TryBeginInitialAfterPasswordAsync(http, user));
        Assert.False(MfaEnrollmentSession.HasFreshProof(session, user.Id));
        if (allowed) Assert.True(MfaEnrollmentSession.HasInitial(session, user.Id));
        else Assert.False(MfaEnrollmentSession.HasInitial(session, user.Id));
    }

    [Theory]
    [InlineData("current", true)]
    [InlineData("expired", false)]
    [InlineData("subject", false)]
    [InlineData("stamp", false)]
    [InlineData("cookie-subject", false)]
    [InlineData("cookie-stamp", false)]
    [InlineData("consumed", false)]
    [InlineData("factor-added", false)]
    [InlineData("codes-added", false)]
    [InlineData("history-added", false)]
    public async Task PasswordContinuation_ShouldTransferOnceIntoBoundedEnrollmentOnlyProof(string condition, bool allowed)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "current", IsActive = true };
        var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(users.Object.Options.ClaimsIdentity.SecurityStampClaimType, user.SecurityStamp),
            new Claim("amr", "pwd")], IdentityConstants.ApplicationScheme));
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(value => value.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>())).ReturnsAsync(AuthenticateResult.NoResult());
        auth.Setup(value => value.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, IdentityConstants.ApplicationScheme)));
        var codes = new Mock<IAccountRecoveryCodeService>();
        codes.Setup(value => value.GetStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(false, 0));
        using var services = new ServiceCollection().AddSingleton<IApplicationDbContext>(db).AddSingleton(users.Object)
            .AddSingleton(auth.Object).AddSingleton(codes.Object).BuildServiceProvider();
        var session = new MemorySession();
        var http = new DefaultHttpContext { Session = session, User = principal, RequestServices = services };
        AuthenticationMethodSession.Replace(session, user, "pwd");
        Assert.True(await MfaEnrollmentSession.TryBeginInitialAfterPasswordAsync(http, user));
        AuthenticationMethodSession.Consume(session);
        var original = JsonDocument.Parse(session.GetString("MfaEnrollment:Initial")!);
        if (condition == "expired")
        {
            var initial = System.Text.Json.Nodes.JsonNode.Parse(session.GetString("MfaEnrollment:Initial")!)!;
            initial["ExpiresUtc"] = DateTimeOffset.UtcNow.AddSeconds(-1);
            session.SetString("MfaEnrollment:Initial", initial.ToJsonString());
        }
        if (condition == "subject") user.Id = Guid.NewGuid();
        if (condition == "stamp") user.SecurityStamp = "rotated";
        if (condition == "cookie-subject") ((ClaimsIdentity)principal.Identity!).RemoveClaim(principal.FindFirst(ClaimTypes.NameIdentifier)!);
        if (condition == "cookie-stamp") ((ClaimsIdentity)principal.Identity!).RemoveClaim(principal.FindFirst(users.Object.Options.ClaimsIdentity.SecurityStampClaimType)!);
        if (condition == "consumed") MfaEnrollmentSession.Consume(session);
        if (condition == "factor-added") user.EmailMfaEnabled = true;
        if (condition == "codes-added") codes.Setup(value => value.GetStatusAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(true, 1));
        if (condition == "history-added") codes.Setup(value => value.GetStatusAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(true, 0));

        Assert.Equal(allowed, await MfaEnrollmentSession.TryContinueInitialAfterPasswordAsync(http, user));
        Assert.False(await MfaEnrollmentSession.TryContinueInitialAfterPasswordAsync(http, user));
        Assert.False(MfaEnrollmentSession.HasInitial(session, user.Id));
        var passkeys = Mock.Of<Core.Application.Interfaces.IPasskeyService>(value => value.GetUserPasskeysAsync(user.Id, It.IsAny<CancellationToken>()) == Task.FromResult(new List<Core.Application.DTOs.UserCredentialDto>()));
        Assert.Equal(allowed, await MfaEnrollmentSession.IsAuthorizedAsync(http, user, passkeys));
        Assert.False(await MfaEnrollmentSession.IsRemovalAuthorizedAsync(http, user));
        if (allowed)
        {
            using var proof = JsonDocument.Parse(session.GetString("MfaEnrollment:Proof")!);
            Assert.Equal(original.RootElement.GetProperty("ExpiresUtc").GetString(), proof.RootElement.GetProperty("ExpiresUtc").GetString());
            Assert.True(proof.RootElement.GetProperty("EnrollmentOnly").GetBoolean());
            Assert.False(proof.RootElement.GetProperty("MfaCompleted").GetBoolean());
            codes.Setup(value => value.GetStatusAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(true, 0));
            Assert.False(await MfaEnrollmentSession.IsAuthorizedAsync(http, user, passkeys));
            MfaEnrollmentSession.Consume(session);
            Assert.False(await MfaEnrollmentSession.IsAuthorizedAsync(http, user, passkeys));
        }
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
