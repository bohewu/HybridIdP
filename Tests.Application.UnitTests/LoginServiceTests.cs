using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Threading.Tasks;
using Xunit;
using Core.Application.Utilities;
using Core.Application.Ports;
using Infrastructure.Options;

namespace Tests.Application.UnitTests;

/// <summary>
/// Unit tests for LoginService
/// Phase 18: Added Person lifecycle validation tests
/// </summary>
public class LoginServiceTests
{
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<ISecurityPolicyService> _mockSecurityPolicyService;
    private readonly Mock<ILegacyAuthService> _mockLegacyAuthService;
    private readonly Mock<IJitProvisioningService> _mockJitProvisioningService;
    private readonly Mock<IApplicationDbContext> _mockDbContext;
    private readonly Mock<ICredentialMigrationStateStore> _mockMigrationStateStore;
    private readonly Mock<ILogger<LoginService>> _mockLogger;
    private readonly Mock<IOptions<Core.Application.Options.ExternalLoginOptions>> _mockExternalLoginOptions;
    private readonly LoginService _loginService;
    private readonly List<Person> _persons;

    public LoginServiceTests()
    {
        // Mock UserManager
        var store = new Mock<IUserStore<ApplicationUser>>();
        var options = new Mock<IOptions<IdentityOptions>>();
        var hasher = new Mock<IPasswordHasher<ApplicationUser>>();
        var userValidators = new List<IUserValidator<ApplicationUser>>();
        var passwordValidators = new List<IPasswordValidator<ApplicationUser>>();
        var normalizer = new Mock<ILookupNormalizer>();
        var errors = new IdentityErrorDescriber();
        var services = new Mock<IServiceProvider>();
        var logger = new Mock<ILogger<UserManager<ApplicationUser>>>();
        _mockUserManager = new Mock<UserManager<ApplicationUser>>(
            store.Object, options.Object, hasher.Object, userValidators, passwordValidators, normalizer.Object, errors, services.Object, logger.Object);

        _mockSecurityPolicyService = new Mock<ISecurityPolicyService>();
        _mockLegacyAuthService = new Mock<ILegacyAuthService>();
        _mockJitProvisioningService = new Mock<IJitProvisioningService>();
        _mockLogger = new Mock<ILogger<LoginService>>();
        _mockExternalLoginOptions = new Mock<IOptions<Core.Application.Options.ExternalLoginOptions>>();
        _mockExternalLoginOptions.Setup(x => x.Value).Returns(new Core.Application.Options.ExternalLoginOptions());

        // Setup mock DbContext with Persons DbSet
        _mockDbContext = new Mock<IApplicationDbContext>();
        _persons = new List<Person>();
        var mockPersonsDbSet = CreateMockDbSet(_persons);
        _mockDbContext.Setup(db => db.Persons).Returns(mockPersonsDbSet.Object);
        var mockBindingsDbSet = CreateMockDbSet(new List<ProviderSubjectDirectoryBinding>());
        _mockDbContext.Setup(db => db.ProviderSubjectDirectoryBindings).Returns(mockBindingsDbSet.Object);
        _mockMigrationStateStore = new Mock<ICredentialMigrationStateStore>();
        _mockMigrationStateStore
            .Setup(store => store.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CredentialMigrationRecord?)null);

        _loginService = new LoginService(
            _mockUserManager.Object,
            _mockSecurityPolicyService.Object,
            _mockLegacyAuthService.Object,
            _mockJitProvisioningService.Object,
            _mockDbContext.Object,
            _mockLogger.Object,
            _mockExternalLoginOptions.Object,
            credentialMigrationStateStore: _mockMigrationStateStore.Object
        );
    }

    private static Mock<DbSet<T>> CreateMockDbSet<T>(List<T> data) where T : class
    {
        var queryable = data.AsQueryable();
        var mockSet = new Mock<DbSet<T>>();
        mockSet.As<IQueryable<T>>().Setup(m => m.Provider).Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
        mockSet.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryable.Expression);
        mockSet.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        mockSet.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(queryable.GetEnumerator());
        mockSet.As<IAsyncEnumerable<T>>().Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<T>(data.GetEnumerator()));
        return mockSet;
    }

    private void SetupDefaultPolicy(int maxAttempts = 5)
    {
        _mockSecurityPolicyService.Setup(s => s.GetCurrentPolicyAsync())
            .ReturnsAsync(new SecurityPolicy { MaxFailedAccessAttempts = maxAttempts, LockoutDurationMinutes = 15 });
    }

    #region Existing tests (no Person linked)

    [Fact]
    public async Task AuthenticateAsync_LocalUser_CorrectPassword_ReturnsSuccess()
    {
        // Arrange - user without PersonId
        var user = new ApplicationUser { UserName = "test", PersonId = null };
        _mockUserManager.Setup(um => um.FindByEmailAsync("test")).ReturnsAsync(user);
        _mockUserManager.Setup(um => um.IsLockedOutAsync(user)).ReturnsAsync(false);
        _mockUserManager.Setup(um => um.CheckPasswordAsync(user, "password")).ReturnsAsync(true);
        SetupDefaultPolicy();

        // Act
        var result = await _loginService.AuthenticateAsync("test", "password");

        // Assert
        Assert.Equal(LoginStatus.Success, result.Status);
        Assert.Equal(user, result.User);
        _mockUserManager.Verify(um => um.ResetAccessFailedCountAsync(user), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_LocalUser_ExpiredPassword_ReturnsPasswordChangeRequired()
    {
        var user = new ApplicationUser
        {
            UserName = "expired-password",
            LastPasswordChangeDate = DateTime.UtcNow.AddDays(-31)
        };
        _mockUserManager.Setup(um => um.FindByEmailAsync(user.UserName)).ReturnsAsync(user);
        _mockUserManager.Setup(um => um.IsLockedOutAsync(user)).ReturnsAsync(false);
        _mockUserManager.Setup(um => um.CheckPasswordAsync(user, "password")).ReturnsAsync(true);
        _mockSecurityPolicyService.Setup(service => service.GetCurrentPolicyAsync())
            .ReturnsAsync(new SecurityPolicy { PasswordExpirationDays = 30 });

        var result = await _loginService.AuthenticateAsync(user.UserName, "password");

        Assert.Equal(LoginStatus.PasswordChangeRequired, result.Status);
        Assert.Same(user, result.User);
    }

    [Fact]
    public async Task AuthenticateAsync_LocalUser_MustChangePassword_ReturnsPasswordChangeRequired()
    {
        var user = new ApplicationUser { UserName = "must-change", RequiresPasswordChange = true };
        _mockUserManager.Setup(um => um.FindByEmailAsync(user.UserName)).ReturnsAsync(user);
        _mockUserManager.Setup(um => um.IsLockedOutAsync(user)).ReturnsAsync(false);
        _mockUserManager.Setup(um => um.CheckPasswordAsync(user, "password")).ReturnsAsync(true);
        SetupDefaultPolicy();

        var result = await _loginService.AuthenticateAsync(user.UserName, "password");

        Assert.Equal(LoginStatus.PasswordChangeRequired, result.Status);
    }

    [Fact]
    public async Task AuthenticateAsync_LocalUser_IncorrectPassword_ReturnsInvalidCredentials()
    {
        // Arrange
        var user = new ApplicationUser { UserName = "test", AccessFailedCount = 1, PersonId = null };
        _mockUserManager.Setup(um => um.FindByEmailAsync("test")).ReturnsAsync(user);
        _mockUserManager.Setup(um => um.IsLockedOutAsync(user)).ReturnsAsync(false);
        _mockUserManager.Setup(um => um.CheckPasswordAsync(user, "wrong")).ReturnsAsync(false);
        SetupDefaultPolicy(maxAttempts: 5);

        // Act
        var result = await _loginService.AuthenticateAsync("test", "wrong");

        // Assert
        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        _mockUserManager.Verify(um => um.AccessFailedAsync(user), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_LocalUser_IncorrectPassword_TriggersLockout_ReturnsLockedOut()
    {
        // Arrange
        var user = new ApplicationUser { UserName = "test", AccessFailedCount = 4, PersonId = null };
        _mockUserManager.Setup(um => um.FindByEmailAsync("test")).ReturnsAsync(user);
        _mockUserManager.Setup(um => um.IsLockedOutAsync(user)).ReturnsAsync(false);
        _mockUserManager.Setup(um => um.CheckPasswordAsync(user, "wrong")).ReturnsAsync(false);
        _mockUserManager.Setup(um => um.GetAccessFailedCountAsync(user)).ReturnsAsync(5);
        SetupDefaultPolicy(maxAttempts: 5);

        // Act
        var result = await _loginService.AuthenticateAsync("test", "wrong");

        // Assert
        Assert.Equal(LoginStatus.LockedOut, result.Status);
        _mockUserManager.Verify(um => um.AccessFailedAsync(user), Times.Once);
        _mockUserManager.Verify(um => um.SetLockoutEndDateAsync(user, It.IsAny<DateTimeOffset>()), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_LocalUser_AlreadyLockedOut_ReturnsLockedOut()
    {
        // Arrange - user without PersonId bypasses Person check
        var user = new ApplicationUser { UserName = "test", PersonId = null };
        _mockUserManager.Setup(um => um.FindByEmailAsync("test")).ReturnsAsync(user);
        _mockUserManager.Setup(um => um.IsLockedOutAsync(user)).ReturnsAsync(true);

        // Act
        var result = await _loginService.AuthenticateAsync("test", "any_password");

        // Assert
        Assert.Equal(LoginStatus.LockedOut, result.Status);
        _mockUserManager.Verify(um => um.CheckPasswordAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AuthenticateAsync_LegacyUser_Success_ReturnsLegacySuccess()
    {
        // Arrange
        var provisionedUser = new ApplicationUser 
        { 
            UserName = "legacy",
            PersonId = null // No Person linked
        };
        _mockUserManager.Setup(um => um.FindByEmailAsync("legacy")).ReturnsAsync((ApplicationUser?)null);
        _mockLegacyAuthService.Setup(las => las.ValidateAsync("legacy", "password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegacyUserDto { IsAuthenticated = true });
        _mockJitProvisioningService.Setup(jps => jps.ProvisionExternalUserAsync(It.IsAny<ExternalAuthResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(provisionedUser);

        // Act
        var result = await _loginService.AuthenticateAsync("legacy", "password");

        // Assert
        Assert.Equal(LoginStatus.LegacySuccess, result.Status);
        Assert.Equal(provisionedUser, result.User);
    }

    [Fact]
    public async Task AuthenticateAsync_NonExistentUser_ReturnsInvalidCredentials()
    {
        // Arrange
        _mockUserManager.Setup(um => um.FindByEmailAsync("nobody")).ReturnsAsync((ApplicationUser?)null);
        _mockLegacyAuthService.Setup(las => las.ValidateAsync("nobody", "password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegacyUserDto { IsAuthenticated = false });

        // Act
        var result = await _loginService.AuthenticateAsync("nobody", "password");

        // Assert
        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
    }

    [Fact]
    public async Task AuthenticateAsync_FinalizedMigrationWhenNewMigrationDisabled_UsesDirectoryNotLocalPassword()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "completed", IsActive = true };
        _mockUserManager.Setup(manager => manager.FindByEmailAsync("completed")).ReturnsAsync(user);
        _mockUserManager.Setup(manager => manager.IsLockedOutAsync(user)).ReturnsAsync(false);
        var state = new Mock<ICredentialMigrationStateStore>(MockBehavior.Strict);
        state.Setup(store => store.FindAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialMigrationRecord(
                user.Id,
                new DirectoryObjectBinding("provider", "subject", Guid.NewGuid()),
                CredentialMigrationState.LocalFinalized));
        var stage2 = new Mock<IStage2CredentialMigrationService>(MockBehavior.Strict);
        stage2.Setup(service => service.AuthenticateCompletedAsync(user.Id, "directory-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryCredentialResult(DirectoryCredentialOutcome.Authenticated));
        var metadata = new Mock<IProviderMetadataRefreshService>(MockBehavior.Strict);
        metadata.Setup(service => service.RefreshAsync("provider", "subject", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProviderMetadataRefreshOutcome.Refreshed);
        var service = new LoginService(
            _mockUserManager.Object,
            _mockSecurityPolicyService.Object,
            _mockLegacyAuthService.Object,
            _mockJitProvisioningService.Object,
            _mockDbContext.Object,
            _mockLogger.Object,
            _mockExternalLoginOptions.Object,
            directoryIntegrationOptions: Options.Create(new DirectoryIntegrationOptions { Enabled = true, AuthenticationEnabled = true }),
            stage2CredentialMigrationService: stage2.Object,
            credentialMigrationOptions: Options.Create(new CredentialMigrationOptions { Enabled = false }),
            credentialMigrationStateStore: state.Object,
            providerMetadataRefreshService: metadata.Object);

        var result = await service.AuthenticateAsync("completed", "directory-password");

        Assert.Equal(LoginStatus.Success, result.Status);
        _mockUserManager.Verify(manager => manager.CheckPasswordAsync(user, It.IsAny<string>()), Times.Never);
        _mockLegacyAuthService.Verify(service => service.ValidateAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        stage2.VerifyAll();
        metadata.VerifyAll();
    }

    [Fact]
    public async Task AuthenticateAsync_FinalizedMigrationCanonicalAlias_UsesDirectoryWithoutLocalOrLegacyFallback()
    {
        await using var context = new Infrastructure.ApplicationDbContext(
            new DbContextOptionsBuilder<Infrastructure.ApplicationDbContext>()
                .UseInMemoryDatabase($"login-alias-{Guid.NewGuid():N}")
                .Options);
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "provider-subject", IsActive = true };
        var directoryObjectId = Guid.NewGuid();
        context.Users.Add(user);
        context.ProviderSubjectDirectoryBindings.Add(new ProviderSubjectDirectoryBinding(
            user.Id,
            "provider",
            "subject",
            directoryObjectId,
            DateTime.UtcNow,
            "canonical-account"));
        await context.SaveChangesAsync();
        _mockUserManager.Setup(manager => manager.FindByEmailAsync("canonical-account"))
            .ReturnsAsync((ApplicationUser?)null);
        _mockUserManager.Setup(manager => manager.FindByNameAsync("canonical-account"))
            .ReturnsAsync((ApplicationUser?)null);
        _mockUserManager.Setup(manager => manager.IsLockedOutAsync(user)).ReturnsAsync(false);
        var state = new Mock<ICredentialMigrationStateStore>(MockBehavior.Strict);
        state.Setup(store => store.FindAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialMigrationRecord(
                user.Id,
                new DirectoryObjectBinding("provider", "subject", directoryObjectId, "canonical-account"),
                CredentialMigrationState.LocalFinalized));
        var proof = new Mock<IProofProvider>(MockBehavior.Strict);
        var stage2 = new Mock<IStage2CredentialMigrationService>(MockBehavior.Strict);
        stage2.Setup(service => service.AuthenticateCompletedAsync(user.Id, "directory-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryCredentialResult(DirectoryCredentialOutcome.Authenticated));
        var service = new LoginService(
            _mockUserManager.Object,
            _mockSecurityPolicyService.Object,
            _mockLegacyAuthService.Object,
            _mockJitProvisioningService.Object,
            context,
            _mockLogger.Object,
            _mockExternalLoginOptions.Object,
            proofProvider: proof.Object,
            directoryIntegrationOptions: Options.Create(new DirectoryIntegrationOptions { Enabled = true, AuthenticationEnabled = true }),
            stage2CredentialMigrationService: stage2.Object,
            credentialMigrationOptions: Options.Create(new CredentialMigrationOptions { Enabled = false }),
            credentialMigrationStateStore: state.Object);

        var result = await service.AuthenticateAsync("canonical-account", "directory-password");

        Assert.Equal(LoginStatus.Success, result.Status);
        _mockUserManager.Verify(manager => manager.CheckPasswordAsync(user, It.IsAny<string>()), Times.Never);
        _mockLegacyAuthService.Verify(service => service.ValidateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        proof.VerifyNoOtherCalls();
        stage2.VerifyAll();
    }

    [Fact]
    public async Task AuthenticateAsync_Stage1CanonicalAliasRepeat_ReusesBoundUserWithoutLocalPasswordOrJit()
    {
        await using var context = new Infrastructure.ApplicationDbContext(
            new DbContextOptionsBuilder<Infrastructure.ApplicationDbContext>()
                .UseInMemoryDatabase($"stage1-alias-{Guid.NewGuid():N}")
                .Options);
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "opaque-subject", IsActive = true };
        var directoryObjectId = Guid.NewGuid();
        context.Users.Add(user);
        context.ProviderSubjectDirectoryBindings.Add(new ProviderSubjectDirectoryBinding(
            user.Id,
            "provider",
            "opaque-subject",
            directoryObjectId,
            DateTime.UtcNow,
            "canonical-account"));
        await context.SaveChangesAsync();
        _mockUserManager.Setup(manager => manager.FindByEmailAsync("canonical-account"))
            .ReturnsAsync((ApplicationUser?)null);
        _mockUserManager.Setup(manager => manager.FindByNameAsync("canonical-account"))
            .ReturnsAsync((ApplicationUser?)null);
        _mockUserManager.Setup(manager => manager.IsLockedOutAsync(user)).ReturnsAsync(false);
        var state = new Mock<ICredentialMigrationStateStore>(MockBehavior.Strict);
        state.Setup(store => store.FindAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CredentialMigrationRecord?)null);
        var proof = new Mock<IProofProvider>(MockBehavior.Strict);
        proof.Setup(provider => provider.ProveAsync(
                It.Is<ProofRequest>(request => request.AccountName == "canonical-account"),
                "proof-password",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProofResult
            {
                Outcome = ProofOutcome.Authenticated,
                ProviderNamespace = "provider",
                StableSubject = "opaque-subject",
                CanonicalAccount = "canonical-account",
                Assurance = new ProofAssurance { StableSubjectAssured = true, CanonicalAccountAssured = true }
            });
        var lookup = new Mock<IDirectoryIdentityLookup>(MockBehavior.Strict);
        lookup.Setup(service => service.FindManagedIdentityAsync("canonical-account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryLookupResult(
                DirectoryLookupOutcome.Found,
                new ManagedDirectoryIdentity(directoryObjectId, "canonical-account", true, true, false)));
        var jit = new Mock<IJitProvisioningService>(MockBehavior.Strict);
        var metadata = new Mock<IProviderMetadataRefreshService>(MockBehavior.Strict);
        metadata.Setup(service => service.RefreshAsync("provider", "opaque-subject", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProviderMetadataRefreshOutcome.Refreshed);
        var service = new LoginService(
            _mockUserManager.Object,
            _mockSecurityPolicyService.Object,
            _mockLegacyAuthService.Object,
            jit.Object,
            context,
            _mockLogger.Object,
            _mockExternalLoginOptions.Object,
            proofProvider: proof.Object,
            directoryIdentityLookup: lookup.Object,
            stage1BindingRefreshService: new Stage1BindingRefreshService(context),
            directoryIntegrationOptions: Options.Create(new DirectoryIntegrationOptions { Enabled = true }),
            credentialMigrationStateStore: state.Object,
            providerMetadataRefreshService: metadata.Object);

        var result = await service.AuthenticateAsync("canonical-account", "proof-password");

        Assert.Equal(LoginStatus.LegacySuccess, result.Status);
        Assert.Same(user, result.User);
        Assert.Equal(1, await context.Users.CountAsync());
        var binding = await context.ProviderSubjectDirectoryBindings.SingleAsync();
        Assert.Equal(user.Id, binding.LocalAccountId);
        Assert.Equal(directoryObjectId, binding.DirectoryObjectId);
        _mockUserManager.Verify(manager => manager.CheckPasswordAsync(user, It.IsAny<string>()), Times.Never);
        jit.VerifyNoOtherCalls();
        proof.VerifyAll();
        lookup.VerifyAll();
        metadata.VerifyAll();
    }

    [Fact]
    public async Task AuthenticateAsync_Stage1SuccessWithoutDurableBinding_DoesNotRefreshMetadata()
    {
        _mockUserManager.Setup(manager => manager.FindByEmailAsync("account"))
            .ReturnsAsync((ApplicationUser?)null);
        _mockUserManager.Setup(manager => manager.FindByNameAsync("account"))
            .ReturnsAsync((ApplicationUser?)null);
        var provisioned = new ApplicationUser { Id = Guid.NewGuid(), UserName = "account", IsActive = true };
        var proof = new Mock<IProofProvider>(MockBehavior.Strict);
        proof.Setup(provider => provider.ProveAsync(
                It.Is<ProofRequest>(request => request.AccountName == "account"),
                "proof-password",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProofResult
            {
                Outcome = ProofOutcome.Authenticated,
                ProviderNamespace = "provider",
                StableSubject = "subject",
                CanonicalAccount = "account",
                Assurance = new ProofAssurance { StableSubjectAssured = true, CanonicalAccountAssured = true }
            });
        var directory = new Mock<IDirectoryIdentityLookup>(MockBehavior.Strict);
        directory.Setup(service => service.FindManagedIdentityAsync("account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryLookupResult(DirectoryLookupOutcome.Ambiguous));
        var bindingRefresh = new Mock<IStage1BindingRefreshService>(MockBehavior.Strict);
        var metadata = new Mock<IProviderMetadataRefreshService>(MockBehavior.Strict);
        var service = new LoginService(
            _mockUserManager.Object,
            _mockSecurityPolicyService.Object,
            _mockLegacyAuthService.Object,
            Mock.Of<IJitProvisioningService>(jit =>
                jit.ProvisionExternalUserAsync(It.IsAny<ExternalAuthResult>(), It.IsAny<CancellationToken>()) ==
                Task.FromResult(provisioned)),
            _mockDbContext.Object,
            _mockLogger.Object,
            _mockExternalLoginOptions.Object,
            proofProvider: proof.Object,
            directoryIdentityLookup: directory.Object,
            stage1BindingRefreshService: bindingRefresh.Object,
            directoryIntegrationOptions: Options.Create(new DirectoryIntegrationOptions { Enabled = true }),
            credentialMigrationStateStore: _mockMigrationStateStore.Object,
            providerMetadataRefreshService: metadata.Object);

        var result = await service.AuthenticateAsync("account", "proof-password");

        Assert.Equal(LoginStatus.LegacySuccess, result.Status);
        proof.VerifyAll();
        directory.VerifyAll();
        bindingRefresh.VerifyNoOtherCalls();
        metadata.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AuthenticateAsync_CanonicalAliasWithAllSwitchesDisabled_UsesLegacyAuthNotLocalPassword()
    {
        await using var context = new Infrastructure.ApplicationDbContext(
            new DbContextOptionsBuilder<Infrastructure.ApplicationDbContext>()
                .UseInMemoryDatabase($"legacy-alias-{Guid.NewGuid():N}")
                .Options);
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "opaque-subject", IsActive = true };
        context.Users.Add(user);
        context.ProviderSubjectDirectoryBindings.Add(new ProviderSubjectDirectoryBinding(
            user.Id,
            "provider",
            "opaque-subject",
            Guid.NewGuid(),
            DateTime.UtcNow,
            "canonical-account"));
        await context.SaveChangesAsync();
        _mockUserManager.Setup(manager => manager.FindByEmailAsync("canonical-account"))
            .ReturnsAsync((ApplicationUser?)null);
        _mockUserManager.Setup(manager => manager.FindByNameAsync("canonical-account"))
            .ReturnsAsync((ApplicationUser?)null);
        var state = new Mock<ICredentialMigrationStateStore>(MockBehavior.Strict);
        state.Setup(store => store.FindAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CredentialMigrationRecord?)null);
        var legacy = new Mock<ILegacyAuthService>(MockBehavior.Strict);
        legacy.Setup(service => service.ValidateAsync("canonical-account", "legacy-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegacyUserDto { IsAuthenticated = false });
        var jit = new Mock<IJitProvisioningService>(MockBehavior.Strict);
        var service = new LoginService(
            _mockUserManager.Object,
            _mockSecurityPolicyService.Object,
            legacy.Object,
            jit.Object,
            context,
            _mockLogger.Object,
            _mockExternalLoginOptions.Object,
            credentialMigrationStateStore: state.Object);

        var result = await service.AuthenticateAsync("canonical-account", "legacy-password");

        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        _mockUserManager.Verify(manager => manager.CheckPasswordAsync(user, It.IsAny<string>()), Times.Never);
        legacy.VerifyAll();
        jit.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AuthenticateAsync_UnmigratedAccountWhenMigrationEnabled_DeniesLocalPasswordFallback()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "unmigrated", IsActive = true };
        _mockUserManager.Setup(manager => manager.FindByEmailAsync("unmigrated")).ReturnsAsync(user);
        var state = new Mock<ICredentialMigrationStateStore>(MockBehavior.Strict);
        state.Setup(store => store.FindAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CredentialMigrationRecord?)null);
        var service = new LoginService(
            _mockUserManager.Object,
            _mockSecurityPolicyService.Object,
            _mockLegacyAuthService.Object,
            _mockJitProvisioningService.Object,
            _mockDbContext.Object,
            _mockLogger.Object,
            _mockExternalLoginOptions.Object,
            directoryIntegrationOptions: Options.Create(new DirectoryIntegrationOptions { Enabled = true, AuthenticationEnabled = true }),
            credentialMigrationOptions: Options.Create(new CredentialMigrationOptions { Enabled = true }),
            credentialMigrationStateStore: state.Object);

        var result = await service.AuthenticateAsync("unmigrated", "local-password");

        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        _mockUserManager.Verify(manager => manager.CheckPasswordAsync(user, It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AuthenticateAsync_AllSwitchesDisabledWithoutMigrationRecord_PreservesLocalAuthentication()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "baseline", IsActive = true };
        _mockUserManager.Setup(manager => manager.FindByEmailAsync("baseline")).ReturnsAsync(user);
        _mockUserManager.Setup(manager => manager.IsLockedOutAsync(user)).ReturnsAsync(false);
        _mockUserManager.Setup(manager => manager.CheckPasswordAsync(user, "local-password")).ReturnsAsync(true);
        SetupDefaultPolicy();

        var result = await _loginService.AuthenticateAsync("baseline", "local-password");

        Assert.Equal(LoginStatus.Success, result.Status);
        _mockMigrationStateStore.Verify(store => store.FindAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_AllSwitchesDisabledWithIncompleteMigration_DeniesLocalPassword()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "incomplete", IsActive = true };
        _mockUserManager.Setup(manager => manager.FindByEmailAsync("incomplete")).ReturnsAsync(user);
        var state = MigrationState(user, CredentialMigrationState.ProofValidated);
        var service = CreateMigrationAwareService(state.Object);

        var result = await service.AuthenticateAsync("incomplete", "local-password");

        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        _mockUserManager.Verify(manager => manager.CheckPasswordAsync(user, It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AuthenticateAsync_AllSwitchesDisabledWithFinalizedMigration_DeniesLocalPassword()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "finalized", IsActive = true };
        _mockUserManager.Setup(manager => manager.FindByEmailAsync("finalized")).ReturnsAsync(user);
        var state = MigrationState(user, CredentialMigrationState.LocalFinalized);
        var service = CreateMigrationAwareService(state.Object);

        var result = await service.AuthenticateAsync("finalized", "local-password");

        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        _mockUserManager.Verify(manager => manager.CheckPasswordAsync(user, It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AuthenticateAsync_FinalizedDirectoryNormalBindWithRequiredFlag_RoutesRestrictedChange()
    {
        var directoryObjectId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "operator-recovered",
            IsActive = true,
            RequiresPasswordChange = true
        };
        _mockUserManager.Setup(manager => manager.FindByEmailAsync(user.UserName)).ReturnsAsync(user);
        _mockUserManager.Setup(manager => manager.IsLockedOutAsync(user)).ReturnsAsync(false);
        var state = new Mock<ICredentialMigrationStateStore>(MockBehavior.Strict);
        state.Setup(store => store.FindAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialMigrationRecord(
                user.Id,
                new DirectoryObjectBinding("provider", "subject", directoryObjectId),
                CredentialMigrationState.LocalFinalized));
        var stage2 = new Mock<IStage2CredentialMigrationService>(MockBehavior.Strict);
        stage2.Setup(service => service.AuthenticateCompletedAsync(
                user.Id,
                "operator-known-credential",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryCredentialResult(DirectoryCredentialOutcome.Authenticated));
        var metadata = new Mock<IProviderMetadataRefreshService>(MockBehavior.Strict);
        var service = CreateMigrationAwareService(
            state.Object,
            new DirectoryIntegrationOptions { Enabled = true, AuthenticationEnabled = true },
            stage2.Object,
            metadata.Object);

        var result = await service.AuthenticateAsync(user.UserName, "operator-known-credential");

        Assert.Equal(LoginStatus.PasswordChangeRequired, result.Status);
        Assert.Same(user, result.User);
        Assert.Equal(directoryObjectId, result.DirectoryObjectId);
        metadata.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AuthenticateAsync_FinalizedDirectoryRequiredFlagStillChecksLocalEligibility()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "inactive-operator-recovered",
            IsActive = false,
            RequiresPasswordChange = true
        };
        _mockUserManager.Setup(manager => manager.FindByEmailAsync(user.UserName)).ReturnsAsync(user);
        var state = MigrationState(user, CredentialMigrationState.LocalFinalized);
        var stage2 = new Mock<IStage2CredentialMigrationService>(MockBehavior.Strict);
        stage2.Setup(service => service.AuthenticateCompletedAsync(
                user.Id,
                "operator-known-credential",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryCredentialResult(DirectoryCredentialOutcome.Authenticated));
        var service = CreateMigrationAwareService(
            state.Object,
            new DirectoryIntegrationOptions { Enabled = true, AuthenticationEnabled = true },
            stage2.Object,
            Mock.Of<IProviderMetadataRefreshService>());

        var result = await service.AuthenticateAsync(user.UserName, "operator-known-credential");

        Assert.Equal(LoginStatus.UserInactive, result.Status);
        Assert.Null(result.DirectoryObjectId);
    }

    [Fact]
    public async Task AuthenticateAsync_FinalizedMigrationDirectoryFailure_DeniesWithoutLocalFallback()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "directory-failure", IsActive = true };
        _mockUserManager.Setup(manager => manager.FindByEmailAsync("directory-failure")).ReturnsAsync(user);
        var state = MigrationState(user, CredentialMigrationState.LocalFinalized);
        var stage2 = new Mock<IStage2CredentialMigrationService>(MockBehavior.Strict);
        stage2.Setup(service => service.AuthenticateCompletedAsync(user.Id, "directory-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryCredentialResult(DirectoryCredentialOutcome.Unavailable));
        var metadata = new Mock<IProviderMetadataRefreshService>(MockBehavior.Strict);
        var service = CreateMigrationAwareService(
            state.Object,
            new DirectoryIntegrationOptions { Enabled = true, AuthenticationEnabled = true },
            stage2.Object,
            metadata.Object);

        var result = await service.AuthenticateAsync("directory-failure", "directory-password");

        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        _mockUserManager.Verify(manager => manager.CheckPasswordAsync(user, It.IsAny<string>()), Times.Never);
        stage2.VerifyAll();
        metadata.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AuthenticateAsync_FinalizedMigrationLockedUser_DoesNotRefreshMetadata()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "locked", IsActive = true };
        _mockUserManager.Setup(manager => manager.FindByEmailAsync("locked")).ReturnsAsync(user);
        _mockUserManager.Setup(manager => manager.IsLockedOutAsync(user)).ReturnsAsync(true);
        var state = MigrationState(user, CredentialMigrationState.LocalFinalized);
        var stage2 = new Mock<IStage2CredentialMigrationService>(MockBehavior.Strict);
        stage2.Setup(service => service.AuthenticateCompletedAsync(user.Id, "directory-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryCredentialResult(DirectoryCredentialOutcome.Authenticated));
        var metadata = new Mock<IProviderMetadataRefreshService>(MockBehavior.Strict);
        var service = CreateMigrationAwareService(
            state.Object,
            new DirectoryIntegrationOptions { Enabled = true, AuthenticationEnabled = true },
            stage2.Object,
            metadata.Object);

        var result = await service.AuthenticateAsync("locked", "directory-password");

        Assert.Equal(LoginStatus.LockedOut, result.Status);
        stage2.VerifyAll();
        metadata.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AuthenticateAsync_MetadataRefreshDisabled_PreservesSuccessfulDirectoryLogin()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "completed", IsActive = true };
        _mockUserManager.Setup(manager => manager.FindByEmailAsync("completed")).ReturnsAsync(user);
        _mockUserManager.Setup(manager => manager.IsLockedOutAsync(user)).ReturnsAsync(false);
        var state = MigrationState(user, CredentialMigrationState.LocalFinalized);
        var stage2 = new Mock<IStage2CredentialMigrationService>(MockBehavior.Strict);
        stage2.Setup(service => service.AuthenticateCompletedAsync(user.Id, "directory-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryCredentialResult(DirectoryCredentialOutcome.Authenticated));
        var metadata = new Mock<IProviderMetadataRefreshService>(MockBehavior.Strict);
        metadata.Setup(service => service.RefreshAsync("provider", "subject", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProviderMetadataRefreshOutcome.Disabled);
        var service = CreateMigrationAwareService(
            state.Object,
            new DirectoryIntegrationOptions { Enabled = true, AuthenticationEnabled = true },
            stage2.Object,
            metadata.Object);

        var result = await service.AuthenticateAsync("completed", "directory-password");

        Assert.Equal(LoginStatus.Success, result.Status);
        metadata.VerifyAll();
    }

    [Fact]
    public async Task AuthenticateAsync_MetadataRefreshFailure_PreservesSuccessfulDirectoryLogin()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "completed", IsActive = true };
        _mockUserManager.Setup(manager => manager.FindByEmailAsync("completed")).ReturnsAsync(user);
        _mockUserManager.Setup(manager => manager.IsLockedOutAsync(user)).ReturnsAsync(false);
        var state = MigrationState(user, CredentialMigrationState.LocalFinalized);
        var stage2 = new Mock<IStage2CredentialMigrationService>(MockBehavior.Strict);
        stage2.Setup(service => service.AuthenticateCompletedAsync(user.Id, "directory-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryCredentialResult(DirectoryCredentialOutcome.Authenticated));
        var metadata = new Mock<IProviderMetadataRefreshService>(MockBehavior.Strict);
        metadata.Setup(service => service.RefreshAsync("provider", "subject", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("metadata unavailable"));
        var service = CreateMigrationAwareService(
            state.Object,
            new DirectoryIntegrationOptions { Enabled = true, AuthenticationEnabled = true },
            stage2.Object,
            metadata.Object);

        var result = await service.AuthenticateAsync("completed", "directory-password");

        Assert.Equal(LoginStatus.Success, result.Status);
        metadata.VerifyAll();
    }

    [Fact]
    public async Task AuthenticateAsync_MetadataRefreshCallerCancellation_Propagates()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "completed", IsActive = true };
        _mockUserManager.Setup(manager => manager.FindByEmailAsync("completed")).ReturnsAsync(user);
        _mockUserManager.Setup(manager => manager.IsLockedOutAsync(user)).ReturnsAsync(false);
        var state = MigrationState(user, CredentialMigrationState.LocalFinalized);
        var stage2 = new Mock<IStage2CredentialMigrationService>(MockBehavior.Strict);
        stage2.Setup(service => service.AuthenticateCompletedAsync(user.Id, "directory-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryCredentialResult(DirectoryCredentialOutcome.Authenticated));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var metadata = new Mock<IProviderMetadataRefreshService>(MockBehavior.Strict);
        metadata.Setup(service => service.RefreshAsync("provider", "subject", cancellation.Token))
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));
        var service = CreateMigrationAwareService(
            state.Object,
            new DirectoryIntegrationOptions { Enabled = true, AuthenticationEnabled = true },
            stage2.Object,
            metadata.Object);

        var authenticating = service.AuthenticateAsync("completed", "directory-password", cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => authenticating);
        metadata.VerifyAll();
    }

    private Mock<ICredentialMigrationStateStore> MigrationState(
        ApplicationUser user,
        CredentialMigrationState migrationState)
    {
        var state = new Mock<ICredentialMigrationStateStore>(MockBehavior.Strict);
        state.Setup(store => store.FindAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialMigrationRecord(
                user.Id,
                new DirectoryObjectBinding("provider", "subject", Guid.NewGuid()),
                migrationState));
        return state;
    }

    private LoginService CreateMigrationAwareService(
        ICredentialMigrationStateStore stateStore,
        DirectoryIntegrationOptions? directoryOptions = null,
        IStage2CredentialMigrationService? stage2 = null,
        IProviderMetadataRefreshService? metadata = null) =>
        new(
            _mockUserManager.Object,
            _mockSecurityPolicyService.Object,
            _mockLegacyAuthService.Object,
            _mockJitProvisioningService.Object,
            _mockDbContext.Object,
            _mockLogger.Object,
            _mockExternalLoginOptions.Object,
            directoryIntegrationOptions: Options.Create(directoryOptions ?? new DirectoryIntegrationOptions()),
            stage2CredentialMigrationService: stage2,
            credentialMigrationStateStore: stateStore,
            providerMetadataRefreshService: metadata);

    #endregion

    #region Phase 18: Person Lifecycle Validation Tests

    [Fact]
    public async Task AuthenticateAsync_LocalUser_ActivePerson_ReturnsSuccess()
    {
        // Arrange - user linked to an active Person
        var personId = Guid.NewGuid();
        var person = new Person 
        { 
            Id = personId, 
            Status = PersonStatus.Active, 
            IsDeleted = false 
        };
        _persons.Add(person);

        var user = new ApplicationUser { UserName = "active.user", PersonId = personId };
        _mockUserManager.Setup(um => um.FindByEmailAsync("active.user")).ReturnsAsync(user);
        _mockUserManager.Setup(um => um.IsLockedOutAsync(user)).ReturnsAsync(false);
        _mockUserManager.Setup(um => um.CheckPasswordAsync(user, "password")).ReturnsAsync(true);
        SetupDefaultPolicy();

        // Act
        var result = await _loginService.AuthenticateAsync("active.user", "password");

        // Assert
        Assert.Equal(LoginStatus.Success, result.Status);
        Assert.Equal(user, result.User);
    }

    [Theory]
    [InlineData(PersonStatus.Pending, "Person status is Pending")]
    [InlineData(PersonStatus.Suspended, "Person status is Suspended")]
    [InlineData(PersonStatus.Resigned, "Person status is Resigned")]
    [InlineData(PersonStatus.Terminated, "Person status is Terminated")]
    public async Task AuthenticateAsync_LocalUser_InactivePerson_ReturnsPersonInactive(PersonStatus status, string expectedMessage)
    {
        // Arrange - user linked to an inactive Person
        var personId = Guid.NewGuid();
        var person = new Person 
        { 
            Id = personId, 
            Status = status, 
            IsDeleted = false 
        };
        _persons.Add(person);

        var user = new ApplicationUser { UserName = "inactive.user", PersonId = personId };
        _mockUserManager.Setup(um => um.FindByEmailAsync("inactive.user")).ReturnsAsync(user);
        SetupDefaultPolicy();

        // Act
        var result = await _loginService.AuthenticateAsync("inactive.user", "password");

        // Assert
        Assert.Equal(LoginStatus.PersonInactive, result.Status);
        Assert.Contains(expectedMessage, result.Message);
    }

    [Fact]
    public async Task AuthenticateAsync_LocalUser_DeletedPerson_ReturnsPersonInactive()
    {
        // Arrange - user linked to a soft-deleted Person
        var personId = Guid.NewGuid();
        var person = new Person 
        { 
            Id = personId, 
            Status = PersonStatus.Active, 
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow 
        };
        _persons.Add(person);

        var user = new ApplicationUser { UserName = "deleted.user", PersonId = personId };
        _mockUserManager.Setup(um => um.FindByEmailAsync("deleted.user")).ReturnsAsync(user);
        SetupDefaultPolicy();

        // Act
        var result = await _loginService.AuthenticateAsync("deleted.user", "password");

        // Assert
        Assert.Equal(LoginStatus.PersonInactive, result.Status);
        Assert.Contains("deleted", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthenticateAsync_LocalUser_FutureStartDate_ReturnsPersonInactive()
    {
        // Arrange - user linked to a Person with future start date
        var personId = Guid.NewGuid();
        var person = new Person 
        { 
            Id = personId, 
            Status = PersonStatus.Active, 
            IsDeleted = false,
            StartDate = DateTime.UtcNow.AddDays(7) // Starts next week
        };
        _persons.Add(person);

        var user = new ApplicationUser { UserName = "future.user", PersonId = personId };
        _mockUserManager.Setup(um => um.FindByEmailAsync("future.user")).ReturnsAsync(user);
        SetupDefaultPolicy();

        // Act
        var result = await _loginService.AuthenticateAsync("future.user", "password");

        // Assert
        Assert.Equal(LoginStatus.PersonInactive, result.Status);
        Assert.Contains("not started", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthenticateAsync_LocalUser_PastEndDate_ReturnsPersonInactive()
    {
        // Arrange - user linked to a Person with past end date
        var personId = Guid.NewGuid();
        var person = new Person 
        { 
            Id = personId, 
            Status = PersonStatus.Active, 
            IsDeleted = false,
            EndDate = DateTime.UtcNow.AddDays(-1) // Ended yesterday
        };
        _persons.Add(person);

        var user = new ApplicationUser { UserName = "expired.user", PersonId = personId };
        _mockUserManager.Setup(um => um.FindByEmailAsync("expired.user")).ReturnsAsync(user);
        SetupDefaultPolicy();

        // Act
        var result = await _loginService.AuthenticateAsync("expired.user", "password");

        // Assert
        Assert.Equal(LoginStatus.PersonInactive, result.Status);
        Assert.Contains("ended", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthenticateAsync_LocalUser_PersonNotFound_ReturnsPersonInactive()
    {
        // Arrange - user linked to a deleted/non-existent Person
        var personId = Guid.NewGuid();
        // Don't add person to _persons list - simulating deleted or non-existent

        var user = new ApplicationUser { UserName = "orphan.user", PersonId = personId };
        _mockUserManager.Setup(um => um.FindByEmailAsync("orphan.user")).ReturnsAsync(user);
        SetupDefaultPolicy();

        // Act
        var result = await _loginService.AuthenticateAsync("orphan.user", "password");

        // Assert
        Assert.Equal(LoginStatus.PersonInactive, result.Status);
        Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
