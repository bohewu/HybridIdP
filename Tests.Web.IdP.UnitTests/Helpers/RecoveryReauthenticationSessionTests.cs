using System.Security.Claims;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP.Helpers;

namespace Tests.Web.IdP.UnitTests.Helpers;

public sealed class RecoveryReauthenticationSessionTests
{
    [Theory]
    [InlineData("local")]
    [InlineData("directory")]
    [InlineData("external")]
    public async Task HardwareCompletion_IssuesOwnBoundGrantExactlyOnce(string authority)
    {
        await using var f = await Fixture.CreateAsync(authority);
        RecoveryReauthenticationSession.MarkFullCompletion(f.Http, f.User.Id, hardware: true);
        await RecoveryReauthenticationSession.CompleteAsync(f.Http, Principal(f.User.Id));
        Assert.NotEqual(Guid.Empty, RecoveryReauthenticationSession.Context(f.Http).StepUpGrantId);
        Assert.Single(await f.Db.RecoveryStepUpGrants.ToListAsync());
        await RecoveryReauthenticationSession.CompleteAsync(f.Http, Principal(f.User.Id));
        Assert.Single(await f.Db.RecoveryStepUpGrants.ToListAsync());
        Assert.False(RecoveryReauthenticationSession.HasPending(f.Http));
    }

    [Theory]
    [InlineData("refresh")]
    [InlineData("mfaWithoutPrimary")]
    [InlineData("wrongAccount")]
    [InlineData("stamp")]
    [InlineData("enrollment")]
    [InlineData("passwordOnly")]
    public async Task UntrustedOrIncompleteCompletion_CannotMintRecoveryGrant(string scenario)
    {
        await using var f = await Fixture.CreateAsync();
        if (scenario == "mfaWithoutPrimary") RecoveryReauthenticationSession.MarkFullCompletion(f.Http, f.User.Id);
        if (scenario is "wrongAccount" or "stamp" or "passwordOnly") RecoveryReauthenticationSession.MarkFullCompletion(f.Http, f.User.Id, hardware: true);
        if (scenario == "stamp") { f.User.SecurityStamp = "changed"; await f.Db.SaveChangesAsync(); }
        if (scenario == "enrollment") { MfaEnrollmentSession.Begin(f.Http.Session); MfaEnrollmentSession.CompletePending(f.Http.Session, Principal(f.User.Id)); }
        await RecoveryReauthenticationSession.CompleteAsync(f.Http,
            Principal(scenario == "wrongAccount" ? Guid.NewGuid() : f.User.Id, scenario != "passwordOnly"));
        Assert.Empty(await f.Db.RecoveryStepUpGrants.ToListAsync());
    }

    [Theory]
    [InlineData("local")]
    [InlineData("directory")]
    public async Task ExistingPasswordAuthority_ThenFullMfa_IssuesFreshGrant(string authority)
    {
        await using var f = await Fixture.CreateAsync(authority);
        var login = new Mock<ILoginService>(MockBehavior.Strict);
        login.Setup(l => l.AuthenticateAsync("synthetic", "password", It.IsAny<CancellationToken>())).ReturnsAsync(LoginResult.Success(f.User));
        var users = Users(f.User);
        var result = await RecoveryReauthenticationSession.AuthenticatePasswordAsync(f.Http, "synthetic", "password", login.Object, users.Object, default);
        Assert.True(result.IsSuccess);
        await RecoveryReauthenticationSession.CompleteAsync(f.Http, Principal(f.User.Id));
        Assert.Empty(await f.Db.RecoveryStepUpGrants.ToListAsync());
        RecoveryReauthenticationSession.MarkFullCompletion(f.Http, f.User.Id);
        await RecoveryReauthenticationSession.CompleteAsync(f.Http, Principal(f.User.Id));
        Assert.Single(await f.Db.RecoveryStepUpGrants.ToListAsync());
        login.Verify(l => l.AuthenticateAsync("synthetic", "password", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("external")]
    [InlineData("wrongAccount")]
    public async Task WrongPasswordAuthority_NeverCallsPasswordProvider(string scenario)
    {
        await using var f = await Fixture.CreateAsync(scenario == "external" ? "external" : "local");
        var login = new Mock<ILoginService>(MockBehavior.Strict);
        var users = Users(scenario == "wrongAccount" ? new ApplicationUser { Id = Guid.NewGuid() } : f.User);
        var result = await RecoveryReauthenticationSession.AuthenticatePasswordAsync(f.Http, "synthetic", "password", login.Object, users.Object, default);
        Assert.False(result.IsSuccess);
        login.VerifyNoOtherCalls();
    }

    private static ClaimsPrincipal Principal(Guid id, bool mfa = true) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(AuthConstants.ClaimTypes.Amr, mfa ? AuthConstants.Amr.Mfa : AuthConstants.Amr.Password)], IdentityConstants.ApplicationScheme));
    private static Mock<UserManager<ApplicationUser>> Users(ApplicationUser user)
    {
        var mock = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        mock.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
        return mock;
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public ApplicationUser User { get; } = new() { Id = Guid.NewGuid(), UserName = "synthetic", PasswordHash = "hash", SecurityStamp = "stamp" };
        public DefaultHttpContext Http { get; } = new() { Session = new MemorySession() };
        private ServiceProvider _provider = null!;
        public static async Task<Fixture> CreateAsync(string authority = "local")
        {
            var f = new Fixture(); f.Db.Users.Add(f.User);
            if (authority == "external")
            {
                f.User.PasswordHash = null;
                f.Db.UserLogins.Add(new IdentityUserLogin<Guid> { UserId = f.User.Id, LoginProvider = "Google", ProviderKey = "synthetic-subject" });
            }
            if (authority == "directory")
            {
                var b = new ProviderSubjectDirectoryBinding(f.User.Id, "p", "s", Guid.NewGuid(), DateTime.UtcNow);
                var m = new CredentialMigrationStateRecord(f.User.Id, b.Id, DateTimeOffset.UtcNow);
                m.Advance(CredentialMigrationState.ProofValidated, EffectiveEmailOtpRequirement.NotRequired, DateTimeOffset.UtcNow);
                m.Advance(CredentialMigrationState.DirectoryCredentialCommitted, DateTimeOffset.UtcNow);
                m.Advance(CredentialMigrationState.LocalFinalized, DateTimeOffset.UtcNow);
                f.Db.ProviderSubjectDirectoryBindings.Add(b); f.Db.CredentialMigrationStateRecords.Add(m);
            }
            await f.Db.SaveChangesAsync();
            var stepUp = new RecoveryEmailStepUpService(f.Db, Microsoft.Extensions.Options.Options.Create(new RecoveryEmailSelectionOptions { Enabled = true, SelfServiceEnabled = true }));
            f._provider = new ServiceCollection().AddSingleton(stepUp).BuildServiceProvider(); f.Http.RequestServices = f._provider;
            var state = await stepUp.ResolveAsync(f.User.Id); Assert.NotNull(state);
            RecoveryReauthenticationSession.Begin(f.Http, state); return f;
        }
        public async ValueTask DisposeAsync() { await _provider.DisposeAsync(); await Db.DisposeAsync(); }
    }
}
