using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure;
using Infrastructure.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Tests.Infrastructure.IntegrationTests;

public sealed class PrivilegedTestAdminBootstrapTests : IDisposable
{
    public static TheoryData<string, bool?> DisabledBootstrapCases => new()
    {
        { "Development", null },
        { "Test", null },
        { "Development", false },
        { "Test", false }
    };

    public static TheoryData<string?> BlockedEnvironments => new()
    {
        "Production",
        "Staging",
        "",
        null,
        "Unknown",
        "DevelopmentLocal",
        "development",
        "test"
    };

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;

    public PrivilegedTestAdminBootstrapTests()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(dbOptions);

        var identityOptions = Options.Create(new IdentityOptions());
        _userManager = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(_db),
            identityOptions,
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            [new PasswordValidator<ApplicationUser>()],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new Mock<IServiceProvider>().Object,
            new Mock<ILogger<UserManager<ApplicationUser>>>().Object);

        _roleManager = new RoleManager<ApplicationRole>(
            new RoleStore<ApplicationRole, ApplicationDbContext, Guid>(_db),
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new Mock<ILogger<RoleManager<ApplicationRole>>>().Object);
    }

    [Theory]
    [MemberData(nameof(DisabledBootstrapCases))]
    public async Task SeedAsync_ShouldNotCreatePrivilegedAdmin_WhenSettingIsAbsentOrFalse(
        string environmentName,
        bool? configuredValue)
    {
        await RoleSeeder.SeedAsync(_roleManager);
        var configuration = CreateConfiguration(configuredValue);

        await UserSeeder.SeedAsync(
            _userManager,
            _roleManager,
            _db,
            seedTestUsers: false,
            configuration.GetValue<bool>(PrivilegedTestAdminBootstrapPolicy.ConfigurationKey),
            environmentName);

        var admin = await _userManager.FindByEmailAsync(AuthConstants.DefaultAdmin.Email);
        Assert.Null(admin);
    }

    [Theory]
    [MemberData(nameof(BlockedEnvironments))]
    public async Task SeedAsync_ShouldNotCreatePrivilegedAdmin_WhenEnvironmentIsNotExactlyAllowlisted(
        string? environmentName)
    {
        await RoleSeeder.SeedAsync(_roleManager);
        var configuration = CreateConfiguration(enabled: true);

        await UserSeeder.SeedAsync(
            _userManager,
            _roleManager,
            _db,
            seedTestUsers: false,
            configuration.GetValue<bool>(PrivilegedTestAdminBootstrapPolicy.ConfigurationKey),
            environmentName);

        var admin = await _userManager.FindByEmailAsync(AuthConstants.DefaultAdmin.Email);
        Assert.Null(admin);
    }

    [Fact]
    public async Task SeedAsync_ShouldNotMutateExistingPrivilegedAdmin_WhenEnvironmentIsBlocked()
    {
        await RoleSeeder.SeedAsync(_roleManager);

        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Existing",
            LastName = "Administrator",
            Status = PersonStatus.Suspended,
            StartDate = DateTime.UtcNow.AddDays(5),
            CreatedAt = DateTime.UtcNow.AddYears(-1)
        };
        _db.Persons.Add(person);
        await _db.SaveChangesAsync();

        var lockoutEnd = DateTimeOffset.UtcNow.AddHours(2);
        var emailMfaCodeExpiry = DateTime.UtcNow.AddMinutes(10);
        var admin = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = AuthConstants.DefaultAdmin.Email,
            Email = AuthConstants.DefaultAdmin.Email,
            EmailConfirmed = true,
            PersonId = person.Id,
            Person = person,
            IsActive = false,
            LockoutEnabled = true,
            LockoutEnd = lockoutEnd,
            AccessFailedCount = 4,
            TwoFactorEnabled = true,
            EmailMfaEnabled = true,
            EmailMfaCode = "unchanged-code",
            EmailMfaCodeExpiry = emailMfaCodeExpiry,
            Locale = "en-US"
        };

        var existingPassword = $"A!a1{Guid.NewGuid():N}";
        var createResult = await _userManager.CreateAsync(admin, existingPassword);
        Assert.True(createResult.Succeeded);
        var roleResult = await _userManager.AddToRoleAsync(admin, AuthConstants.Roles.User);
        Assert.True(roleResult.Succeeded);

        var originalPasswordHash = admin.PasswordHash;
        var originalSecurityStamp = admin.SecurityStamp;
        var originalConcurrencyStamp = admin.ConcurrencyStamp;
        Assert.True(await _userManager.CheckPasswordAsync(admin, existingPassword));
        Assert.False(await _userManager.CheckPasswordAsync(admin, AuthConstants.DefaultAdmin.Password));

        var configuration = CreateConfiguration(enabled: true);
        await UserSeeder.SeedAsync(
            _userManager,
            _roleManager,
            _db,
            seedTestUsers: false,
            configuration.GetValue<bool>(PrivilegedTestAdminBootstrapPolicy.ConfigurationKey),
            environmentName: "Production");

        _db.ChangeTracker.Clear();
        var unchangedAdmin = await _userManager.FindByEmailAsync(AuthConstants.DefaultAdmin.Email);
        Assert.NotNull(unchangedAdmin);
        Assert.Equal(originalPasswordHash, unchangedAdmin.PasswordHash);
        Assert.Equal(originalSecurityStamp, unchangedAdmin.SecurityStamp);
        Assert.Equal(originalConcurrencyStamp, unchangedAdmin.ConcurrencyStamp);
        Assert.Equal(person.Id, unchangedAdmin.PersonId);
        Assert.False(unchangedAdmin.IsActive);
        Assert.True(unchangedAdmin.LockoutEnabled);
        Assert.Equal(lockoutEnd, unchangedAdmin.LockoutEnd);
        Assert.Equal(4, unchangedAdmin.AccessFailedCount);
        Assert.True(unchangedAdmin.TwoFactorEnabled);
        Assert.True(unchangedAdmin.EmailMfaEnabled);
        Assert.Equal("unchanged-code", unchangedAdmin.EmailMfaCode);
        Assert.Equal(emailMfaCodeExpiry, unchangedAdmin.EmailMfaCodeExpiry);
        Assert.Equal("en-US", unchangedAdmin.Locale);
        Assert.True(await _userManager.CheckPasswordAsync(unchangedAdmin, existingPassword));
        Assert.False(await _userManager.CheckPasswordAsync(unchangedAdmin, AuthConstants.DefaultAdmin.Password));
        Assert.True(await _userManager.IsInRoleAsync(unchangedAdmin, AuthConstants.Roles.User));
        Assert.False(await _userManager.IsInRoleAsync(unchangedAdmin, AuthConstants.Roles.Admin));

        var unchangedPerson = await _db.Persons.FindAsync(person.Id);
        Assert.NotNull(unchangedPerson);
        Assert.Equal(PersonStatus.Suspended, unchangedPerson.Status);
        Assert.Equal(person.StartDate, unchangedPerson.StartDate);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public async Task SeedAsync_ShouldCreatePrivilegedAdmin_WhenExplicitlyEnabledInAllowedEnvironment(
        string environmentName)
    {
        await RoleSeeder.SeedAsync(_roleManager);
        var configuration = CreateConfiguration(enabled: true);

        await UserSeeder.SeedAsync(
            _userManager,
            _roleManager,
            _db,
            seedTestUsers: false,
            configuration.GetValue<bool>(PrivilegedTestAdminBootstrapPolicy.ConfigurationKey),
            environmentName);

        var admin = await _userManager.FindByEmailAsync(AuthConstants.DefaultAdmin.Email);
        Assert.NotNull(admin);
        Assert.True(await _userManager.CheckPasswordAsync(admin, AuthConstants.DefaultAdmin.Password));
        Assert.True(await _userManager.IsInRoleAsync(admin, AuthConstants.Roles.Admin));
        Assert.NotNull(admin.PersonId);

        var person = await _db.Persons.FindAsync(admin.PersonId);
        Assert.NotNull(person);
        Assert.Equal(PersonStatus.Active, person.Status);
    }

    [Fact]
    public async Task SeedAsync_ShouldSeedOtherTestUsers_WhenPrivilegedBootstrapIsDisabled()
    {
        await RoleSeeder.SeedAsync(_roleManager);
        var configuration = CreateConfiguration(enabled: null);

        await UserSeeder.SeedAsync(
            _userManager,
            _roleManager,
            _db,
            seedTestUsers: true,
            configuration.GetValue<bool>(PrivilegedTestAdminBootstrapPolicy.ConfigurationKey),
            environmentName: "Development");

        var privilegedAdmin = await _userManager.FindByEmailAsync(AuthConstants.DefaultAdmin.Email);
        var ordinaryTestUser = await _userManager.FindByEmailAsync("testuser@hybridauth.local");
        Assert.Null(privilegedAdmin);
        Assert.NotNull(ordinaryTestUser);
        Assert.True(await _userManager.CheckPasswordAsync(ordinaryTestUser, "Test@123"));
    }

    private static IConfiguration CreateConfiguration(bool? enabled)
    {
        var values = new Dictionary<string, string?>();
        if (enabled.HasValue)
        {
            values[PrivilegedTestAdminBootstrapPolicy.ConfigurationKey] = enabled.Value.ToString();
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    public void Dispose()
    {
        _userManager.Dispose();
        _roleManager.Dispose();
        _db.Dispose();
    }
}
