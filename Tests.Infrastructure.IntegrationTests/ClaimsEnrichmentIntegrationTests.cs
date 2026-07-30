using System.Security.Claims;
using Core.Application;
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
}
