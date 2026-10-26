using System.Security.Claims;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Interfaces;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Web.IdP.Helpers;
using Web.IdP.Services;
using Web.IdP.Services.Saml.Idp;

namespace Tests.Web.IdP.UnitTests;

public sealed class SamlIdpAccountPolicyTests
{
    [Theory]
    [InlineData("eligible", 0)]
    [InlineData("inactive", 403)]
    [InlineData("deleted", 403)]
    [InlineData("locked", 403)]
    [InlineData("password-change", 403)]
    [InlineData("stamp", 403)]
    [InlineData("can-sign-in", 403)]
    [InlineData("lifecycle", 403)]
    [InlineData("migration", 403)]
    [InlineData("missing-person", 403)]
    [InlineData("old-assurance", 403)]
    [InlineData("provider-mfa", 302)]
    [InlineData("stale-mfa", 302)]
    [InlineData("local-mfa", 0)]
    public async Task Evaluate_ShouldEnforceCurrentLocalAccountAndPerformedMfa(string condition, int expected)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = "fixture", SecurityStamp = "current", IsActive = condition != "inactive",
            IsDeleted = condition == "deleted", RequiresPasswordChange = condition == "password-change",
            LockoutEnd = condition == "locked" ? DateTimeOffset.UtcNow.AddMinutes(5) : null,
            PersonId = condition == "missing-person" ? Guid.NewGuid() : null,
            TwoFactorEnabled = condition is "provider-mfa" or "stale-mfa" or "local-mfa"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        var signIn = new Mock<SignInManager<ApplicationUser>>(users.Object, Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(), null, null, null, null);
        signIn.Setup(s => s.CanSignInAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(condition != "can-sign-in");
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(users.Object.Options.ClaimsIdentity.SecurityStampClaimType, condition == "stamp" ? "old" : "current"),
            new Claim("auth_time", DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds().ToString()),
            new Claim(AuthorizationAuthenticationSession.AssuranceVersionClaim, condition == "old-assurance" ? "0" : AuthorizationAuthenticationSession.AssuranceVersion)
        ], IdentityConstants.ApplicationScheme);
        if (condition is "provider-mfa" or "stale-mfa" or "local-mfa") identity.AddClaim(new Claim("amr", "mfa"));
        if (condition is "local-mfa" or "stale-mfa") identity.AddClaim(new Claim(AuthorizationAuthenticationSession.MfaSecurityStampClaim, condition == "local-mfa" ? "current" : "old"));
        var principal = new ClaimsPrincipal(identity);
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, IdentityConstants.ApplicationScheme)));
        using var services = new ServiceCollection().AddSingleton(auth.Object).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services, User = principal };
        var lifecycle = Mock.Of<ICurrentUserLifecycleEligibility>(l => l.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>()) == Task.FromResult(condition != "lifecycle"));
        var migration = Mock.Of<IMigrationIssuanceGuard>(m => m.CanIssueAsync(user.Id, It.IsAny<CancellationToken>()) == Task.FromResult(condition != "migration"));
        var policies = Mock.Of<ISecurityPolicyService>(p => p.GetCurrentPolicyAsync() == Task.FromResult(new SecurityPolicy { EnforceMandatoryMfaEnrollment = user.TwoFactorEnabled }));
        var passkeys = Mock.Of<IPasskeyService>(p => p.GetUserPasskeysAsync(user.Id, It.IsAny<CancellationToken>()) == Task.FromResult(new List<UserCredentialDto>()));
        var codes = Mock.Of<IAccountRecoveryCodeService>(c => c.GetStatusAsync(user.Id, It.IsAny<CancellationToken>()) == Task.FromResult(new AccountRecoveryCodeStatusDto(false, 0)));
        var policy = new SamlIdpAccountPolicy(db, users.Object, signIn.Object, lifecycle, migration, policies, passkeys, codes, TimeProvider.System);
        var result = await policy.EvaluateAsync(http, "/saml/idp/continue?state=fixture", default);
        if (expected == 0) { Assert.Null(result.RequiredAction); Assert.Equal(user.Id, result.UserId); }
        else if (expected == 302) Assert.Contains("/Account/LoginMfa?", Assert.IsType<RedirectResult>(result.RequiredAction).Url);
        else Assert.Equal(expected, Assert.IsType<StatusCodeResult>(result.RequiredAction).StatusCode);
    }
}
