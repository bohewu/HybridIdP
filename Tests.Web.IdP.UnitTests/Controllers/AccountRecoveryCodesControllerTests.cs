using System.Security.Claims;
using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP.Controllers.Account;
using Web.IdP.Helpers;
using Web.IdP.Services;

namespace Tests.Web.IdP.UnitTests.Controllers;

public sealed class AccountRecoveryCodesControllerTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("no-proof")]
    [InlineData("password-proof")]
    [InlineData("stale-proof")]
    [InlineData("stale-cookie")]
    [InlineData("wrong-subject")]
    [InlineData("lifecycle")]
    [InlineData("migration")]
    [InlineData("locked")]
    public async Task Generate_ShouldRequireCurrentSubjectAndFreshStrongProof(string condition)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "account", SecurityStamp = "current", IsActive = true };
        var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        var authenticationMethod = "mfa";
        if (condition == "password-proof") authenticationMethod = "pwd";
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(users.Object.Options.ClaimsIdentity.SecurityStampClaimType, condition == "stale-cookie" ? "old" : "current"),
            new Claim("amr", authenticationMethod)], IdentityConstants.ApplicationScheme);
        var principal = new ClaimsPrincipal(identity);
        users.Setup(value => value.GetUserAsync(principal)).ReturnsAsync(user);
        users.Setup(value => value.IsLockedOutAsync(user)).ReturnsAsync(condition == "locked");
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(value => value.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>())).ReturnsAsync(AuthenticateResult.NoResult());
        auth.Setup(value => value.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, IdentityConstants.ApplicationScheme)));
        var lifecycle = Mock.Of<ICurrentUserLifecycleEligibility>(value => value.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>()) == Task.FromResult(condition != "lifecycle"));
        var migration = Mock.Of<IMigrationIssuanceGuard>(value => value.CanIssueAsync(user.Id, It.IsAny<CancellationToken>()) == Task.FromResult(condition != "migration"));
        var codes = new Mock<IAccountRecoveryCodeService>();
        codes.Setup(value => value.GenerateAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync(["one-time-display"]);
        codes.Setup(value => value.GetStatusAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new AccountRecoveryCodeStatusDto(true, 4));
        using var services = new ServiceCollection().AddSingleton(auth.Object).AddSingleton(lifecycle).AddSingleton(migration).BuildServiceProvider();
        var http = new DefaultHttpContext { User = principal, Session = new MemorySession(), RequestServices = services };
        if (condition != "no-proof")
        {
            MfaEnrollmentSession.Begin(http.Session, user.Id, condition != "password-proof", securityStamp: identity.FindFirst(users.Object.Options.ClaimsIdentity.SecurityStampClaimType)!.Value,
                action: MfaEnrollmentAction.RegenerateAccountRecoveryCodes);
            Assert.True(MfaEnrollmentSession.CompletePending(http.Session, principal));
        }
        if (condition == "stale-proof") user.SecurityStamp = "new";
        if (condition == "wrong-subject") http.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], IdentityConstants.ApplicationScheme));
        var controller = new AccountRecoveryCodesController(codes.Object, users.Object, lifecycle, migration)
        { ControllerContext = new ControllerContext { HttpContext = http } };
        var result = await controller.Generate(default);
        if (condition == "none")
        {
            Assert.IsType<OkObjectResult>(result);
            Assert.Equal("no-store", http.Response.Headers.CacheControl);
            Assert.False(MfaEnrollmentSession.HasFreshProof(http.Session, user.Id));
            Assert.IsType<ObjectResult>(await controller.Generate(default));
            codes.Verify(value => value.GenerateAsync(user, It.IsAny<CancellationToken>()), Times.Once);
            Assert.Equal(new AccountRecoveryCodeStatusDto(true, 4), Assert.IsType<OkObjectResult>(await controller.GetStatus(default)).Value);
        }
        else
        {
            Assert.True(result is UnauthorizedObjectResult || result is ObjectResult { StatusCode: 403 });
            codes.Verify(value => value.GenerateAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
