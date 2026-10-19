using Core.Application;
using Core.Application.DTOs;
using Core.Application.Options;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure;
using Infrastructure.Identity;
using Infrastructure.Services;
using Infrastructure.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.EntityFrameworkCore.Models;
using Web.IdP;
using Web.IdP.Controllers.Admin;
using Web.IdP.Services;

namespace Tests.Web.IdP.UnitTests.Controllers;

public sealed class ProviderAccountLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteUser_ShouldReleaseNameForFreshProviderProvisioning(bool canonical)
    {
        await using var fixture = await Fixture.CreateAsync();
        var auth = Auth(canonical ? "readable-account" : null);
        var original = await fixture.Jit.ProvisionExternalUserAsync(auth);
        var originalName = original.UserName;
        original.Email = "old@example.test";
        original.EmailConfirmed = true;
        original.TwoFactorEnabled = true;
        original.RecoveryCodes = "old-recovery-material";
        Assert.True((await fixture.Users.UpdateAsync(original)).Succeeded);
        Assert.True((await fixture.Users.AddToRoleAsync(original, "OldAuthority")).Succeeded);
        fixture.Db.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().Add(new()
        { Id = Guid.NewGuid(), Subject = original.Id.ToString(), Status = "valid", Type = "permanent" });
        await fixture.Db.SaveChangesAsync();
        var originalStamp = original.SecurityStamp;

        Assert.IsType<NoContentResult>(await fixture.Controller.DeleteUser(original.Id));
        var replacement = await fixture.Jit.ProvisionExternalUserAsync(auth);

        Assert.NotEqual(original.Id, replacement.Id);
        Assert.Equal(originalName, replacement.UserName);
        Assert.NotEqual(original.PersonId, replacement.PersonId);
        Assert.False(replacement.TwoFactorEnabled);
        Assert.Null(replacement.RecoveryCodes);
        Assert.Empty(await fixture.Users.GetRolesAsync(replacement));
        Assert.False(await fixture.Db.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>()
            .AnyAsync(grant => grant.Subject == replacement.Id.ToString()));
        Assert.Null(await fixture.Users.FindByEmailAsync("old@example.test"));
        Assert.True(original.IsDeleted);
        Assert.False(original.IsActive);
        Assert.NotEqual(originalStamp, original.SecurityStamp);
        Assert.Equal(replacement.Id, (await fixture.Users.FindByLoginAsync(auth.Provider, auth.ProviderKey))!.Id);
    }

    [Fact]
    public async Task Provision_ShouldPreserveExactTupleState_WhenCanonicalChanges()
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = await fixture.Jit.ProvisionExternalUserAsync(Auth(null));
        original.TwoFactorEnabled = true;
        original.EmailMfaEnabled = true;
        original.Email = "factor@example.test";
        original.EmailConfirmed = true;
        original.RecoveryCodes = "existing-factor";
        Assert.True((await fixture.Users.UpdateAsync(original)).Succeeded);
        Assert.True((await fixture.Users.AddToRoleAsync(original, "OldAuthority")).Succeeded);
        var name = original.UserName;
        var stamp = original.SecurityStamp;
        var person = original.PersonId;
        var consent = new OpenIddictEntityFrameworkCoreAuthorization<Guid>
        { Id = Guid.NewGuid(), Subject = original.Id.ToString(), Status = "valid", Type = "permanent" };
        fixture.Db.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().Add(consent);
        await fixture.Db.SaveChangesAsync();

        var repeated = await fixture.Jit.ProvisionExternalUserAsync(Auth("renamed-upstream"));

        Assert.Equal(original.Id, repeated.Id);
        Assert.Equal(name, repeated.UserName);
        Assert.Equal(stamp, repeated.SecurityStamp);
        Assert.Equal(person, repeated.PersonId);
        Assert.True(repeated.TwoFactorEnabled);
        Assert.True(repeated.EmailMfaEnabled);
        Assert.Equal("factor@example.test", repeated.Email);
        Assert.Equal("existing-factor", repeated.RecoveryCodes);
        Assert.Equal(new[] { "OldAuthority" }, await fixture.Users.GetRolesAsync(repeated));
        Assert.Equal(consent.Id, (await fixture.Db.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().SingleAsync()).Id);
        Assert.Single(await fixture.Db.Users.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provision_ShouldRejectOccupiedCanonicalWithoutLinkingOrCreatingPerson(bool otherProvider)
    {
        await using var fixture = await Fixture.CreateAsync();
        var holder = new ApplicationUser { UserName = "readable-account", Email = "holder@example.test" };
        Assert.True((await fixture.Users.CreateAsync(holder)).Succeeded);
        if (otherProvider) Assert.True((await fixture.Users.AddLoginAsync(holder, new("other.provider", "other-subject", "Other"))).Succeeded);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Jit.ProvisionExternalUserAsync(Auth("READABLE-ACCOUNT")));
        await fixture.Db.SaveChangesAsync();

        Assert.Null(await fixture.Users.FindByLoginAsync("example.provider", "subject"));
        Assert.Single(await fixture.Db.Users.ToListAsync());
        Assert.Empty(await fixture.Db.Persons.ToListAsync());
        Assert.Equal(otherProvider ? 1 : 0, (await fixture.Users.GetLoginsAsync(holder)).Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("account/name")]
    [InlineData("oversize")]
    public async Task Provision_ShouldRejectInvalidCanonicalBeforeMutation(string canonical)
    {
        await using var fixture = await Fixture.CreateAsync();
        if (canonical == "oversize") canonical = new string('a', 257);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Jit.ProvisionExternalUserAsync(Auth(canonical)));
        await fixture.Db.SaveChangesAsync();
        Assert.Empty(await fixture.Db.Users.ToListAsync());
        Assert.Empty(await fixture.Db.Persons.ToListAsync());
    }

    [Fact]
    public async Task Provision_ShouldUseCanonicalWithoutEmailOrPersonLinking()
    {
        await using var fixture = await Fixture.CreateAsync();
        var contact = new Person { Id = Guid.NewGuid(), Email = "shared@example.test" };
        fixture.Db.Persons.Add(contact);
        await fixture.Db.SaveChangesAsync();
        var auth = Auth("readable-account");
        auth.Email = contact.Email;
        auth.EmailVerified = true;
        var result = await fixture.Jit.ProvisionExternalUserAsync(auth);
        Assert.Equal("readable-account", result.UserName);
        Assert.NotEqual(contact.Id, result.PersonId);
        Assert.Null(result.Email);
        Assert.False(result.EmailConfirmed);
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("inactive")]
    [InlineData("person")]
    [InlineData("directory")]
    public async Task DeleteUser_ShouldRetainTerminalDenialBindings(string protectedState)
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = await fixture.Jit.ProvisionExternalUserAsync(Auth("protected-account"));
        if (protectedState == "deleted") original.IsDeleted = true;
        if (protectedState == "inactive") original.IsActive = false;
        if (protectedState == "person") (await fixture.Db.Persons.SingleAsync()).Status = PersonStatus.Suspended;
        if (protectedState == "directory") fixture.Db.ProviderSubjectDirectoryBindings.Add(new(
            original.Id, "example.provider", "subject", Guid.NewGuid(), DateTime.UtcNow));
        await fixture.Db.SaveChangesAsync();

        Assert.IsType<NoContentResult>(await fixture.Controller.DeleteUser(original.Id));
        Assert.Equal("protected-account", original.UserName);
        Assert.Equal(original.Id, (await fixture.Users.FindByLoginAsync("example.provider", "subject"))!.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Jit.ProvisionExternalUserAsync(Auth("new-name")));
    }

    [Fact]
    public async Task Login_ShouldReprovisionAfterDelete_WithoutStaleEmailSelectingDeletedAccount()
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = await fixture.Jit.ProvisionExternalUserAsync(Auth(null));
        original.Email = "readable@example.test";
        Assert.True((await fixture.Users.UpdateAsync(original)).Succeeded);
        Assert.IsType<NoContentResult>(await fixture.Controller.DeleteUser(original.Id));
        var result = await fixture.Login("readable@example.test").AuthenticateAsync("readable@example.test", "password");
        Assert.True(result.IsSuccess);
        Assert.NotEqual(original.Id, result.User!.Id);
        Assert.Equal("readable@example.test", result.User.UserName);
        Assert.False(result.User.EmailConfirmed);
    }

    [Theory]
    [InlineData("account/name", false)]
    [InlineData("occupied", true)]
    public async Task Login_ShouldReturnControlledDenial_WhenCanonicalCannotBeCreated(string canonical, bool occupied)
    {
        await using var fixture = await Fixture.CreateAsync();
        if (occupied) Assert.True((await fixture.Users.CreateAsync(new() { UserName = canonical })).Succeeded);
        var result = await fixture.Login(canonical).AuthenticateAsync("upstream-alias", "password");
        Assert.False(result.IsSuccess);
        Assert.Empty(await fixture.Db.Persons.ToListAsync());
        Assert.Null(await fixture.Users.FindByLoginAsync("example.provider", "subject"));
    }

    private static ExternalAuthResult Auth(string? canonical) => new()
    { Provider = "example.provider", ProviderKey = "subject", AssuredCanonicalAccount = canonical };

    [Fact]
    public async Task Login_ShouldRollbackNewAccountAndPerson_WhenBindingPersistenceFails()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER RejectProviderBinding BEFORE INSERT ON AspNetUserLogins
            BEGIN SELECT RAISE(ABORT, 'injected binding write failure'); END;
            """);
        var result = await fixture.Login("readable-account").AuthenticateAsync("upstream-alias", "password");
        Assert.False(result.IsSuccess);
        await fixture.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectProviderBinding;");
        await fixture.Db.SaveChangesAsync();
        Assert.Empty(await fixture.Db.Users.ToListAsync());
        Assert.Empty(await fixture.Db.Persons.ToListAsync());
        Assert.Empty(await fixture.Db.UserLogins.ToListAsync());
    }

    [Fact]
    public async Task DeleteUser_ShouldPreserveLiveIdentity_WhenRetiredNameWriteIsRejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = await fixture.Jit.ProvisionExternalUserAsync(Auth("readable-account"));
        var originalStamp = original.SecurityStamp;
        Assert.True((await fixture.Users.CreateAsync(new() { UserName = $"deleted_{original.Id:N}" })).Succeeded);

        Assert.IsType<BadRequestObjectResult>(await fixture.Controller.DeleteUser(original.Id));
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var retained = await fixture.Users.FindByIdAsync(original.Id.ToString());
        Assert.False(retained!.IsDeleted);
        Assert.True(retained.IsActive);
        Assert.Equal("readable-account", retained.UserName);
        Assert.Equal(originalStamp, retained.SecurityStamp);
        Assert.Equal(original.Id, (await fixture.Users.FindByLoginAsync("example.provider", "subject"))!.Id);
    }

    [Fact]
    public async Task DeleteUser_ShouldReportFailureAndRetainDenial_WhenUnlinkWriteFails()
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = await fixture.Jit.ProvisionExternalUserAsync(Auth("readable-account"));
        var originalStamp = original.SecurityStamp;
        await fixture.Db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER RejectProviderUnlink BEFORE DELETE ON AspNetUserLogins
            BEGIN SELECT RAISE(ABORT, 'injected unlink write failure'); END;
            """);

        var error = Assert.IsType<ObjectResult>(await fixture.Controller.DeleteUser(original.Id));
        Assert.Equal(500, error.StatusCode);
        fixture.Db.ChangeTracker.Clear();
        var retained = await fixture.Users.FindByLoginAsync("example.provider", "subject");
        Assert.Equal(original.Id, retained!.Id);
        Assert.True(retained.IsDeleted);
        Assert.False(retained.IsActive);
        Assert.NotEqual(originalStamp, retained.SecurityStamp);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Jit.ProvisionExternalUserAsync(Auth("new-name")));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;
        public ApplicationDbContext Db { get; }
        public UserManager<ApplicationUser> Users { get; }
        public JitProvisioningService Jit { get; }
        public UsersController Controller { get; }

        public LoginService Login(string canonical)
        {
            var proof = new Mock<IProofProvider>();
            proof.Setup(provider => provider.ProveAsync(It.IsAny<ProofRequest>(), "password", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProofResult { Outcome = ProofOutcome.Authenticated,
                    ProviderNamespace = "example.provider", StableSubject = "subject", CanonicalAccount = canonical,
                    Assurance = new() { StableSubjectAssured = true, CanonicalAccountAssured = true } });
            return new LoginService(Users, Mock.Of<ISecurityPolicyService>(), Jit, Db,
                Mock.Of<ILogger<LoginService>>(), Microsoft.Extensions.Options.Options.Create(new ExternalLoginOptions()),
                proofProvider: proof.Object, credentialMigrationStateStore: Mock.Of<ICredentialMigrationStateStore>(),
                providerProofOptions: Microsoft.Extensions.Options.Options.Create(new ProviderProofOptions
                { Enabled = true, TrustedProviderNamespace = "example.provider" }));
        }

        private Fixture(SqliteConnection connection)
        {
            _connection = connection;
            var services = new ServiceCollection().AddLogging();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection).UseOpenIddict<Guid>());
            services.AddIdentityCore<ApplicationUser>().AddRoles<ApplicationRole>().AddEntityFrameworkStores<ApplicationDbContext>();
            _provider = services.BuildServiceProvider();
            _scope = _provider.CreateAsyncScope();
            Db = _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Users = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Jit = new JitProvisioningService(Users, Db, Microsoft.Extensions.Options.Options.Create(new ExternalLoginOptions { AutoProvisionDefaultRole = null }));
            Controller = new UsersController(Mock.Of<ICurrentUserLifecycleEligibility>(), Mock.Of<IUserManagementService>(),
                Users, _scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>(), Mock.Of<ISessionService>(),
                Mock.Of<ILoginHistoryService>(), Db, Mock.Of<IStringLocalizer<SharedResource>>(), Mock.Of<IImpersonationService>(),
                Mock.Of<Microsoft.AspNetCore.Authorization.IAuthorizationService>(), Microsoft.Extensions.Options.Options.Create(new PrivilegedRoleProtectionOptions()),
                Mock.Of<ILogger<UsersController>>(), Mock.Of<IRecoveryAssistanceService>(), Mock.Of<IAuditService>())
                { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var fixture = new Fixture(connection);
            await fixture.Db.Database.EnsureCreatedAsync();
            var roles = fixture._scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            Assert.True((await roles.CreateAsync(new ApplicationRole { Name = "OldAuthority" })).Succeeded);
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}

