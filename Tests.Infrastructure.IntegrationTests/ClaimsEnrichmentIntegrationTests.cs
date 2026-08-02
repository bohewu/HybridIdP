using System.Security.Claims;
using System.Security.Cryptography;
using Core.Application;
using Core.Application.Security;
using Core.Domain; // Added for ApplicationUser
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Web.IdP.Services;
using Xunit;

namespace Tests.Infrastructure.IntegrationTests;

public class ClaimsEnrichmentIntegrationTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly TokenService _tokenService;
    private readonly ClaimsEnrichmentService _claimsEnricher;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly Mock<SignInManager<ApplicationUser>> _mockSignInManager;
    private readonly Mock<IApiResourceService> _mockApiResourceService;
    private readonly Mock<IAuditService> _mockAuditService;
    private readonly Mock<IOpenIddictApplicationManager> _mockAppManager;
    private readonly Mock<ILogger<TokenService>> _mockTokenLogger;
    private readonly Mock<ILogger<ClaimsEnrichmentService>> _mockClaimsLogger;

    public ClaimsEnrichmentIntegrationTests()
    {
        // 1. Setup InMemory Database
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);

        // 2. Setup Identity Managers (User/Role) with Real Stores
        var userStore = new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(_db);
        var roleStore = new RoleStore<ApplicationRole, ApplicationDbContext, Guid>(_db);

        var optionsAccessor = Options.Create(new IdentityOptions());
        var passwordHasher = new PasswordHasher<ApplicationUser>();
        var userValidators = new List<IUserValidator<ApplicationUser>> { new UserValidator<ApplicationUser>() };
        var passwordValidators = new List<IPasswordValidator<ApplicationUser>> { new PasswordValidator<ApplicationUser>() };
        var keyNormalizer = new UpperInvariantLookupNormalizer();
        var errors = new IdentityErrorDescriber();
        var services = new Mock<IServiceProvider>();
        var logger = new Mock<ILogger<UserManager<ApplicationUser>>>();

        _userManager = new UserManager<ApplicationUser>(
            userStore, optionsAccessor, passwordHasher, userValidators, passwordValidators, keyNormalizer, errors, services.Object, logger.Object);

        _roleManager = new RoleManager<ApplicationRole>(
            roleStore, new List<IRoleValidator<ApplicationRole>>(), new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), new Mock<ILogger<RoleManager<ApplicationRole>>>().Object);

        // 3. Mock SignInManager (simpler than instantiating real one)
        _mockSignInManager = new Mock<SignInManager<ApplicationUser>>(
            _userManager,
            new Mock<IHttpContextAccessor>().Object,
            new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>().Object,
            optionsAccessor,
            new Mock<ILogger<SignInManager<ApplicationUser>>>().Object,
            null,
            null);
        
        // Setup successful sign-in by default
        _mockSignInManager.Setup(x => x.CheckPasswordSignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);
        _mockSignInManager.Setup(x => x.CanSignInAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(true);
        
        // 4. Setup other mocks
        _mockApiResourceService = new Mock<IApiResourceService>();
        _mockApiResourceService.Setup(x => x.GetAudiencesByScopesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<string>());

        _mockAuditService = new Mock<IAuditService>();
        _mockAppManager = new Mock<IOpenIddictApplicationManager>();
        
        // Grant permissions for password flow
        _mockAppManager.Setup(x => x.FindByClientIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new object());
        _mockAppManager.Setup(x => x.GetPermissionsAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(System.Collections.Immutable.ImmutableArray.Create(OpenIddictConstants.Permissions.GrantTypes.Password));

        _mockTokenLogger = new Mock<ILogger<TokenService>>();
        _mockClaimsLogger = new Mock<ILogger<ClaimsEnrichmentService>>();
        var securityPolicyService = new Mock<ISecurityPolicyService>();
        securityPolicyService
            .Setup(service => service.GetCurrentPolicyAsync())
            .ReturnsAsync(new SecurityPolicy());

        // 5. Instantiate Real Services
        _claimsEnricher = new ClaimsEnrichmentService(
            _userManager,
            _roleManager,
            _db,
            _mockClaimsLogger.Object);

        _tokenService = new TokenService(
            _userManager,
            _mockSignInManager.Object,
            _roleManager,
            _mockApiResourceService.Object,
            _mockAuditService.Object,
            securityPolicyService.Object,
            _db,
            _mockAppManager.Object,
            _mockTokenLogger.Object,
            _claimsEnricher);
    }

    public void Dispose()
    {
        _db.Dispose();
        _userManager.Dispose();
        _roleManager.Dispose();
    }

    [Fact]
    public async Task HandleTokenRequestAsync_ShouldIncludePersonClaims_WhenRequested()
    {
        // Arrange
        // 1. Seed User and Person
        var userId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            UserName = "testuser",
            NormalizedUserName = "TESTUSER",
            Email = "test@example.com",
            EmailConfirmed = true,
            PersonId = personId, // Link check
            SecurityStamp = Guid.NewGuid().ToString() // Required for sign in checks sometimes
        };
        var person = new Person
        {
            Id = personId,
            FirstName = "Test", // The value expected in the claim
            LastName = "User",
            Email = "test@example.com"
        };
        // Link navigation from Person side if needed or just add both
        user.Person = person;

        await _userManager.CreateAsync(user, "P@ssword1");
        // _db.Users.Add(user);
        // _db.Persons.Add(person); // Cascade or manual add. Add manually to be safe.
        // Wait, if I add user and user.Person is set, EF Core adds person too.
        // await _db.SaveChangesAsync();

        // 2. Seed Claims Configuration (replicating ScopeSeeder)
        // Create "test_person_name" user claim definition
        var claimDef = new ClaimDefinition
        {
            Name = "test_person_name",
            ClaimType = "test_person_name",
            UserPropertyPath = "Person.FirstName", // This path requires Person entity loaded
            DataType = "String",
            IsStandard = false,
            DisplayName = "Test Person Name" // Required
        };
        _db.Set<ClaimDefinition>().Add(claimDef);
        await _db.SaveChangesAsync();

        // Create "test_scope" scope claim mapping
        // We create a scope claim that maps scope "test_scope" to the user claim we just created
        var scopeClaim = new ScopeClaim
        {
            ScopeName = "test_scope",
            ScopeId = "test_scope_id_placeholder",
            ClaimDefinitionId = claimDef.Id,
            AlwaysInclude = false
        };
        _db.ScopeClaims.Add(scopeClaim);
        await _db.SaveChangesAsync();

        // 3. Create Token Request
        var request = new OpenIddictRequest
        {
            GrantType = OpenIddictConstants.GrantTypes.Password,
            Username = "testuser", // Matches user.UserName
            Password = "P@ssword1", // Ignored by mock
            Scope = "openid test_scope" // Request the test scope
        };

        // Act
        var result = await _tokenService.HandleTokenRequestAsync(request, null);

        // Assert
        var signInResult = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
        Assert.Equal(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, signInResult.AuthenticationScheme);
        
        var principal = signInResult.Principal;
        Assert.NotNull(principal);

        // Verify "test_person_name" claim exists and has correct value
        var claim = principal.FindFirst("test_person_name");
        Assert.NotNull(claim);
        Assert.Equal("Test", claim.Value);
    }

    [Fact]
    public async Task HandleTokenRequestAsync_ShouldNotIncludeClaim_FromSecuritySensitiveSource()
    {
        var userId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            UserName = "sensitive-source-user",
            NormalizedUserName = "SENSITIVE-SOURCE-USER",
            Email = "sensitive-source@example.test",
            EmailConfirmed = true,
            PersonId = personId,
            Person = new Person
            {
                Id = personId,
                FirstName = "Sensitive",
                LastName = "Source",
                Email = "sensitive-source@example.test"
            },
            EmailMfaCode = Convert.ToBase64String(
                RandomNumberGenerator.GetBytes(32))
        };
        var password = $"P@ssword1-{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}";
        await _userManager.CreateAsync(user, password);

        const string claimType = "test_security_sensitive_source";
        const string scopeName = "test_security_sensitive_scope";
        var claimDefinition = new ClaimDefinition
        {
            Name = claimType,
            ClaimType = claimType,
            UserPropertyPath = nameof(ApplicationUser.EmailMfaCode),
            DataType = "String",
            IsStandard = false,
            DisplayName = "Security-sensitive source test"
        };
        _db.Set<ClaimDefinition>().Add(claimDefinition);
        await _db.SaveChangesAsync();
        _db.ScopeClaims.Add(new ScopeClaim
        {
            ScopeName = scopeName,
            ScopeId = "test_security_sensitive_scope_id",
            ClaimDefinitionId = claimDefinition.Id,
            AlwaysInclude = false
        });
        await _db.SaveChangesAsync();

        var request = new OpenIddictRequest
        {
            GrantType = OpenIddictConstants.GrantTypes.Password,
            Username = user.UserName,
            Password = password,
            Scope = $"openid {scopeName}"
        };

        var result = await _tokenService.HandleTokenRequestAsync(request, null);

        var signInResult = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
        Assert.False(
            signInResult.Principal!.HasClaim(claim => claim.Type == claimType),
            "Security-sensitive user properties must not become token claims.");
    }

    [Fact]
    public async Task HandleTokenRequestAsync_ShouldIncludeEveryApprovedClaimSource()
    {
        var personId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "approved-source-user",
            NormalizedUserName = "APPROVED-SOURCE-USER",
            Email = "approved-source@example.test",
            EmailConfirmed = true,
            PhoneNumber = "+886900000000",
            PhoneNumberConfirmed = true,
            FirstName = "Approved",
            MiddleName = "Profile",
            LastName = "Source",
            Nickname = "ApprovedSource",
            Department = "Engineering",
            JobTitle = "Engineer",
            ProfileUrl = "https://example.test/profile",
            PictureUrl = "https://example.test/picture",
            Website = "https://example.test",
            Address = "{}",
            Birthdate = "2000-01-01",
            Gender = "unspecified",
            TimeZone = "Asia/Taipei",
            Locale = "zh-TW",
            EmployeeId = "employee-1",
            PersonId = personId,
            Person = new Person
            {
                Id = personId,
                Email = "person@example.test",
                PhoneNumber = "+886911111111",
                FirstName = "Person",
                MiddleName = "Profile",
                LastName = "Source",
                Nickname = "PersonNickname",
                EmployeeId = "person-employee-1",
                Department = "Research",
                JobTitle = "Researcher",
                ProfileUrl = "https://example.test/person/profile",
                PictureUrl = "https://example.test/person/picture",
                Website = "https://example.test/person",
                Address = "{}",
                Birthdate = "2000-02-02",
                Gender = "unspecified",
                TimeZone = "Asia/Taipei",
                Locale = "zh-TW",
                NationalId = Convert.ToHexString(
                    RandomNumberGenerator.GetBytes(32))
            }
        };
        var password = $"P@ssword1-{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}";
        await _userManager.CreateAsync(user, password);

        const string scopeName = "test_approved_source_scope";
        var definitions = ClaimSourcePropertyPolicy.AllowedPaths
            .Select((path, index) => new ClaimDefinition
            {
                Name = $"test_approved_source_{index}",
                ClaimType = $"test_approved_source_{index}",
                UserPropertyPath = path,
                DataType = "String",
                IsStandard = false,
                DisplayName = $"Approved source {index}"
            })
            .ToList();
        _db.Set<ClaimDefinition>().AddRange(definitions);
        await _db.SaveChangesAsync();
        _db.ScopeClaims.AddRange(definitions.Select(definition => new ScopeClaim
        {
            ScopeName = scopeName,
            ScopeId = "test_approved_source_scope_id",
            ClaimDefinitionId = definition.Id,
            AlwaysInclude = false
        }));
        await _db.SaveChangesAsync();

        var request = new OpenIddictRequest
        {
            GrantType = OpenIddictConstants.GrantTypes.Password,
            Username = user.UserName,
            Password = password,
            Scope = $"openid {scopeName}"
        };

        var result = await _tokenService.HandleTokenRequestAsync(request, null);

        var signInResult = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
        var principal = Assert.IsType<ClaimsPrincipal>(signInResult.Principal);
        foreach (var definition in definitions)
        {
            var claim = principal.FindFirst(definition.ClaimType);
            Assert.True(
                claim is not null,
                $"Approved source '{definition.UserPropertyPath}' must reach the token principal.");
            Assert.Contains(
                OpenIddictConstants.Destinations.AccessToken,
                claim!.GetDestinations());
            Assert.Contains(
                OpenIddictConstants.Destinations.IdentityToken,
                claim.GetDestinations());
        }
    }
}
