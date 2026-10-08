using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP.Helpers;

namespace Tests.Web.IdP.UnitTests.Helpers;

public class AuthorizationAuthenticationSessionTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("bad", null)]
    [InlineData("-1", null)]
    [InlineData(" 1000", null)]
    [InlineData("9223372036854775808", null)]
    [InlineData("0", 0L)]
    [InlineData("1000", 1000L)]
    public void GetAuthenticationTime_ShouldReturnOnlyValidNumericEpochSeconds(string? value, long? expected)
    {
        var principal = Principal();
        if (value != null) ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("auth_time", value));
        Assert.Equal(expected, AuthorizationAuthenticationSession.GetAuthenticationTime(principal));
    }

    [Theory]
    [InlineData(null, 60L, false)]
    [InlineData("bad", 60L, false)]
    [InlineData("900", 60L, false)]
    [InlineData("950", 60L, true)]
    [InlineData("1001", 60L, false)]
    [InlineData("1000", 0L, false)]
    [InlineData(null, null, true)]
    public void IsFresh_ShouldRequireTrustworthyAuthenticationTime(string? time, long? maxAge, bool expected)
    {
        var principal = Principal();
        if (time != null) ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("auth_time", time));
        Assert.Equal(expected, AuthorizationAuthenticationSession.IsFresh(principal, maxAge, new FixedClock()));
    }

    [Fact]
    public void FreshCeremony_ShouldBindToExactRequestAndBeConsumedOnce()
    {
        var session = new MemorySession();
        var request = new OpenIddictRequest { ClientId = "client", Prompt = "login consent", State = "original" };
        AuthorizationAuthenticationSession.Begin(session, request, new FixedClock());
        var old = Principal();
        Assert.False(AuthorizationAuthenticationSession.TryConsume(session, old, request, new FixedClock()));
        var context = SigningContext(session);
        AuthorizationAuthenticationSession.OnSigningIn(context);
        Assert.Equal("1000", context.Principal!.FindFirst("auth_time")!.Value);
        Assert.False(AuthorizationAuthenticationSession.TryConsume(session, context.Principal,
            new OpenIddictRequest { ClientId = "other", Prompt = "login consent", State = "original" }, new FixedClock()));
        Assert.True(AuthorizationAuthenticationSession.TryConsume(session, context.Principal, request, new FixedClock()));
        Assert.False(AuthorizationAuthenticationSession.TryConsume(session, context.Principal, request, new FixedClock()));
    }

    [Fact]
    public void Refresh_ShouldPreserveOriginalTimeWithoutCompletingPendingCeremony()
    {
        var session = new MemorySession();
        var request = new OpenIddictRequest { ClientId = "client", MaxAge = 0 };
        AuthorizationAuthenticationSession.Begin(session, request, new FixedClock());
        var context = SigningContext(session);
        var old = Principal();
        ((ClaimsIdentity)old.Identity!).AddClaim(new Claim("auth_time", "100"));
        AuthorizationAuthenticationSession.PreserveTime(context.HttpContext, old);
        AuthorizationAuthenticationSession.OnSigningIn(context);
        Assert.Equal("100", context.Principal!.FindFirst("auth_time")!.Value);
        Assert.False(AuthorizationAuthenticationSession.TryConsume(session, context.Principal, request, new FixedClock()));
    }

    [Fact]
    public void SignInWithoutCredentialEvidence_ShouldNotInventFreshAuthentication()
    {
        var context = SigningContext(new MemorySession());
        ((ClaimsIdentity)context.Principal!.Identity!).RemoveClaim(context.Principal.FindFirst("amr")!);
        AuthorizationAuthenticationSession.OnSigningIn(context);
        Assert.Null(context.Principal.FindFirst("auth_time"));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void CopyAssurance_ShouldReplaceAllMethodsOnlyForCurrentVersionSameSubject(bool sameSubject, bool currentVersion)
    {
        var source = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "source"), new Claim("AspNet.Identity.SecurityStamp", "stamp"), new Claim("amr", "ext"), new Claim("amr", "otp"),
            new Claim("amr", "mfa"), new Claim(ClaimTypes.AuthenticationMethod, "custom"), new Claim("auth_time", "123")], "cookie"));
        if (currentVersion) ((ClaimsIdentity)source.Identity!).AddClaim(new Claim(
            AuthorizationAuthenticationSession.AssuranceVersionClaim, AuthorizationAuthenticationSession.AssuranceVersion));
        var target = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, sameSubject ? "source" : "target"), new Claim("AspNet.Identity.SecurityStamp", "stamp"), new Claim("amr", "hwk"),
            new Claim("auth_time", "999")], "cookie"));
        AuthorizationAuthenticationSession.CopyAssurance(source, target);
        if (sameSubject && currentVersion)
        {
            Assert.Equal(["ext", "otp", "mfa"], target.FindAll("amr").Select(claim => claim.Value));
            Assert.Equal("custom", target.FindFirstValue(ClaimTypes.AuthenticationMethod));
            Assert.Equal("123", target.FindFirstValue("auth_time"));
        }
        else
        {
            Assert.Empty(target.FindAll("amr"));
            Assert.Null(target.FindFirst("auth_time"));
            Assert.Null(target.FindFirst(ClaimTypes.AuthenticationMethod));
        }
    }

    [Fact]
    public void CopyAssurance_ShouldRejectUnapprovedSecurityStampTransition()
    {
        var source = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "subject"), new Claim("AspNet.Identity.SecurityStamp", "old"),
            new Claim(AuthorizationAuthenticationSession.AssuranceVersionClaim, AuthorizationAuthenticationSession.AssuranceVersion),
            new Claim("amr", "mfa"), new Claim("auth_time", "123")], "cookie"));
        var target = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "subject"), new Claim("AspNet.Identity.SecurityStamp", "new")], "cookie"));
        AuthorizationAuthenticationSession.CopyAssurance(source, target);
        Assert.Empty(target.FindAll("amr"));
        Assert.Null(target.FindFirst("auth_time"));
    }

    [Fact]
    public void ManualRefresh_ShouldPreserveEveryCookieMethodAndTimeWithoutPendingAuthority()
    {
        var session = new MemorySession();
        session.SetString(AuthenticationMethodSession.SessionKey, "[\"hwk\",\"mfa\"]");
        var request = new OpenIddictRequest { ClientId = "client", MaxAge = 0 };
        AuthorizationAuthenticationSession.Begin(session, request, new FixedClock());
        var context = SigningContext(session);
        var source = Principal();
        var identity = (ClaimsIdentity)source.Identity!;
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "same-subject"));
        identity.AddClaim(new Claim("AspNet.Identity.SecurityStamp", "stamp"));
        identity.AddClaim(new Claim(AuthorizationAuthenticationSession.AssuranceVersionClaim, AuthorizationAuthenticationSession.AssuranceVersion));
        identity.AddClaim(new Claim("amr", "otp"));
        identity.AddClaim(new Claim("amr", "mfa"));
        identity.AddClaim(new Claim("auth_time", "123"));
        ((ClaimsIdentity)context.Principal!.Identity!).AddClaim(new Claim(ClaimTypes.NameIdentifier, "same-subject"));
        AuthorizationAuthenticationSession.PreserveAssurance(context.HttpContext, source);
        AuthorizationAuthenticationSession.OnSigningIn(context);
        Assert.Equal(["pwd", "otp", "mfa"], context.Principal.FindAll("amr").Select(claim => claim.Value));
        Assert.Equal("123", context.Principal.FindFirstValue("auth_time"));
        Assert.False(AuthorizationAuthenticationSession.TryConsume(session, context.Principal, request, new FixedClock()));
        Assert.True(AuthorizationAuthenticationSession.HasCurrentAssuranceVersion(context.Principal));
    }

    private static ClaimsPrincipal Principal() => new(new ClaimsIdentity([new Claim("amr", "pwd")], "Identity.Application"));

    [Fact]
    public void PositiveMaxAge_ShouldConsumeCeremonyImmediatelyAndRejectStaleReplay()
    {
        var session = new MemorySession();
        var request = new OpenIddictRequest { ClientId = "client", MaxAge = 60 };
        AuthorizationAuthenticationSession.Begin(session, request, new FixedClock());
        var context = SigningContext(session);
        AuthorizationAuthenticationSession.OnSigningIn(context);
        Assert.False(AuthorizationAuthenticationSession.RequiresChallenge(session, context.Principal!, request, false, new FixedClock()));
        Assert.True(AuthorizationAuthenticationSession.RequiresChallenge(session, context.Principal!, request, false, new LaterClock()));
        Assert.False(AuthorizationAuthenticationSession.TryConsume(session, context.Principal!, request, new LaterClock()));
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, 0L)]
    public void CompletedCeremony_ShouldSatisfyForcedLoginOnlyOnce(bool forceLogin, long? maxAge)
    {
        var session = new MemorySession();
        var request = new OpenIddictRequest { ClientId = "client", MaxAge = maxAge, Prompt = forceLogin ? "login" : null };
        AuthorizationAuthenticationSession.Begin(session, request, new FixedClock());
        var context = SigningContext(session);
        AuthorizationAuthenticationSession.OnSigningIn(context);
        Assert.False(AuthorizationAuthenticationSession.RequiresChallenge(session, context.Principal!, request, forceLogin, new FixedClock()));
        Assert.True(AuthorizationAuthenticationSession.RequiresChallenge(session, context.Principal!, request, forceLogin, new FixedClock()));
    }

    private sealed class LaterClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeSeconds(1061);
    }

    private static CookieSigningInContext SigningContext(ISession session) => new(
        new DefaultHttpContext { Session = session, RequestServices = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider() }, new AuthenticationScheme("Identity.Application", null, typeof(CookieAuthenticationHandler)),
        new CookieAuthenticationOptions { TimeProvider = new FixedClock() }, Principal(), new AuthenticationProperties(), new CookieOptions());

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeSeconds(1000);
    }
}
