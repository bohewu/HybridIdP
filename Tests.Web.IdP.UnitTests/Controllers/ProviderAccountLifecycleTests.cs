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
using System.Net;
using System.Text;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provision_ShouldRetainEmailAssuranceWithoutEmailOrPersonLinking(bool verified)
    {
        await using var fixture = await Fixture.CreateAsync();
        var contact = new Person { Id = Guid.NewGuid(), Email = "shared@example.test" };
        fixture.Db.Persons.Add(contact);
        await fixture.Db.SaveChangesAsync();
        var auth = Auth("readable-account");
        auth.Email = contact.Email;
        auth.EmailVerified = verified;
        var result = await fixture.Jit.ProvisionExternalUserAsync(auth);
        Assert.Equal("readable-account", result.UserName);
        Assert.NotEqual(contact.Id, result.PersonId);
        Assert.Equal(contact.Email, result.Email);
        Assert.Equal(verified, result.EmailConfirmed);
        Assert.Equal(contact.Email, (await fixture.Db.Persons.SingleAsync(person => person.Id == result.PersonId)).Email);
        Assert.Equal(2, await fixture.Db.Persons.CountAsync());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("existing@example.test", false)]
    [InlineData("factor@example.test", true)]
    public async Task Provision_ShouldFillMissingContactWithoutReplacingExistingAddressOrMfa(string? existingEmail, bool emailMfa)
    {
        await using var fixture = await Fixture.CreateAsync();
        var user = await fixture.Jit.ProvisionExternalUserAsync(Auth("contact-account"));
        user.Email = existingEmail;
        user.EmailConfirmed = existingEmail is not null;
        user.EmailMfaEnabled = emailMfa;
        user.EmailMfaCode = "pending-factor-code";
        user.EmailMfaCodeExpiry = DateTime.UtcNow.AddMinutes(5);
        Assert.True((await fixture.Users.UpdateAsync(user)).Succeeded);
        var originalStamp = user.SecurityStamp;
        var auth = Auth("contact-account");
        auth.Email = "upstream@example.test";

        var result = await fixture.Jit.ProvisionExternalUserAsync(auth);
        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Users.FindByIdAsync(result.Id.ToString());
        Assert.Equal(existingEmail ?? auth.Email, saved!.Email);
        Assert.Equal(existingEmail is not null, saved.EmailConfirmed);
        Assert.Equal(emailMfa, saved.EmailMfaEnabled);
        Assert.Equal(originalStamp, saved.SecurityStamp);
        Assert.Equal(existingEmail is null ? null : "pending-factor-code", saved.EmailMfaCode);
        Assert.Equal(auth.Email, (await fixture.Db.Persons.SingleAsync()).Email);
        Assert.Equal(user.Id, saved.Id);
        Assert.Single(await fixture.Db.UserLogins.ToListAsync());
    }

    [Theory]
    [InlineData("source", "confirmed", 1)]
    [InlineData("other-source", "snapshot_missing", 0)]
    public async Task Login_ShouldRetainContactAndExposeSnapshotOnlyUnderItsConfiguredSource(
        string previewSource, string expectedStatus, int expectedProperties)
    {
        await using var fixture = await Fixture.CreateAsync();
        var source = new ProviderProfileSourceOptions
        {
            ProviderNamespace = "example.provider", Endpoint = "https://profile.example.test/profile",
            SharedSecret = "synthetic-profile-secret", AllowedProperties = new() { ["divis"] = "String" }
        };
        using var http = new HttpClient(new ProfileResponseHandler());
        var profileOptions = new ProviderProfileOptions { Enabled = true, Sources = new() { ["source"] = source } };
        var profiles = new ProviderProfileService(fixture.Db, new ProviderProfileClient(http),
            Microsoft.Extensions.Options.Options.Create(profileOptions));
        var login = fixture.Login("fresh-provider-account", new AssuredProfile
        { Email = "contact@example.test", AssuredFields = [AssuredProfileField.Email] }, profiles,
            trustedEmailNamespace: "example.provider");

        var result = await login.AuthenticateAsync("upstream-alias", "password");

        Assert.True(result.IsSuccess);
        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Users.FindByIdAsync(result.User!.Id.ToString());
        Assert.Equal("contact@example.test", saved!.Email);
        Assert.True(saved.EmailConfirmed);
        Assert.Equal("contact@example.test", (await fixture.Db.Persons.AsNoTracking().SingleAsync()).Email);
        // A different source alias must not borrow the original source's snapshot.
        profileOptions.Sources["other-source"] = source;
        var snapshot = await profiles.ReadSnapshotAsync(saved, previewSource);
        Assert.Equal(expectedStatus, snapshot.Status);
        Assert.Equal(expectedProperties, snapshot.Properties.Count);
        if (expectedProperties > 0) Assert.Equal("4", snapshot.Properties["divis"].GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Login_ShouldRepairMissingEmailWithoutRecreatingAccount(bool trustEmail)
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = await fixture.Jit.ProvisionExternalUserAsync(Auth("existing-provider-account"));
        var personId = original.PersonId;
        original.TwoFactorEnabled = true;
        original.RecoveryCodes = "existing-recovery-material";
        Assert.True((await fixture.Users.UpdateAsync(original)).Succeeded);
        Assert.True((await fixture.Users.AddToRoleAsync(original, "OldAuthority")).Succeeded);
        var originalStamp = original.SecurityStamp;
        var grant = new OpenIddictEntityFrameworkCoreAuthorization<Guid>
        { Id = Guid.NewGuid(), Subject = original.Id.ToString(), Status = "valid", Type = "permanent" };
        fixture.Db.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().Add(grant);
        await fixture.Db.SaveChangesAsync();
        var login = fixture.Login("existing-provider-account", new AssuredProfile
        { Email = "verified@example.test", AssuredFields = [AssuredProfileField.Email] },
            trustedEmailNamespace: trustEmail ? "example.provider" : null);

        var result = await login.AuthenticateAsync("existing-provider-account", "password");

        Assert.True(result.IsSuccess);
        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Users.FindByIdAsync(original.Id.ToString());
        Assert.Equal(original.Id, result.User!.Id);
        Assert.Equal(personId, saved!.PersonId);
        Assert.Equal("verified@example.test", saved.Email);
        Assert.Equal(trustEmail, saved.EmailConfirmed);
        Assert.Equal(originalStamp, saved.SecurityStamp);
        Assert.True(saved.TwoFactorEnabled);
        Assert.Equal("existing-recovery-material", saved.RecoveryCodes);
        Assert.Equal(new[] { "OldAuthority" }, await fixture.Users.GetRolesAsync(saved));
        Assert.Equal(grant.Id, (await fixture.Db.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().SingleAsync()).Id);
        Assert.Equal("verified@example.test", (await fixture.Db.Persons.SingleAsync()).Email);
        Assert.Single(await fixture.Db.Users.ToListAsync());
        var binding = await fixture.Db.UserLogins.SingleAsync();
        Assert.Equal(original.Id, binding.UserId);
        Assert.Equal("example.provider", binding.LoginProvider);
        Assert.Equal("subject", binding.ProviderKey);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("example.provider", true)]
    [InlineData("Example.Provider", false)]
    [InlineData("example.provider ", false)]
    [InlineData("other.provider", false)]
    public async Task Login_ShouldStoreNewContactAndConfirmOnlyExactTrustedSource(string? trustedNamespace, bool confirmed)
    {
        await using var fixture = await Fixture.CreateAsync();
        var otherPerson = new Person { Id = Guid.NewGuid(), Email = "contact@example.test" };
        fixture.Db.Persons.Add(otherPerson);
        await fixture.Db.SaveChangesAsync();
        var login = fixture.Login("new-provider-account", new AssuredProfile
        { Email = otherPerson.Email, AssuredFields = [AssuredProfileField.Email] },
            trustedEmailNamespace: trustedNamespace);

        var result = await login.AuthenticateAsync("upstream-alias", "password");

        Assert.True(result.IsSuccess);
        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Users.FindByIdAsync(result.User!.Id.ToString());
        Assert.Equal(otherPerson.Email, saved!.Email);
        Assert.Equal(confirmed, saved.EmailConfirmed);
        Assert.NotEqual(otherPerson.Id, saved.PersonId);
        Assert.Equal(otherPerson.Email, (await fixture.Db.Persons.SingleAsync(p => p.Id == saved.PersonId)).Email);
        Assert.Equal(2, await fixture.Db.Persons.CountAsync());
        Assert.Single(await fixture.Db.UserLogins.ToListAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Login_ShouldRequireEmailAssuranceEvenForTrustedSource(bool trustEmail, bool omitEmail)
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Login("new-provider-account", new AssuredProfile
        { Email = omitEmail ? null : "unassured@example.test", AssuredFields = [] },
            trustedEmailNamespace: trustEmail ? "example.provider" : null)
            .AuthenticateAsync("upstream-alias", "password");

        Assert.Equal(omitEmail, result.IsSuccess);
        if (omitEmail)
        {
            Assert.Null(result.User!.Email);
            Assert.False(result.User.EmailConfirmed);
        }
        else
        {
            Assert.Empty(await fixture.Db.Users.ToListAsync());
            Assert.Empty(await fixture.Db.Persons.ToListAsync());
            Assert.Empty(await fixture.Db.UserLogins.ToListAsync());
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task Login_ShouldPreserveConfirmedAddressAndEnrolledFactor(bool trustEmail, bool emailMfa, bool changedEmail)
    {
        await using var fixture = await Fixture.CreateAsync();
        var auth = Auth("existing-provider-account");
        auth.Email = "original@example.test";
        auth.EmailVerified = true;
        var original = await fixture.Jit.ProvisionExternalUserAsync(auth);
        original.EmailMfaEnabled = emailMfa;
        original.TwoFactorEnabled = emailMfa;
        original.EmailMfaCode = "existing-code-hash";
        original.EmailMfaCodeExpiry = DateTime.UtcNow.AddMinutes(5);
        original.EmailMfaVerificationAttempts = 2;
        Assert.True((await fixture.Users.UpdateAsync(original)).Succeeded);
        var stamp = original.SecurityStamp;
        var expiry = original.EmailMfaCodeExpiry;
        var result = await fixture.Login("existing-provider-account", new AssuredProfile
        { Email = changedEmail ? "new@example.test" : original.Email, AssuredFields = [AssuredProfileField.Email] },
            trustedEmailNamespace: trustEmail ? "example.provider" : null)
            .AuthenticateAsync("existing-provider-account", "password");

        Assert.True(result.IsSuccess);
        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Users.FindByIdAsync(original.Id.ToString());
        Assert.Equal("original@example.test", saved!.Email);
        Assert.True(saved.EmailConfirmed);
        Assert.Equal(emailMfa, saved.EmailMfaEnabled);
        Assert.Equal(emailMfa, saved.TwoFactorEnabled);
        Assert.Equal("existing-code-hash", saved.EmailMfaCode);
        Assert.Equal(expiry, saved.EmailMfaCodeExpiry);
        Assert.Equal(2, saved.EmailMfaVerificationAttempts);
        Assert.Equal(stamp, saved.SecurityStamp);
        Assert.Equal(original.PersonId, saved.PersonId);
        Assert.Equal(original.Id, (await fixture.Db.UserLogins.SingleAsync()).UserId);
    }

    private sealed class ProfileResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"contractVersion":"1.0","providerNamespace":"example.provider","stableSubject":"subject","extraProperties":{"divis":"4"}}
                    """, Encoding.UTF8, "application/json")
            });
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

        public LoginService Login(string canonical, AssuredProfile? profile = null, IProviderProfileService? profiles = null,
            string? trustedEmailNamespace = null)
        {
            var proof = new Mock<IProofProvider>();
            proof.Setup(provider => provider.ProveAsync(It.IsAny<ProofRequest>(), "password", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProofResult { Outcome = ProofOutcome.Authenticated,
                    ProviderNamespace = "example.provider", StableSubject = "subject", CanonicalAccount = canonical,
                    Assurance = new() { StableSubjectAssured = true, CanonicalAccountAssured = true }, Profile = profile });
            return new LoginService(Users, Mock.Of<ISecurityPolicyService>(), Jit, Db,
                Mock.Of<ILogger<LoginService>>(), Microsoft.Extensions.Options.Options.Create(new ExternalLoginOptions()),
                proofProvider: proof.Object, credentialMigrationStateStore: Mock.Of<ICredentialMigrationStateStore>(),
                providerProofOptions: Microsoft.Extensions.Options.Options.Create(new ProviderProofOptions
                { Enabled = true, TrustedProviderNamespace = "example.provider",
                    TrustedEmailProviderNamespaces = trustedEmailNamespace is null ? [] : [trustedEmailNamespace] }),
                providerProfileService: profiles);
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

