using Core.Application;
using Core.Application.DTOs;
using Core.Application.Options;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure;
using Infrastructure.Identity;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class ProofProvisioningTests : IDisposable
{
    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly UserManager<ApplicationUser> _users;
    private readonly JitProvisioningService _jit;
    private readonly Mock<IProofProvider> _proof = new();
    private readonly Mock<IProviderProfileService> _profile = new();
    private readonly NativeFieldPolicyOptions _nativeFieldOptions = new();
    private readonly LoginService _login;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticateAsync_ShouldPersistFullNameAndOrganizationId_ForNewAndExistingBlankAccounts(bool existing)
    {
        var original = existing ? await CreateExistingAccountAsync(null, null) : null;
        if (original is not null)
        {
            original.FirstName = original.LastName = null;
            var originalPerson = Assert.Single(_db.Persons);
            originalPerson.FirstName = originalPerson.LastName = null;
            original.UserName = "historical@example.org";
            Assert.True((await _users.UpdateAsync(original)).Succeeded);
        }
        const string fullName = "\u6e2c\u8a66\u59d3\u540d";
        SetProof(AuthenticatedProof(fullName) with
        {
            Profile = new AssuredProfile { DisplayName = fullName, EmployeeId = "ORG-104",
                AssuredFields = [AssuredProfileField.DisplayName, AssuredProfileField.EmployeeId] }
        });

        Assert.True((await _login.AuthenticateAsync(original?.UserName ?? "submitted-alias", "password")).IsSuccess);

        _db.ChangeTracker.Clear();
        var user = Assert.Single(_db.Users);
        var person = Assert.Single(_db.Persons);
        Assert.Equal(fullName, user.Nickname);
        Assert.Equal(fullName, person.Nickname);
        Assert.Equal("ORG-104", user.EmployeeId);
        Assert.Equal("ORG-104", person.EmployeeId);
        Assert.Equal(original?.Id ?? user.Id, user.Id);
        Assert.Equal(existing ? "historical@example.org" : "source-account", user.UserName);
        Assert.Equal(person.Id, user.PersonId);
        Assert.Null(user.FirstName);
        Assert.Null(user.LastName);
        Assert.Null(person.FirstName);
        Assert.Null(person.LastName);
        Assert.Equal("opaque-subject", Assert.Single(_db.UserLogins).ProviderKey);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticateAsync_ShouldPreserveNativeOrganizationIdsAndNames_WhenPopulatedOrManuallyCleared(bool clear)
    {
        var user = await CreateExistingAccountAsync("Account name", "Person name");
        var person = Assert.Single(_db.Persons);
        var policy = CreateNativeFields(_db);
        policy.ApplyManual(user, new Dictionary<NativeField, string?>
        { [NativeField.Nickname] = clear ? "" : "Account name", [NativeField.EmployeeId] = clear ? "" : "LOCAL-104" });
        policy.ApplyManual(person, new Dictionary<NativeField, string?>
        { [NativeField.Nickname] = clear ? "" : "Person name", [NativeField.EmployeeId] = clear ? "" : "PERSON-104" });
        await _db.SaveChangesAsync();
        SetProof(AuthenticatedProof("Changed source") with
        {
            Profile = new AssuredProfile { DisplayName = "Changed source", EmployeeId = "ORG-104",
                AssuredFields = [AssuredProfileField.DisplayName, AssuredProfileField.EmployeeId] }
        });

        Assert.True((await _login.AuthenticateAsync(user.UserName!, "password")).IsSuccess);

        _db.ChangeTracker.Clear();
        user = Assert.Single(_db.Users);
        person = Assert.Single(_db.Persons);
        Assert.Equal(clear ? "" : "Account name", user.Nickname);
        Assert.Equal(clear ? "" : "Person name", person.Nickname);
        Assert.Equal(clear ? null : "LOCAL-104", user.EmployeeId);
        Assert.Equal(clear ? null : "PERSON-104", person.EmployeeId);
    }

    [Fact]
    public async Task AuthenticateAsync_ShouldProtectAllNativeBlanksUntilUnlockAndStillRefreshProfile()
    {
        var policy = CreateNativeFields(_db);
        SetProof(AuthenticatedProof("Display") with
        {
            Profile = new AssuredProfile
            {
                DisplayName = "Display", GivenName = "Given", Surname = "Family",
                Department = "Department", Title = "Title", EmployeeId = "employee",
                AssuredFields = Enum.GetValues<AssuredProfileField>()
            }
        });
        Assert.True((await _login.AuthenticateAsync("alias", "password")).IsSuccess);
        var user = Assert.Single(_db.Users);
        var person = Assert.Single(_db.Persons);
        var fields = Enum.GetValues<NativeField>();
        Assert.All((await policy.GetTargetAsync(NativeFieldTarget.User, user.Id)).Fields, f => Assert.True(f.Attributed));
        Assert.All((await policy.GetTargetAsync(NativeFieldTarget.Person, person.Id)).Fields, f => Assert.True(f.Attributed));
        policy.ApplyManual(user, fields.ToDictionary(f => f, _ => (string?)""));
        policy.ApplyManual(person, fields.ToDictionary(f => f, _ => (string?)""));
        await _db.SaveChangesAsync();
        for (var i = 0; i < 2; i++)
            Assert.True((await _login.AuthenticateAsync(user.UserName!, "password")).IsSuccess);
        foreach (var target in new[] { (NativeFieldTarget.User, user.Id), (NativeFieldTarget.Person, person.Id) })
        {
            var state = await policy.GetTargetAsync(target.Item1, target.Id);
            Assert.All(state.Fields, f => { Assert.True(f.ManualFixed); Assert.True(string.IsNullOrEmpty(f.Value)); });
            await policy.UpdateAsync(target.Item1, target.Id, NativeField.Nickname,
                new() { Operation = NativeFieldOperation.Unlock, ExpectedRevision = state.Revision });
        }
        Assert.True((await _login.AuthenticateAsync(user.UserName!, "password")).IsSuccess);
        Assert.Equal("Display", user.Nickname);
        Assert.Equal("Display", person.Nickname);
        Assert.Equal("", user.FirstName);
        Assert.Null(person.EmployeeId);
        _profile.Verify(p => p.RefreshAfterLoginAsync(It.IsAny<ApplicationUser>(), "example.provider", "opaque-subject",
            It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    [Fact]
    public async Task AuthenticateAsync_ShouldPauseOnlyNativeFieldsAndRetainExactLinkAndProfileRefresh()
    {
        var user = await CreateExistingAccountAsync(null, null);
        var policy = CreateNativeFields(_db);
        await policy.SetPausedAsync(NativeFieldSource.Proof, new(true, Guid.Empty));
        SetProof(AuthenticatedProof("Paused display"));
        Assert.True((await _login.AuthenticateAsync(user.UserName!, "password")).IsSuccess);
        Assert.Null(user.Nickname);
        Assert.Null(Assert.Single(_db.Persons).Nickname);
        var link = Assert.Single(_db.UserLogins);
        Assert.Equal(user.Id, link.UserId);
        Assert.Equal("opaque-subject", link.ProviderKey);
        Assert.Equal("Paused display", link.ProviderDisplayName);
        _profile.Verify(p => p.RefreshAfterLoginAsync(user, "example.provider", "opaque-subject",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task AuthenticateAsync_ShouldIgnoreOversizedNativeValuesAndRefreshProfile_WhenNativeSourceUnavailable(
        bool nativeEnabled, bool paused)
    {
        var user = await CreateExistingAccountAsync(null, null);
        var person = Assert.Single(_db.Persons);
        var policy = CreateNativeFields(_db);
        _nativeFieldOptions.ProofEnabled = nativeEnabled;
        if (paused) await policy.SetPausedAsync(NativeFieldSource.Proof, new(true, Guid.Empty));
        var userBefore = await policy.GetTargetAsync(NativeFieldTarget.User, user.Id);
        var personBefore = await policy.GetTargetAsync(NativeFieldTarget.Person, person.Id);
        var proof = AuthenticatedProof("Unapplied display") with
        {
            Profile = new AssuredProfile
            {
                DisplayName = "Unapplied display", GivenName = new string('x', 101), Surname = "Unapplied family",
                Department = "Unapplied department", Title = "Unapplied title", EmployeeId = "unapplied-employee",
                AssuredFields = Enum.GetValues<AssuredProfileField>()
            }
        };
        Assert.True(proof.TryValidate(out _));
        SetProof(proof);

        Assert.True((await _login.AuthenticateAsync(user.UserName!, "password")).IsSuccess);

        _db.ChangeTracker.Clear();
        var userAfter = await policy.GetTargetAsync(NativeFieldTarget.User, user.Id);
        var personAfter = await policy.GetTargetAsync(NativeFieldTarget.Person, person.Id);
        Assert.Equal(userBefore.Fields, userAfter.Fields);
        Assert.Equal(userBefore.Revision, userAfter.Revision);
        Assert.Equal(personBefore.Fields, personAfter.Fields);
        Assert.Equal(personBefore.Revision, personAfter.Revision);
        _profile.Verify(service => service.RefreshAfterLoginAsync(It.Is<ApplicationUser>(value => value.Id == user.Id),
            "example.provider", "opaque-subject", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProvisionAsync_ShouldKeepSharedPersonOwnerAndPreventBrowserBypass()
    {
        var first = await CreateExistingAccountAsync("first", "person");
        var person = Assert.Single(_db.Persons);
        var second = new ApplicationUser { UserName = "second", PersonId = person.Id };
        Assert.True((await _users.CreateAsync(second)).Succeeded);
        Assert.True((await _users.AddLoginAsync(second, new UserLoginInfo("example.provider", "second", "second"))).Succeeded);
        var policy = CreateNativeFields(_db);
        var state = await policy.GetTargetAsync(NativeFieldTarget.Person, person.Id);
        await policy.UpdateAsync(NativeFieldTarget.Person, person.Id, NativeField.Nickname, new()
        {
            Operation = NativeFieldOperation.AssignSource, ExpectedRevision = state.Revision, AllowOverwrite = true,
            SourceIdentity = new(NativeFieldSource.Proof, first.Id, "example.provider", "opaque-subject")
        });
        await _jit.ProvisionExternalUserAsync(new ExternalAuthResult
        {
            Provider = "example.provider", ProviderKey = "second", AssuredCanonicalAccount = "second",
            DisplayName = "competing", Department = "competing department"
        });
        Assert.Equal("person", person.Nickname);
        Assert.Null(person.Department); // Shared Person's unassigned fields cannot be claimed on later login.
        Assert.Equal("competing", second.Nickname);
        policy.ApplyManual(second, new Dictionary<NativeField, string?> { [NativeField.FirstName] = "" });
        await _db.SaveChangesAsync();
        Assert.True((await _users.AddLoginAsync(second, new UserLoginInfo("Google", "browser", "Google"))).Succeeded);
        await _jit.ProvisionExternalUserAsync(new ExternalAuthResult
        {
            Provider = "Google", ProviderKey = "browser", FirstName = "browser name", Department = "browser department"
        });
        Assert.Equal("", second.FirstName);
        Assert.Equal("competing department", second.Department);
        Assert.Equal("PersonGiven", person.FirstName);
        Assert.Null(person.Department);
    }

    public ProofProvisioningTests()
    {
        _users = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(_db),
            Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()], [], new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), null!, NullLogger<UserManager<ApplicationUser>>.Instance);
        var externalOptions = Options.Create(new ExternalLoginOptions { AutoProvisionDefaultRole = null });
        _jit = new JitProvisioningService(_users,
            _db,
            externalOptions,
            CreateNativeFields(_db));
        var migration = new Mock<ICredentialMigrationStateStore>();
        migration.Setup(store => store.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CredentialMigrationRecord?)null);
        _profile.Setup(service => service.RefreshAfterLoginAsync(It.IsAny<ApplicationUser>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _login = new LoginService(_users, Mock.Of<ISecurityPolicyService>(), _jit, _db,
            NullLogger<LoginService>.Instance, externalOptions, proofProvider: _proof.Object,
            credentialMigrationStateStore: migration.Object,
            providerProofOptions: Options.Create(new ProviderProofOptions
            { Enabled = true, TrustedProviderNamespace = "example.provider" }), providerProfileService: _profile.Object);
    }

    public void Dispose()
    {
        _users.Dispose();
        _db.Dispose();
    }

    [Theory]
    [InlineData("account/123")]
    [InlineData("maximum")]
    public async Task ProvisionAsync_ShouldRetainOpaqueTupleAndResolveRepeatLogin(string subject)
    {
        if (subject == "maximum") subject = new string('x', 256);
        var auth = new ExternalAuthResult { Provider = "example.provider", ProviderKey = subject };
        var first = await _jit.ProvisionExternalUserAsync(auth);
        Assert.True(first.UserName!.Length <= 256);
        Assert.DoesNotContain('/', first.UserName);
        var link = Assert.Single(await _users.GetLoginsAsync(first));
        Assert.Equal(subject, link.ProviderKey);
        Assert.Equal("example.provider", link.LoginProvider);
        var repeated = await _jit.ProvisionExternalUserAsync(auth);
        Assert.Equal(first.Id, repeated.Id);
        Assert.Single(_db.Users);
        Assert.Single(_db.Persons);
    }

    [Fact]
    public async Task AuthenticateAsync_ShouldFillNewUserAndPersonNicknameWithoutSplittingDisplayName()
    {
        SetProof(AuthenticatedProof("Display Name With Spaces"));

        var result = await _login.AuthenticateAsync("submitted-alias", "password");

        Assert.True(result.IsSuccess);
        _db.ChangeTracker.Clear();
        var user = Assert.Single(_db.Users);
        var person = Assert.Single(_db.Persons);
        Assert.Equal("source-account", user.UserName);
        Assert.Equal(person.Id, user.PersonId);
        Assert.Equal("Display Name With Spaces", user.Nickname);
        Assert.Equal(user.Nickname, person.Nickname);
        Assert.Null(user.FirstName);
        Assert.Null(user.LastName);
        Assert.Null(person.FirstName);
        Assert.Null(person.LastName);
        var link = Assert.Single(await _users.GetLoginsAsync(user));
        Assert.Equal("example.provider", link.LoginProvider);
        Assert.Equal("opaque-subject", link.ProviderKey);
        Assert.Equal(user.Nickname, link.ProviderDisplayName);
        _profile.Verify(service => service.RefreshAfterLoginAsync(It.Is<ApplicationUser>(value => value.Id == user.Id),
            "example.provider", "opaque-subject", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(" ", "\t")]
    [InlineData("Local account name", null)]
    [InlineData(null, "Local person name")]
    [InlineData("Local account name", "Local person name")]
    public async Task AuthenticateAsync_ShouldFillEachBlankNicknameAndPreserveOriginalIdentityOnRepeatProof(
        string? userNickname, string? personNickname)
    {
        var original = await CreateExistingAccountAsync(userNickname, personNickname);
        var userId = original.Id;
        var personId = original.PersonId;
        var username = original.UserName;
        var stamp = original.SecurityStamp;
        SetProof(AuthenticatedProof("First Display"));

        Assert.True((await _login.AuthenticateAsync(username!, "password")).IsSuccess);
        SetProof(AuthenticatedProof("Changed Upstream Display"));
        Assert.True((await _login.AuthenticateAsync(username!, "password")).IsSuccess);

        _db.ChangeTracker.Clear();
        var user = Assert.Single(_db.Users);
        var person = Assert.Single(_db.Persons);
        Assert.Equal(string.IsNullOrWhiteSpace(userNickname) ? "First Display" : userNickname, user.Nickname);
        Assert.Equal(string.IsNullOrWhiteSpace(personNickname) ? "First Display" : personNickname, person.Nickname);
        Assert.Equal(userId, user.Id);
        Assert.Equal(personId, person.Id);
        Assert.Equal(personId, user.PersonId);
        Assert.Equal(username, user.UserName);
        Assert.Equal(stamp, user.SecurityStamp);
        Assert.Equal("UserGiven", user.FirstName);
        Assert.Equal("UserFamily", user.LastName);
        Assert.Equal("PersonGiven", person.FirstName);
        Assert.Equal("PersonFamily", person.LastName);
        Assert.Equal("{\"fixture\":true}", person.ProviderProfilesJson);
        var link = Assert.Single(await _users.GetLoginsAsync(user));
        Assert.Equal("example.provider", link.LoginProvider);
        Assert.Equal("opaque-subject", link.ProviderKey);
        Assert.Equal("Changed Upstream Display", link.ProviderDisplayName);
        _profile.Verify(service => service.RefreshAfterLoginAsync(It.Is<ApplicationUser>(value => value.Id == userId),
            "example.provider", "opaque-subject", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" \t ", false)]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData(" \t ", true)]
    public async Task AuthenticateAsync_ShouldIgnoreBlankDisplayName(string? displayName, bool existing)
    {
        var original = existing ? await CreateExistingAccountAsync(null, null) : null;
        SetProof(AuthenticatedProof(displayName));

        Assert.True((await _login.AuthenticateAsync(original?.UserName ?? "source-account", "password")).IsSuccess);

        _db.ChangeTracker.Clear();
        Assert.Null(Assert.Single(_db.Users).Nickname);
        Assert.Null(Assert.Single(_db.Persons).Nickname);
    }

    [Theory]
    [InlineData(100, false)]
    [InlineData(101, false)]
    [InlineData(100, true)]
    [InlineData(101, true)]
    public async Task AuthenticateAsync_ShouldRespectPersonNicknameLimitWithoutFailingLogin(int length, bool existing)
    {
        var original = existing ? await CreateExistingAccountAsync(null, null) : null;
        var displayName = new string('x', length);
        SetProof(AuthenticatedProof(displayName));

        Assert.True((await _login.AuthenticateAsync(original?.UserName ?? "source-account", "password")).IsSuccess);

        _db.ChangeTracker.Clear();
        Assert.Equal(displayName, Assert.Single(_db.Users).Nickname);
        Assert.Equal(length <= 100 ? displayName : null, Assert.Single(_db.Persons).Nickname);
        Assert.Equal(displayName, Assert.Single(_db.UserLogins).ProviderDisplayName);
    }

    [Theory]
    [InlineData("unassured")]
    [InlineData("failed")]
    [InlineData("malformed")]
    [InlineData("wrong-subject")]
    [InlineData("wrong-namespace")]
    [InlineData("required-action")]
    public async Task AuthenticateAsync_ShouldNotFillNicknamesWhenProofIsRejected(string condition)
    {
        var original = await CreateExistingAccountAsync(null, null);
        var proof = AuthenticatedProof("Rejected Display");
        SetProof(condition switch
        {
            "unassured" => proof with { Profile = new AssuredProfile { DisplayName = "Rejected Display" } },
            "failed" => new ProofResult { Outcome = ProofOutcome.InvalidCredentials },
            "malformed" => proof with { ContractVersion = "2.0" },
            "wrong-subject" => proof with { StableSubject = "other-subject" },
            "wrong-namespace" => proof with { ProviderNamespace = "other.provider" },
            _ => proof with { RequiredActions = [ProofRequiredAction.EmailOtp] }
        });

        Assert.False((await _login.AuthenticateAsync(original.UserName!, "password")).IsSuccess);

        _db.ChangeTracker.Clear();
        Assert.Null(Assert.Single(_db.Users).Nickname);
        Assert.Null(Assert.Single(_db.Persons).Nickname);
        Assert.Equal("Old Display", Assert.Single(_db.UserLogins).ProviderDisplayName);
        _profile.Verify(service => service.RefreshAfterLoginAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AuthenticateAsync_ShouldNotFillNicknamesWhenLinkedPersonIsIneligible()
    {
        var original = await CreateExistingAccountAsync(null, null);
        Assert.Single(_db.Persons).Status = PersonStatus.Suspended;
        await _db.SaveChangesAsync();
        SetProof(AuthenticatedProof("Rejected Display"));

        Assert.False((await _login.AuthenticateAsync(original.UserName!, "password")).IsSuccess);

        _db.ChangeTracker.Clear();
        Assert.Null(Assert.Single(_db.Users).Nickname);
        Assert.Null(Assert.Single(_db.Persons).Nickname);
    }

    private async Task<ApplicationUser> CreateExistingAccountAsync(string? userNickname, string? personNickname)
    {
        var user = await _jit.ProvisionExternalUserAsync(new ExternalAuthResult
        {
            Provider = "example.provider", ProviderKey = "opaque-subject",
            AssuredCanonicalAccount = "historical-account", DisplayName = "Old Display"
        });
        user.Nickname = userNickname;
        user.FirstName = "UserGiven";
        user.LastName = "UserFamily";
        var person = Assert.Single(_db.Persons);
        person.Nickname = personNickname;
        person.FirstName = "PersonGiven";
        person.LastName = "PersonFamily";
        person.ProviderProfilesJson = "{\"fixture\":true}";
        await _db.SaveChangesAsync();
        return user;
    }

    private static ProofResult AuthenticatedProof(string? displayName) => new()
    {
        Outcome = ProofOutcome.Authenticated, ProviderNamespace = "example.provider",
        StableSubject = "opaque-subject", CanonicalAccount = "source-account",
        Assurance = new ProofAssurance { StableSubjectAssured = true, CanonicalAccountAssured = true },
        Profile = new AssuredProfile { DisplayName = displayName, AssuredFields = [AssuredProfileField.DisplayName] }
    };

    private void SetProof(ProofResult proof) => _proof
        .Setup(provider => provider.ProveAsync(It.IsAny<ProofRequest>(), "password", It.IsAny<CancellationToken>()))
        .ReturnsAsync(proof);
    private global::Infrastructure.Services.NativeFieldPolicyService CreateNativeFields(Core.Application.IApplicationDbContext context) =>
        new(context,
            Microsoft.Extensions.Options.Options.Create(_nativeFieldOptions),
            Microsoft.Extensions.Options.Options.Create(new global::Infrastructure.Options.ProviderProofOptions { Enabled = true, TrustedProviderNamespace = "example.provider" }),
            Microsoft.Extensions.Options.Options.Create(new global::Infrastructure.Options.DirectoryIntegrationOptions()));
}
