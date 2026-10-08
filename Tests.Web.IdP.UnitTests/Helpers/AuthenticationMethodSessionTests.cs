using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Core.Domain;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using Core.Domain.Constants;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP.Helpers;

namespace Tests.Web.IdP.UnitTests.Helpers;

public class AuthenticationMethodSessionTests
{
    [Fact]
    public void CreateClaims_LocalPasswordAndOtp_PreservesTruthfulMethods()
    {
        var session = new MemorySession();
        var user = new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "verified-stamp" };

        AuthenticationMethodSession.Replace(session, user, AuthConstants.Amr.Password);
        AuthenticationMethodSession.Add(
            session, user,
            AuthConstants.Amr.Mfa,
            AuthConstants.Amr.Otp);

        var methods = AuthenticationMethodSession.CreateClaims(session, user)
            .Select(claim => claim.Value)
            .ToList();

        Assert.Equal(
            [AuthConstants.Amr.Password, AuthConstants.Amr.Mfa, AuthConstants.Amr.Otp],
            methods);
    }

    [Fact]
    public void CreateClaims_ExternalLoginAndOtp_DoesNotInventPasswordMethod()
    {
        var session = new MemorySession();
        var user = new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "verified-stamp" };

        AuthenticationMethodSession.Replace(session, user, AuthConstants.Amr.External);
        AuthenticationMethodSession.Add(
            session, user,
            AuthConstants.Amr.Mfa,
            AuthConstants.Amr.Otp);

        var methods = AuthenticationMethodSession.CreateClaims(session, user)
            .Select(claim => claim.Value)
            .ToList();

        Assert.Equal(
            [AuthConstants.Amr.External, AuthConstants.Amr.Mfa, AuthConstants.Amr.Otp],
            methods);
        Assert.DoesNotContain(AuthConstants.Amr.Password, methods);
    }

    [Fact]
    public void CreateClaims_ExternalLoginAndPasskeySetup_PreservesBothFactors()
    {
        var session = new MemorySession();
        var user = new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "verified-stamp" };

        AuthenticationMethodSession.Replace(session, user, AuthConstants.Amr.External);
        AuthenticationMethodSession.Add(
            session, user,
            AuthConstants.Amr.HardwareKey,
            AuthConstants.Amr.UserPresence,
            AuthConstants.Amr.Mfa);

        var methods = AuthenticationMethodSession.CreateClaims(session, user)
            .Select(claim => claim.Value)
            .ToList();

        Assert.Equal(
            [
                AuthConstants.Amr.External,
                AuthConstants.Amr.HardwareKey,
                AuthConstants.Amr.UserPresence,
                AuthConstants.Amr.Mfa
            ],
            methods);
        Assert.DoesNotContain(AuthConstants.Amr.Password, methods);
    }

    [Fact]
    public void CreateClaims_MfaOnlySession_DoesNotInventPassword()
    {
        var session = new MemorySession();
        var user = new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "verified-stamp" };
        AuthenticationMethodSession.Replace(
            session, user,
            AuthConstants.Amr.Mfa,
            AuthConstants.Amr.Otp);

        var methods = AuthenticationMethodSession.CreateClaims(session, user)
            .Select(claim => claim.Value)
            .ToList();

        Assert.Equal(
            [AuthConstants.Amr.Mfa, AuthConstants.Amr.Otp],
            methods);
    }
    [Theory]
    [InlineData("other-user")]
    [InlineData("other-stamp")]
    [InlineData("missing-stamp")]
    [InlineData("expired")]
    [InlineData("legacy")]
    [InlineData("malformed")]
    public void CreateClaims_ShouldRejectUnboundOrStaleEvidence(string invalidState)
    {
        var session = new MemorySession();
        var user = new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "verified-stamp" };
        AuthenticationMethodSession.Replace(session, user, "pwd", "mfa", "otp");
        switch (invalidState)
        {
            case "other-user": user.Id = Guid.NewGuid(); break;
            case "other-stamp": user.SecurityStamp = "rotated"; break;
            case "missing-stamp": user.SecurityStamp = null; break;
            case "legacy": session.SetString(AuthenticationMethodSession.SessionKey, "[\"pwd\",\"mfa\"]"); break;
            case "malformed": session.SetString(AuthenticationMethodSession.SessionKey, "{invalid"); break;
            case "expired":
                session.SetString(AuthenticationMethodSession.SessionKey, JsonSerializer.Serialize(new
                {
                    UserId = user.Id, SecurityStamp = user.SecurityStamp, Methods = new[] { "pwd", "mfa" },
                    AuthenticatedAt = DateTimeOffset.UtcNow.AddMinutes(-10), ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
                }));
                break;
        }
        Assert.Empty(AuthenticationMethodSession.CreateClaims(session, user));
        Assert.DoesNotContain(AuthenticationMethodSession.SessionKey, session.Keys);
    }

    [Fact]
    public void Add_ShouldKeepOnlyNewVerifiedFactor_WhenPendingSubjectDiffers()
    {
        var session = new MemorySession();
        var first = new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "first" };
        var second = new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "second" };
        AuthenticationMethodSession.Replace(session, first, "pwd", "otp", "mfa");
        AuthenticationMethodSession.Add(session, second, "mfa");
        Assert.Equal(["mfa"], AuthenticationMethodSession.Get(session, second));
        AuthenticationMethodSession.Consume(session);
        Assert.Empty(AuthenticationMethodSession.CreateClaims(session, second));
    }

    [Theory]
    [InlineData("current", true)]
    [InlineData("other-subject", false)]
    [InlineData("old-stamp", false)]
    [InlineData("legacy", false)]
    public async Task Enrollment_ShouldCarryOnlyValidatedSameSubjectCookieMethods(string cookieState, bool carriesPrimary)
    {
        var session = new MemorySession();
        var user = new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "current-stamp" };
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, cookieState == "other-subject" ? Guid.NewGuid().ToString() : user.Id.ToString()),
            new Claim("AspNet.Identity.SecurityStamp", cookieState == "old-stamp" ? "old-stamp" : user.SecurityStamp),
            new Claim("amr", "ext")], IdentityConstants.ApplicationScheme);
        if (cookieState != "legacy") identity.AddClaim(new Claim(
            AuthorizationAuthenticationSession.AssuranceVersionClaim, AuthorizationAuthenticationSession.AssuranceVersion));
        var authentication = new Mock<IAuthenticationService>();
        authentication.Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), IdentityConstants.ApplicationScheme)));
        var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        using var services = new ServiceCollection().AddSingleton(authentication.Object).AddSingleton(users.Object).BuildServiceProvider();
        var context = new DefaultHttpContext { Session = session, RequestServices = services };
        await AuthenticationMethodSession.AddForEnrollmentAsync(context, user, "mfa", "otp");
        Assert.Equal(carriesPrimary ? new[] { "ext", "mfa", "otp" } : new[] { "mfa", "otp" },
            AuthenticationMethodSession.Get(session, user));
    }

}
