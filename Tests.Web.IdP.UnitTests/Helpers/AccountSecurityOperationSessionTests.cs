using System.Security.Claims;
using System.Text.Json.Nodes;
using Core.Application;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP.Helpers;

namespace Tests.Web.IdP.UnitTests.Helpers;

public class AccountSecurityOperationSessionTests
{
    [Theory]
    [InlineData("cookie-refresh", false)]
    [InlineData("new-passkey", false)]
    [InlineData("existing-passkey", true)]
    public async Task Enrollment_ShouldRequirePerformedProofFromSnapshotBeforeGrantingManagementAuthority(string verification, bool expected)
    {
        using var fixture = new Fixture();
        fixture.Db.UserCredentials.Add(new UserCredential { UserId = fixture.User.Id, CredentialId = [1] });
        await fixture.Db.SaveChangesAsync();
        await AccountSecurityOperationSession.BeginAsync(fixture.Http, fixture.User,
            AccountSecurityOperationSession.MfaEnrollmentPurpose, fixture.User.Id.ToString());
        MfaEnrollmentSession.Begin(fixture.Http.Session, fixture.User.Id, true, securityStamp: fixture.User.SecurityStamp);
        if (verification != "cookie-refresh")
            AccountSecurityOperationSession.MarkVerified(fixture.Http, fixture.User, "passkey",
                verification == "existing-passkey" ? "AQ" : "Ag");
        var cookie = await fixture.Http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        Assert.Equal(expected, MfaEnrollmentSession.CompletePending(fixture.Http, cookie.Principal!));
        Assert.Equal(expected, MfaEnrollmentSession.HasFreshProof(fixture.Http.Session, fixture.User.Id));
    }

    [Theory]
    [InlineData("old-cookie")]
    [InlineData("new-totp")]
    [InlineData("new-passkey")]
    [InlineData("wrong-user")]
    [InlineData("stamp")]
    [InlineData("expired")]
    [InlineData("target")]
    [InlineData("purpose")]
    [InlineData("nonce")]
    [InlineData("consumed")]
    public async Task IsAuthorizedAsync_ShouldRejectUnprovenOrUnboundOperation(string condition)
    {
        using var fixture = new Fixture();
        var nonce = await AccountSecurityOperationSession.BeginAsync(fixture.Http, fixture.User,
            AccountSecurityOperationSession.ExternalLinkPurpose, "Google");
        if (condition == "new-totp")
        {
            fixture.User.TwoFactorEnabled = true;
            AccountSecurityOperationSession.MarkVerified(fixture.Http, fixture.User, "totp");
        }
        else if (condition == "new-passkey")
            AccountSecurityOperationSession.MarkVerified(fixture.Http, fixture.User, "passkey", "new-key");
        else if (condition != "old-cookie")
            AccountSecurityOperationSession.MarkVerified(fixture.Http, fixture.User, "password");
        if (condition == "wrong-user") fixture.User.Id = Guid.NewGuid();
        if (condition == "stamp") fixture.User.SecurityStamp = "changed";
        if (condition == "expired")
        {
            var state = JsonNode.Parse(fixture.Http.Session.GetString("account-security.operation")!)!;
            state["ExpiresUtc"] = DateTimeOffset.UtcNow.AddMinutes(-1);
            fixture.Http.Session.SetString("account-security.operation", state.ToJsonString());
        }
        if (condition == "consumed") AccountSecurityOperationSession.Consume(fixture.Http);
        Assert.False(await AccountSecurityOperationSession.IsAuthorizedAsync(fixture.Http, fixture.User,
            condition == "purpose" ? AccountSecurityOperationSession.MfaResetPurpose : AccountSecurityOperationSession.ExternalLinkPurpose,
            condition == "target" ? "Microsoft" : "Google", condition == "nonce" ? "different" : nonce));
    }

    [Theory]
    [InlineData("password")]
    [InlineData("totp")]
    [InlineData("email")]
    [InlineData("passkey")]
    public async Task IsAuthorizedAsync_ShouldAllowFreshEstablishedMethodIncludingPasswordlessAccount(string method)
    {
        using var fixture = new Fixture();
        fixture.User.TwoFactorEnabled = method == "totp";
        fixture.User.EmailMfaEnabled = method == "email";
        if (method == "passkey")
        {
            fixture.Db.UserCredentials.Add(new UserCredential { UserId = fixture.User.Id, CredentialId = [1, 2, 3] });
            await fixture.Db.SaveChangesAsync();
        }
        var nonce = await AccountSecurityOperationSession.BeginAsync(fixture.Http, fixture.User,
            AccountSecurityOperationSession.ExternalLinkPurpose, "Google");
        AccountSecurityOperationSession.MarkVerified(fixture.Http, fixture.User, method, "AQID");
        Assert.True(await AccountSecurityOperationSession.IsAuthorizedAsync(fixture.Http, fixture.User,
            AccountSecurityOperationSession.ExternalLinkPurpose, "Google", nonce));
    }

    [Fact]
    public async Task MfaReset_ShouldRequireExistingMfaAndRejectPasswordProof()
    {
        using var fixture = new Fixture();
        Assert.Null(await AccountSecurityOperationSession.BeginAsync(fixture.Http, fixture.User,
            AccountSecurityOperationSession.MfaResetPurpose, "target"));
        fixture.User.TwoFactorEnabled = true;
        await AccountSecurityOperationSession.BeginAsync(fixture.Http, fixture.User,
            AccountSecurityOperationSession.MfaResetPurpose, "target");
        AccountSecurityOperationSession.MarkVerified(fixture.Http, fixture.User, "password");
        Assert.False(await AccountSecurityOperationSession.IsAuthorizedAsync(fixture.Http, fixture.User,
            AccountSecurityOperationSession.MfaResetPurpose, "target"));
        AccountSecurityOperationSession.MarkVerified(fixture.Http, fixture.User, "totp");
        Assert.True(await AccountSecurityOperationSession.IsAuthorizedAsync(fixture.Http, fixture.User,
            AccountSecurityOperationSession.MfaResetPurpose, "target"));
    }

    private sealed class Fixture : IDisposable
    {
        public ApplicationDbContext Db { get; } = new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public ApplicationUser User { get; } = new() { Id = Guid.NewGuid(), IsActive = true, SecurityStamp = "existing", Email = "user@example.test" };
        public DefaultHttpContext Http { get; } = new();
        private readonly ServiceProvider _services;

        public Fixture()
        {
            var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
            var auth = new Mock<IAuthenticationService>();
            auth.Setup(value => value.SignOutAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<AuthenticationProperties>())).Returns(Task.CompletedTask);
            auth.Setup(value => value.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
                .ReturnsAsync(() => AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, User.Id.ToString()), new Claim(users.Object.Options.ClaimsIdentity.SecurityStampClaimType, "existing"),
                     new Claim(AuthConstants.ClaimTypes.Amr, AuthConstants.Amr.Mfa)], IdentityConstants.ApplicationScheme)), IdentityConstants.ApplicationScheme)));
            _services = new ServiceCollection().AddSingleton<IApplicationDbContext>(Db).AddSingleton(users.Object)
                .AddSingleton(auth.Object).BuildServiceProvider();
            Http.RequestServices = _services;
            Http.Session = new MemorySession();
        }
        public void Dispose() { _services.Dispose(); Db.Dispose(); }
    }
}
