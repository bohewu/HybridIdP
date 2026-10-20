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
    private readonly LoginService _login;

    public ProofProvisioningTests()
    {
        _users = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(_db),
            Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()], [], new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), null!, NullLogger<UserManager<ApplicationUser>>.Instance);
        var externalOptions = Options.Create(new ExternalLoginOptions { AutoProvisionDefaultRole = null });
        _jit = new JitProvisioningService(_users, _db, externalOptions);
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
}
