using System.Security.Claims;
using Core.Application;
using Core.Application.Options;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP.Helpers;
using Web.IdP.Services;

namespace Tests.Web.IdP.UnitTests.Services;

public class AdministrativeMfaResetServiceTests
{
    [Theory]
    [InlineData("bearer")]
    [InlineData("permission")]
    [InlineData("protected")]
    [InlineData("app-admin")]
    [InlineData("inactive-role")]
    [InlineData("old-cookie")]
    [InlineData("password")]
    [InlineData("target")]
    [InlineData("reason")]
    public async Task ResetAsync_ShouldRejectUnprivilegedOrUnprovenOperatorWithoutWrites(string condition)
    {
        using var fixture = new Fixture();
        if (condition == "bearer") fixture.Bearer = true;
        if (condition == "permission") fixture.Permission = false;
        if (condition is "protected" or "app-admin" or "inactive-role")
        {
            fixture.TargetRoles = [AuthConstants.Roles.Admin];
            fixture.FullAdmin = condition != "protected";
            fixture.Identity.AddClaim(new Claim(ClaimTypes.Role, AuthConstants.Roles.Admin));
            if (condition == "app-admin") fixture.Identity.AddClaim(new Claim("app_role", AuthConstants.Roles.Admin));
            if (condition == "inactive-role") fixture.Identity.AddClaim(new Claim("active_role", "Support"));
        }
        if (condition != "old-cookie")
        {
            await AccountSecurityOperationSession.BeginAsync(fixture.Http, fixture.Actor,
                AccountSecurityOperationSession.MfaResetPurpose, fixture.Target.Id.ToString());
            var verificationMethod = condition == "password" ? condition : "totp";
            AccountSecurityOperationSession.MarkVerified(fixture.Http, fixture.Actor, verificationMethod);
        }
        var result = await fixture.Service.ResetAsync(fixture.Http, condition == "target" ? Guid.NewGuid() : fixture.Target.Id,
            condition == "reason" ? " " : "user requested factor recovery", default);
        Assert.NotEqual(200, result.StatusCode);
        fixture.Users.Verify(value => value.UpdateSecurityStampAsync(It.IsAny<ApplicationUser>()), Times.Never);
        fixture.Users.Verify(value => value.SetTwoFactorEnabledAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>()), Times.Never);
        fixture.Tokens.Verify(value => value.TryRevokeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResetAsync_ShouldAuditAndRevokeSessionsAndTokensAfterFreshInteractiveMfa(bool protectedTarget)
    {
        using var fixture = new Fixture();
        if (protectedTarget)
        {
            fixture.TargetRoles = [AuthConstants.Roles.Admin];
            fixture.FullAdmin = true;
            fixture.Identity.AddClaim(new Claim(ClaimTypes.Role, AuthConstants.Roles.Admin));
            fixture.Identity.AddClaim(new Claim("active_role", AuthConstants.Roles.Admin));
        }
        var session = new UserSession { Id = Guid.NewGuid(), UserId = fixture.Target.Id };
        fixture.Db.UserSessions.Add(session);
        await fixture.Db.SaveChangesAsync();
        await AccountSecurityOperationSession.BeginAsync(fixture.Http, fixture.Actor,
            AccountSecurityOperationSession.MfaResetPurpose, fixture.Target.Id.ToString());
        AccountSecurityOperationSession.MarkVerified(fixture.Http, fixture.Actor, "totp");
        var result = await fixture.Service.ResetAsync(fixture.Http, fixture.Target.Id, " verified recovery request ", default);
        Assert.Equal(200, result.StatusCode);
        Assert.False(fixture.Target.TwoFactorEnabled);
        Assert.False(fixture.Target.EmailMfaEnabled);
        Assert.Null(fixture.Target.EmailMfaCode);
        Assert.NotNull(session.RevokedUtc);
        fixture.Users.Verify(value => value.UpdateSecurityStampAsync(fixture.Target), Times.Once);
        fixture.Authorizations.Verify(value => value.TryRevokeAsync(fixture.Grant, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Tokens.Verify(value => value.TryRevokeAsync(fixture.Token, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Tokens.Verify(value => value.TryRevokeAsync(fixture.LinkedToken, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Audit.Verify(value => value.LogAdministrativeEventAsync("UserMfaReset", "User", fixture.Target.Id.ToString(),
            "verified recovery request", It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(await AccountSecurityOperationSession.IsAuthorizedAsync(fixture.Http, fixture.Actor,
            AccountSecurityOperationSession.MfaResetPurpose, fixture.Target.Id.ToString()));
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("authorization")]
    [InlineData("token")]
    public async Task ResetAsync_ShouldNotReportSuccessWhenPersistenceOrRevocationFails(string failure)
    {
        using var fixture = new Fixture();
        await AccountSecurityOperationSession.BeginAsync(fixture.Http, fixture.Actor,
            AccountSecurityOperationSession.MfaResetPurpose, fixture.Target.Id.ToString());
        AccountSecurityOperationSession.MarkVerified(fixture.Http, fixture.Actor, "totp");
        if (failure == "identity") fixture.Users.Setup(value => value.UpdateSecurityStampAsync(fixture.Target)).ReturnsAsync(IdentityResult.Failed());
        if (failure == "authorization") fixture.Authorizations.Setup(value => value.TryRevokeAsync(fixture.Grant, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        if (failure == "token") fixture.Tokens.Setup(value => value.TryRevokeAsync(fixture.Token, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ResetAsync(fixture.Http, fixture.Target.Id, "verified request", default));
        fixture.Audit.Verify(value => value.LogAdministrativeEventAsync("UserMfaReset", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class Fixture : IDisposable
    {
        public ApplicationDbContext Db { get; } = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public ApplicationUser Actor { get; } = new() { Id = Guid.NewGuid(), IsActive = true, SecurityStamp = "actor-stamp", TwoFactorEnabled = true };
        public ApplicationUser Target { get; } = new() { Id = Guid.NewGuid(), IsActive = true, SecurityStamp = "target-stamp", TwoFactorEnabled = true, EmailMfaEnabled = true, EmailMfaCode = "fixture-code" };
        public Mock<UserManager<ApplicationUser>> Users { get; } = new(Mock.Of<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        public Mock<IOpenIddictAuthorizationManager> Authorizations { get; } = new();
        public Mock<IOpenIddictTokenManager> Tokens { get; } = new();
        public Mock<IAuditService> Audit { get; } = new();
        public object Grant { get; } = new();
        public object Token { get; } = new();
        public object LinkedToken { get; } = new();
        public bool Bearer { get; set; }
        public bool Permission { get; set; } = true;
        public bool FullAdmin { get; set; }
        public string[] TargetRoles { get; set; } = ["User"];
        public ClaimsIdentity Identity { get; }
        public DefaultHttpContext Http { get; } = new();
        public AdministrativeMfaResetService Service { get; }
        private readonly ServiceProvider _services;

        public Fixture()
        {
            Identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Actor.Id.ToString()),
                new Claim(Users.Object.Options.ClaimsIdentity.SecurityStampClaimType, Actor.SecurityStamp!),
                new Claim(AuthConstants.ClaimTypes.Amr, AuthConstants.Amr.Mfa)], IdentityConstants.ApplicationScheme);
            var principal = new ClaimsPrincipal(Identity);
            var boundary = new Mock<IAdministrativeAuthorizationBoundary>();
            boundary.Setup(value => value.ResolveAsync()).ReturnsAsync(() => new AdministrativeAuthority(principal, Bearer, new HashSet<string>()));
            var authorization = new Mock<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
            authorization.Setup(value => value.AuthorizeAsync(principal, null, Permissions.Users.ResetMfa))
                .ReturnsAsync(() => Permission ? AuthorizationResult.Success() : AuthorizationResult.Failed());
            Users.Setup(value => value.FindByIdAsync(Actor.Id.ToString())).ReturnsAsync(Actor);
            Users.Setup(value => value.FindByIdAsync(Target.Id.ToString())).ReturnsAsync(Target);
            Users.Setup(value => value.GetRolesAsync(Target)).ReturnsAsync(() => TargetRoles);
            Users.Setup(value => value.IsInRoleAsync(Actor, AuthConstants.Roles.Admin)).ReturnsAsync(() => FullAdmin);
            Users.Setup(value => value.UpdateSecurityStampAsync(Target)).ReturnsAsync(IdentityResult.Success);
            Users.Setup(value => value.SetTwoFactorEnabledAsync(Target, false)).Callback(() => Target.TwoFactorEnabled = false).ReturnsAsync(IdentityResult.Success);
            Users.Setup(value => value.ResetAuthenticatorKeyAsync(Target)).ReturnsAsync(IdentityResult.Success);
            Users.Setup(value => value.UpdateAsync(Target)).ReturnsAsync(IdentityResult.Success);
            Authorizations.Setup(value => value.FindBySubjectAsync(Target.Id.ToString(), It.IsAny<CancellationToken>())).Returns(Records(Grant));
            Authorizations.Setup(value => value.GetIdAsync(Grant, It.IsAny<CancellationToken>())).ReturnsAsync("grant-id");
            Authorizations.Setup(value => value.TryRevokeAsync(Grant, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            Tokens.Setup(value => value.FindBySubjectAsync(Target.Id.ToString(), It.IsAny<CancellationToken>())).Returns(Records(Token));
            Tokens.Setup(value => value.FindByAuthorizationIdAsync("grant-id", It.IsAny<CancellationToken>())).Returns(Records(Token, LinkedToken));
            Tokens.Setup(value => value.GetIdAsync(Token, It.IsAny<CancellationToken>())).ReturnsAsync("token-id");
            Tokens.Setup(value => value.GetIdAsync(LinkedToken, It.IsAny<CancellationToken>())).ReturnsAsync("linked-token-id");
            Tokens.Setup(value => value.TryRevokeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var lifecycle = new Mock<ICurrentUserLifecycleEligibility>();
            lifecycle.Setup(value => value.IsEligibleAsync(Actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var migration = new Mock<IMigrationIssuanceGuard>();
            migration.Setup(value => value.CanIssueAsync(Actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var auth = new Mock<IAuthenticationService>();
            auth.Setup(value => value.AuthenticateAsync(Http, IdentityConstants.ApplicationScheme))
                .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, IdentityConstants.ApplicationScheme)));
            _services = new ServiceCollection().AddSingleton<IApplicationDbContext>(Db).AddSingleton(Users.Object).AddSingleton(auth.Object).BuildServiceProvider();
            Http.RequestServices = _services;
            Http.Session = new MemorySession();
            Service = new(boundary.Object, authorization.Object, Users.Object, Db, Authorizations.Object, Tokens.Object,
                Audit.Object, Microsoft.Extensions.Options.Options.Create(new PrivilegedRoleProtectionOptions()), lifecycle.Object, migration.Object);
        }
        private static async IAsyncEnumerable<object> Records(params object[] records)
        {
            await Task.CompletedTask;
            foreach (var record in records) yield return record;
        }
        public void Dispose() { _services.Dispose(); Db.Dispose(); }
    }
}
