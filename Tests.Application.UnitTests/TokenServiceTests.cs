using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using System.Collections.Immutable;
using System.Security.Claims;
using System.Threading;
using Core.Application;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Web.IdP.Services;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tests.Application.UnitTests
{
    public class TokenServiceTests
    {
        private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
        private readonly Mock<SignInManager<ApplicationUser>> _mockSignInManager;
        private readonly Mock<RoleManager<ApplicationRole>> _mockRoleManager;
        private readonly Mock<IApiResourceService> _mockApiResourceService;
        private readonly Mock<IAuditService> _mockAuditService;
        private readonly Mock<ISecurityPolicyService> _mockSecurityPolicyService;
        private readonly Mock<IApplicationDbContext> _mockDbContext;
        private readonly Mock<IOpenIddictApplicationManager> _mockApplicationManager;
        private readonly Mock<ILogger<TokenService>> _mockLogger;
        private readonly Mock<IClaimsEnrichmentService> _mockClaimsEnricher;
        private readonly TokenService _service;

        public TokenServiceTests()
        {
            var userStore = new Mock<IUserStore<ApplicationUser>>();
            _mockUserManager = new Mock<UserManager<ApplicationUser>>(userStore.Object, null, null, null, null, null, null, null, null);

            var contextAccessor = new Mock<IHttpContextAccessor>();
            var userClaimsPrincipalFactory = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
            _mockSignInManager = new Mock<SignInManager<ApplicationUser>>(
                _mockUserManager.Object, 
                contextAccessor.Object, 
                userClaimsPrincipalFactory.Object, 
                null, null, null, null);

            var roleStore = new Mock<IRoleStore<ApplicationRole>>();
            _mockRoleManager = new Mock<RoleManager<ApplicationRole>>(roleStore.Object, null, null, null, null);

            _mockApiResourceService = new Mock<IApiResourceService>();
            _mockAuditService = new Mock<IAuditService>();
            _mockSecurityPolicyService = new Mock<ISecurityPolicyService>();
            _mockDbContext = new Mock<IApplicationDbContext>();
            _mockApplicationManager = new Mock<IOpenIddictApplicationManager>();
            _mockLogger = new Mock<ILogger<TokenService>>();
            _mockClaimsEnricher = new Mock<IClaimsEnrichmentService>();
            
            // Default setup for claims enricher to avoid null task exceptions
            _mockClaimsEnricher.Setup(x => x.AddScopeMappedClaimsAsync(It.IsAny<ClaimsIdentity>(), It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _mockClaimsEnricher.Setup(x => x.AddPermissionClaimsAsync(It.IsAny<ClaimsIdentity>(), It.IsAny<ApplicationUser>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _mockClaimsEnricher.Setup(x => x.AddAppSpecificRolesAsync(It.IsAny<ClaimsIdentity>(), It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _mockSecurityPolicyService
                .Setup(x => x.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy());

            _service = new TokenService(
                _mockUserManager.Object,
                _mockSignInManager.Object,
                _mockRoleManager.Object,
                _mockApiResourceService.Object,
                _mockAuditService.Object,
                _mockSecurityPolicyService.Object,
                _mockDbContext.Object,
                _mockApplicationManager.Object,
                _mockLogger.Object,
                _mockClaimsEnricher.Object);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_NullRequest_ThrowsArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _service.HandleTokenRequestAsync(null!, null));
        }

        [Fact]
        public async Task HandleTokenRequestAsync_UnsupportedGrantType_ReturnsForbidResult()
        {
            var request = new OpenIddictRequest
            {
                GrantType = "unsupported_grant_type",
                ClientId = "test-client"
            };

            // Setup valid client to pass permission check
            var clientApp = new object();
            _mockApplicationManager.Setup(m => m.FindByClientIdAsync("test-client", default))
                .ReturnsAsync(clientApp);
            _mockApplicationManager.Setup(m => m.GetPermissionsAsync(clientApp, default))
                .ReturnsAsync(ImmutableArray.Create<string>()); // No specific permissions needed to fail grant type check later

            var result = await _service.HandleTokenRequestAsync(request, null);

            var forbidResult = Assert.IsType<ForbidResult>(result);
            Assert.Equal(Errors.UnsupportedGrantType, forbidResult.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_ClientCredentials_ReturnsSignInResult()
        {
            // Arrange
            var request = CreateRequest(GrantTypes.ClientCredentials, clientId: "service-client", scope: "api:read");
            _mockApiResourceService.Setup(s => s.GetAudiencesByScopesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new List<string> { "api1" });

            // Setup ApplicationManager for grant permission validation
            var clientApp = new object();
            _mockApplicationManager.Setup(m => m.FindByClientIdAsync("service-client", default))
                .ReturnsAsync(clientApp); // Return non-null client
            _mockApplicationManager.Setup(m => m.GetPermissionsAsync(clientApp, default))
                .ReturnsAsync(new List<string> { OpenIddictConstants.Permissions.GrantTypes.ClientCredentials }.ToImmutableArray());
            
            _mockApplicationManager.Setup(m => m.GetClientIdAsync(clientApp, default)).ReturnsAsync("service-client");
            _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(clientApp, default)).ReturnsAsync("Service Client");

            // Act
            var result = await _service.HandleTokenRequestAsync(request, null);

            // Assert
            var signInResult = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
            Assert.Equal(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, signInResult.AuthenticationScheme);
            Assert.NotNull(signInResult.Principal);
            Assert.True(signInResult.Principal.HasClaim(Claims.Subject, "service-client"));
        }

        [Fact]
        public async Task HandleTokenRequestAsync_Password_ValidCredentials_ReturnsSignInResult()
        {
            // Arrange
            var request = CreateRequest(GrantTypes.Password, username: "user", password: "password", scope: "openid");
            var userId = Guid.NewGuid();
            var user = new ApplicationUser { Id = userId, UserName = "user", Email = "user@test.com" };
            
            _mockUserManager.Setup(m => m.FindByNameAsync("user")).ReturnsAsync(user);
            _mockUserManager.Setup(m => m.GetUserIdAsync(user)).ReturnsAsync(user.Id.ToString());
            _mockUserManager.Setup(m => m.GetEmailAsync(user)).ReturnsAsync(user.Email);
            _mockUserManager.Setup(m => m.GetUserNameAsync(user)).ReturnsAsync(user.UserName);
            _mockUserManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(new List<string>());
            
            // Correctly mock CheckPasswordAsync instead of SignInManager
            _mockUserManager.Setup(m => m.CheckPasswordAsync(user, "password")).ReturnsAsync(true);
            _mockSignInManager.Setup(m => m.CanSignInAsync(user)).ReturnsAsync(true);

            _mockApiResourceService.Setup(s => s.GetAudiencesByScopesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new List<string>());

            // Setup empty ScopeClaims for this test
            var emptyScopeClaims = new List<Core.Domain.Entities.ScopeClaim>().AsQueryable();
            var mockScopeClaimsDbSet = new Mock<Microsoft.EntityFrameworkCore.DbSet<Core.Domain.Entities.ScopeClaim>>();
            mockScopeClaimsDbSet.As<IQueryable<Core.Domain.Entities.ScopeClaim>>()
                .Setup(m => m.Provider).Returns(new TestAsyncQueryProvider<Core.Domain.Entities.ScopeClaim>(emptyScopeClaims.Provider));
            mockScopeClaimsDbSet.As<IQueryable<Core.Domain.Entities.ScopeClaim>>()
                .Setup(m => m.Expression).Returns(emptyScopeClaims.Expression);
            mockScopeClaimsDbSet.As<IQueryable<Core.Domain.Entities.ScopeClaim>>()
                .Setup(m => m.ElementType).Returns(emptyScopeClaims.ElementType);
            mockScopeClaimsDbSet.As<IQueryable<Core.Domain.Entities.ScopeClaim>>()
                .Setup(m => m.GetEnumerator()).Returns(emptyScopeClaims.GetEnumerator());
            _mockDbContext.Setup(c => c.ScopeClaims).Returns(mockScopeClaimsDbSet.Object);

            // Setup ApplicationManager for grant permission validation
            _mockApplicationManager.Setup(m => m.FindByClientIdAsync("test-client", default))
                .ReturnsAsync(new object()); // Return non-null client
            _mockApplicationManager.Setup(m => m.GetPermissionsAsync(It.IsAny<object>(), default))
                .ReturnsAsync(new List<string> { OpenIddictConstants.Permissions.GrantTypes.Password }.ToImmutableArray());

            SetupMockUsers(user);

            // Act
            var result = await _service.HandleTokenRequestAsync(request, null);

            // Assert
            var signInResult = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
            Assert.NotNull(signInResult.Principal);
            Assert.True(signInResult.Principal.HasClaim(Claims.Subject, userId.ToString()));
            _mockUserManager.Verify(
                manager => manager.ResetAccessFailedCountAsync(user),
                Times.Once);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_Password_InvalidUser_ReturnsForbidResult()
        {
            // Arrange
            var request = CreateRequest(GrantTypes.Password, username: "unknown", password: "password");
            _mockUserManager.Setup(m => m.FindByNameAsync("unknown")).ReturnsAsync((ApplicationUser?)null);

            // Setup ApplicationManager for grant permission validation
            _mockApplicationManager.Setup(m => m.FindByClientIdAsync("test-client", default))
                .ReturnsAsync(new object()); // Return non-null client
            _mockApplicationManager.Setup(m => m.GetPermissionsAsync(It.IsAny<object>(), default))
                .ReturnsAsync(new List<string> { OpenIddictConstants.Permissions.GrantTypes.Password }.ToImmutableArray());

            SetupMockUsers();

            // Act
            var result = await _service.HandleTokenRequestAsync(request, null);

            // Assert
            AssertPasswordGrantRejected(result);
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(true, true, false)]
        [InlineData(true, false, true)]
        public async Task HandleTokenRequestAsync_Password_RestrictedAccount_ReturnsInvalidGrant(
            bool isActive,
            bool isDeleted,
            bool isLockedOut)
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "password-user",
                IsActive = isActive,
                IsDeleted = isDeleted
            };
            SetupPasswordGrant(user, isLockedOut: isLockedOut);

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(
                    GrantTypes.Password,
                    username: user.UserName,
                    password: "valid-password"),
                null);

            AssertPasswordGrantRejected(result);
        }

        [Theory]
        [InlineData(PersonStatus.Suspended, null, null, false)]
        [InlineData(PersonStatus.Active, 1, null, false)]
        [InlineData(PersonStatus.Active, null, -1, false)]
        [InlineData(PersonStatus.Active, null, null, true)]
        public async Task HandleTokenRequestAsync_Password_IneligiblePerson_ReturnsInvalidGrant(
            PersonStatus status,
            int? startDateOffsetDays,
            int? endDateOffsetDays,
            bool isDeleted)
        {
            var personId = Guid.NewGuid();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "password-user",
                IsActive = true,
                PersonId = personId
            };
            SetupPasswordGrant(user);
            SetupMockPersons(new Person
            {
                Id = personId,
                Status = status,
                StartDate = startDateOffsetDays.HasValue
                    ? DateTime.UtcNow.Date.AddDays(startDateOffsetDays.Value)
                    : null,
                EndDate = endDateOffsetDays.HasValue
                    ? DateTime.UtcNow.Date.AddDays(endDateOffsetDays.Value)
                    : null,
                IsDeleted = isDeleted
            });

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(
                    GrantTypes.Password,
                    username: user.UserName,
                    password: "valid-password"),
                null);

            AssertPasswordGrantRejected(result);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_Password_MissingLinkedPerson_ReturnsInvalidGrant()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "password-user",
                IsActive = true,
                PersonId = Guid.NewGuid()
            };
            SetupPasswordGrant(user);
            SetupMockPersons();

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(
                    GrantTypes.Password,
                    username: user.UserName,
                    password: "valid-password"),
                null);

            AssertPasswordGrantRejected(result);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public async Task HandleTokenRequestAsync_Password_MfaEnabled_ReturnsInvalidGrant(
            bool twoFactorEnabled,
            bool emailMfaEnabled)
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "password-user",
                IsActive = true,
                TwoFactorEnabled = twoFactorEnabled,
                EmailMfaEnabled = emailMfaEnabled
            };
            SetupPasswordGrant(user);

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(
                    GrantTypes.Password,
                    username: user.UserName,
                    password: "valid-password"),
                null);

            AssertPasswordGrantRejected(result);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_Password_MandatoryMfaGraceExpired_ReturnsInvalidGrant()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "password-user",
                IsActive = true,
                MfaRequirementNotifiedAt = DateTime.UtcNow.AddDays(-4)
            };
            SetupPasswordGrant(user);
            SetupMockUserCredentials();
            SetupMandatoryMfaPolicy(gracePeriodDays: 3);

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(
                    GrantTypes.Password,
                    username: user.UserName,
                    password: "valid-password"),
                null);

            AssertPasswordGrantRejected(result);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_Password_MandatoryMfaGraceActive_ReturnsSignInResult()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "password-user",
                IsActive = true,
                MfaRequirementNotifiedAt = DateTime.UtcNow.AddDays(-1)
            };
            SetupPasswordGrant(user);
            SetupMockUserCredentials();
            SetupMandatoryMfaPolicy(gracePeriodDays: 3);

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(
                    GrantTypes.Password,
                    username: user.UserName,
                    password: "valid-password"),
                null);

            Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_Password_MandatoryMfaFirstUse_StartsGracePeriod()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "password-user",
                IsActive = true
            };
            SetupPasswordGrant(user);
            SetupMockUserCredentials();
            SetupMandatoryMfaPolicy(gracePeriodDays: 3);
            _mockUserManager
                .Setup(manager => manager.UpdateAsync(user))
                .ReturnsAsync(IdentityResult.Success);

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(
                    GrantTypes.Password,
                    username: user.UserName,
                    password: "valid-password"),
                null);

            Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
            Assert.NotNull(user.MfaRequirementNotifiedAt);
            _mockUserManager.Verify(manager => manager.UpdateAsync(user), Times.Once);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_Password_MandatoryMfaNotificationPersistenceFails_ReturnsInvalidGrant()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "password-user",
                IsActive = true
            };
            SetupPasswordGrant(user);
            SetupMockUserCredentials();
            SetupMandatoryMfaPolicy(gracePeriodDays: 3);
            _mockUserManager
                .Setup(manager => manager.UpdateAsync(user))
                .ReturnsAsync(IdentityResult.Failed(new IdentityError
                {
                    Code = "ConcurrencyFailure",
                    Description = "The account was modified."
                }));

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(
                    GrantTypes.Password,
                    username: user.UserName,
                    password: "valid-password"),
                null);

            AssertPasswordGrantRejected(result);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_Password_MandatoryMfaWithPasskey_ReturnsSignInResult()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "password-user",
                IsActive = true,
                MfaRequirementNotifiedAt = DateTime.UtcNow.AddDays(-30)
            };
            SetupPasswordGrant(user);
            SetupMockUserCredentials(new UserCredential { UserId = user.Id });
            SetupMandatoryMfaPolicy(gracePeriodDays: 3);

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(
                    GrantTypes.Password,
                    username: user.UserName,
                    password: "valid-password"),
                null);

            Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_Password_InvalidPassword_AppliesConfiguredLockout()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "password-user",
                IsActive = true
            };
            SetupPasswordGrant(user, passwordIsValid: false);
            _mockSecurityPolicyService
                .Setup(service => service.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy
                {
                    MaxFailedAccessAttempts = 3,
                    LockoutDurationMinutes = 15
                });
            _mockUserManager
                .Setup(manager => manager.AccessFailedAsync(user))
                .ReturnsAsync(IdentityResult.Success);
            _mockUserManager
                .Setup(manager => manager.GetAccessFailedCountAsync(user))
                .ReturnsAsync(3);
            _mockUserManager
                .Setup(manager => manager.SetLockoutEndDateAsync(
                    user,
                    It.IsAny<DateTimeOffset?>()))
                .ReturnsAsync(IdentityResult.Success);

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(
                    GrantTypes.Password,
                    username: user.UserName,
                    password: "valid-password"),
                null);

            AssertPasswordGrantRejected(result);
            _mockUserManager.Verify(manager => manager.AccessFailedAsync(user), Times.Once);
            _mockUserManager.Verify(
                manager => manager.SetLockoutEndDateAsync(user, It.IsAny<DateTimeOffset?>()),
                Times.Once);
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(true, true, false)]
        [InlineData(true, false, true)]
        public async Task HandleTokenRequestAsync_RefreshToken_RestrictedAccount_ReturnsInvalidGrant(
            bool isActive,
            bool isDeleted,
            bool isLockedOut)
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "refresh-user",
                IsActive = isActive,
                IsDeleted = isDeleted
            };
            var principal = SetupRefreshGrant(user, isLockedOut);

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(GrantTypes.RefreshToken, refreshToken: "opaque-refresh-token"),
                principal);

            AssertInvalidGrant(result);
        }

        [Theory]
        [InlineData(PersonStatus.Suspended, null, null, false)]
        [InlineData(PersonStatus.Active, 1, null, false)]
        [InlineData(PersonStatus.Active, null, -1, false)]
        [InlineData(PersonStatus.Active, null, null, true)]
        public async Task HandleTokenRequestAsync_RefreshToken_IneligiblePerson_ReturnsInvalidGrant(
            PersonStatus status,
            int? startDateOffsetDays,
            int? endDateOffsetDays,
            bool isDeleted)
        {
            var personId = Guid.NewGuid();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "refresh-user",
                IsActive = true,
                PersonId = personId
            };
            var person = new Person
            {
                Id = personId,
                Status = status,
                StartDate = startDateOffsetDays.HasValue
                    ? DateTime.UtcNow.Date.AddDays(startDateOffsetDays.Value)
                    : null,
                EndDate = endDateOffsetDays.HasValue
                    ? DateTime.UtcNow.Date.AddDays(endDateOffsetDays.Value)
                    : null,
                IsDeleted = isDeleted
            };
            var principal = SetupRefreshGrant(user);
            SetupMockPersons(person);

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(GrantTypes.RefreshToken, refreshToken: "opaque-refresh-token"),
                principal);

            AssertInvalidGrant(result);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_RefreshToken_MissingLinkedPerson_ReturnsInvalidGrant()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "refresh-user",
                IsActive = true,
                PersonId = Guid.NewGuid()
            };
            var principal = SetupRefreshGrant(user);
            SetupMockPersons();

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(GrantTypes.RefreshToken, refreshToken: "opaque-refresh-token"),
                principal);

            AssertInvalidGrant(result);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_RefreshToken_EligibleLinkedUser_ReturnsSignInResult()
        {
            var personId = Guid.NewGuid();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "refresh-user",
                Email = "refresh-user@test.local",
                IsActive = true,
                PersonId = personId
            };
            var principal = SetupRefreshGrant(user);
            SetupMockPersons(new Person
            {
                Id = personId,
                Status = PersonStatus.Active
            });

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(GrantTypes.RefreshToken, refreshToken: "opaque-refresh-token"),
                principal);

            var signInResult = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
            Assert.Equal(
                user.Id.ToString(),
                signInResult.Principal!.GetClaim(Claims.Subject));
        }

        [Fact]
        public async Task HandleTokenRequestAsync_AuthorizationCode_MissingPermission_ReturnsForbidResult()
        {
            // Arrange
            var request = CreateRequest(GrantTypes.AuthorizationCode, clientId: "test-client", code: "auth_code");

            // Setup ApplicationManager for grant permission validation - return empty permissions
            _mockApplicationManager.Setup(m => m.FindByClientIdAsync("test-client", default))
                .ReturnsAsync(new object()); // Return non-null client
            _mockApplicationManager.Setup(m => m.GetPermissionsAsync(It.IsAny<object>(), default))
                .ReturnsAsync(ImmutableArray<string>.Empty);

            // Act
            var result = await _service.HandleTokenRequestAsync(request, null);

            // Assert
            var forbidResult = Assert.IsType<ForbidResult>(result);
            Assert.Contains(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, forbidResult.AuthenticationSchemes);
        }
        // Helper to create mocked OpenIddictRequest
        private OpenIddictRequest CreateRequest(string grantType, string clientId = "test-client", string? scope = null, string? code = null, string? refreshToken = null, string? username = null, string? password = null)
        {
             // OpenIddictRequest is partially internal/complex to instantiate directly with properties in tests sometimes,
             // but we can set public properties.
             // Typically we can rely on property initializers if they are settable.
             // If not, we might need reflection or specialized OpenIddict test helpers, but standard properties should be settable.
             return new OpenIddictRequest
             {
                 GrantType = grantType,
                 ClientId = clientId,
                 Scope = scope,
                 Code = code,
                 RefreshToken = refreshToken,
                 Username = username,
                 Password = password
             };
        }

        private ClaimsPrincipal SetupRefreshGrant(ApplicationUser user, bool isLockedOut = false)
        {
            var clientApp = new object();
            _mockApplicationManager
                .Setup(m => m.FindByClientIdAsync("test-client", It.IsAny<CancellationToken>()))
                .ReturnsAsync(clientApp);
            _mockApplicationManager
                .Setup(m => m.GetPermissionsAsync(clientApp, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ImmutableArray.Create(OpenIddictConstants.Permissions.GrantTypes.RefreshToken));

            _mockUserManager.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
            _mockUserManager.Setup(m => m.IsLockedOutAsync(user)).ReturnsAsync(isLockedOut);
            _mockUserManager.Setup(m => m.GetUserIdAsync(user)).ReturnsAsync(user.Id.ToString());
            _mockUserManager.Setup(m => m.GetEmailAsync(user)).ReturnsAsync(user.Email);
            _mockUserManager.Setup(m => m.GetUserNameAsync(user)).ReturnsAsync(user.UserName);
            _mockUserManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync([]);
            _mockSignInManager.Setup(m => m.CanSignInAsync(user)).ReturnsAsync(true);

            return new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(Claims.Subject, user.Id.ToString())],
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));
        }

        private void SetupPasswordGrant(
            ApplicationUser user,
            bool isLockedOut = false,
            bool passwordIsValid = true)
        {
            var clientApp = new object();
            _mockApplicationManager
                .Setup(m => m.FindByClientIdAsync("test-client", It.IsAny<CancellationToken>()))
                .ReturnsAsync(clientApp);
            _mockApplicationManager
                .Setup(m => m.GetPermissionsAsync(clientApp, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ImmutableArray.Create(OpenIddictConstants.Permissions.GrantTypes.Password));

            _mockUserManager.Setup(m => m.FindByNameAsync(user.UserName!)).ReturnsAsync(user);
            _mockUserManager.Setup(m => m.IsLockedOutAsync(user)).ReturnsAsync(isLockedOut);
            _mockUserManager
                .Setup(m => m.CheckPasswordAsync(user, "valid-password"))
                .ReturnsAsync(passwordIsValid);
            _mockUserManager
                .Setup(m => m.ResetAccessFailedCountAsync(user))
                .ReturnsAsync(IdentityResult.Success);
            _mockUserManager.Setup(m => m.GetUserIdAsync(user)).ReturnsAsync(user.Id.ToString());
            _mockUserManager.Setup(m => m.GetEmailAsync(user)).ReturnsAsync(user.Email);
            _mockUserManager.Setup(m => m.GetUserNameAsync(user)).ReturnsAsync(user.UserName);
            _mockUserManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync([]);
            _mockSignInManager.Setup(m => m.CanSignInAsync(user)).ReturnsAsync(true);
        }

        private void SetupMandatoryMfaPolicy(int gracePeriodDays)
        {
            _mockSecurityPolicyService
                .Setup(service => service.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy
                {
                    EnforceMandatoryMfaEnrollment = true,
                    MfaEnforcementGracePeriodDays = gracePeriodDays
                });
        }

        private static void AssertInvalidGrant(IActionResult result)
        {
            var forbidResult = Assert.IsType<ForbidResult>(result);
            Assert.Equal(
                Errors.InvalidGrant,
                forbidResult.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
        }

        private static void AssertPasswordGrantRejected(IActionResult result)
        {
            AssertInvalidGrant(result);
            var forbidResult = Assert.IsType<ForbidResult>(result);
            Assert.Equal(
                "The username/password couple is invalid.",
                forbidResult.Properties!.Items[
                    OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription]);
        }

        private void SetupMockPersons(params Person[] persons)
        {
            var personsQueryable = persons.AsQueryable();
            var mockSet = new Mock<DbSet<Person>>();
            mockSet.As<IAsyncEnumerable<Person>>()
                .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
                .Returns(new TestAsyncEnumerator<Person>(personsQueryable.GetEnumerator()));
            mockSet.As<IQueryable<Person>>()
                .Setup(m => m.Provider)
                .Returns(new TestAsyncQueryProvider<Person>(personsQueryable.Provider));
            mockSet.As<IQueryable<Person>>()
                .Setup(m => m.Expression)
                .Returns(personsQueryable.Expression);
            mockSet.As<IQueryable<Person>>()
                .Setup(m => m.ElementType)
                .Returns(personsQueryable.ElementType);
            mockSet.As<IQueryable<Person>>()
                .Setup(m => m.GetEnumerator())
                .Returns(personsQueryable.GetEnumerator());

            _mockDbContext.Setup(c => c.Persons).Returns(mockSet.Object);
        }

        private void SetupMockUserCredentials(params UserCredential[] credentials)
        {
            var credentialsQueryable = credentials.AsQueryable();
            var mockSet = new Mock<DbSet<UserCredential>>();
            mockSet.As<IAsyncEnumerable<UserCredential>>()
                .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
                .Returns(new TestAsyncEnumerator<UserCredential>(credentialsQueryable.GetEnumerator()));
            mockSet.As<IQueryable<UserCredential>>()
                .Setup(m => m.Provider)
                .Returns(new TestAsyncQueryProvider<UserCredential>(credentialsQueryable.Provider));
            mockSet.As<IQueryable<UserCredential>>()
                .Setup(m => m.Expression)
                .Returns(credentialsQueryable.Expression);
            mockSet.As<IQueryable<UserCredential>>()
                .Setup(m => m.ElementType)
                .Returns(credentialsQueryable.ElementType);
            mockSet.As<IQueryable<UserCredential>>()
                .Setup(m => m.GetEnumerator())
                .Returns(credentialsQueryable.GetEnumerator());

            _mockDbContext.Setup(c => c.UserCredentials).Returns(mockSet.Object);
        }

        private void SetupMockUsers(params ApplicationUser[] users)
        {
            var usersQueryable = users.AsQueryable();
            var mockSet = new Mock<DbSet<ApplicationUser>>();
            mockSet.As<IAsyncEnumerable<ApplicationUser>>()
                .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
                .Returns(new TestAsyncEnumerator<ApplicationUser>(usersQueryable.GetEnumerator()));
            mockSet.As<IQueryable<ApplicationUser>>()
                .Setup(m => m.Provider)
                .Returns(new TestAsyncQueryProvider<ApplicationUser>(usersQueryable.Provider));
            mockSet.As<IQueryable<ApplicationUser>>()
                .Setup(m => m.Expression)
                .Returns(usersQueryable.Expression);
            mockSet.As<IQueryable<ApplicationUser>>()
                .Setup(m => m.ElementType)
                .Returns(usersQueryable.ElementType);
            mockSet.As<IQueryable<ApplicationUser>>()
                .Setup(m => m.GetEnumerator())
                .Returns(usersQueryable.GetEnumerator());

            _mockDbContext.Setup(c => c.Users).Returns(mockSet.Object);
        }
    }
}
