using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using Core.Application;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Infrastructure.Options;
using Moq;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Web.IdP.Services;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;
using OidcPermissions = OpenIddict.Abstractions.OpenIddictConstants.Permissions;

namespace Tests.Application.UnitTests
{
    public class TokenServiceTests
    {
        [Theory]
        [InlineData(GrantTypes.AuthorizationCode, false)]
        [InlineData(GrantTypes.AuthorizationCode, true)]
        [InlineData(GrantTypes.DeviceCode, false)]
        [InlineData(GrantTypes.DeviceCode, true)]
        public async Task OutstandingCode_ShouldRejectRemovedScopeAndKeepAuthorizedGrant(string grant, bool removed)
        {
            var user = new ApplicationUser { Id = Guid.NewGuid(), IsActive = true, UserName = "code-user" };
            var principal = grant == GrantTypes.DeviceCode ? SetupDeviceCodeGrant(user) : SetupAuthorizationCodeGrant(user);
            principal.SetClaim(Claims.AuthenticationTime, 1700000000L);
            principal.SetScopes(Scopes.OpenId, Scopes.Profile);
            _mockApplicationManager.Setup(manager => manager.GetPermissionsAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(removed ? ImmutableArray.Create(OidcPermissions.Prefixes.GrantType + grant, OidcPermissions.Prefixes.Scope + Scopes.OpenId)
                    : ImmutableArray.Create(OidcPermissions.Prefixes.GrantType + grant, OidcPermissions.Prefixes.Scope + Scopes.OpenId, OidcPermissions.Prefixes.Scope + Scopes.Profile));
            var result = await _service.HandleTokenRequestAsync(CreateRequest(grant), principal);
            if (removed) AssertInvalidGrant(result);
            else
            {
                var issued = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result).Principal;
                var authenticationTime = Assert.Single(issued.FindAll(Claims.AuthenticationTime));
                Assert.Equal("1700000000", authenticationTime.Value);
                Assert.Equal(ClaimValueTypes.Integer64, authenticationTime.ValueType);
            }
        }

        [Fact]
        public async Task OutstandingCode_ShouldRemoveUnscopedHistoricalProfileClaims()
        {
            var user = new ApplicationUser { Id = Guid.NewGuid(), IsActive = true };
            var principal = SetupAuthorizationCodeGrant(user);
            principal.SetScopes(Scopes.OpenId);
            principal.SetClaim(Claims.Name, "historical-name");
            principal.SetClaim(Claims.Email, "historical@example.test");
            _mockApplicationManager.Setup(manager => manager.GetPermissionsAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ImmutableArray.Create(OidcPermissions.GrantTypes.AuthorizationCode, OidcPermissions.Prefixes.Scope + Scopes.OpenId));
            var issued = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(await _service.HandleTokenRequestAsync(CreateRequest(GrantTypes.AuthorizationCode), principal)).Principal!;
            Assert.Null(issued.GetClaim(Claims.Name));
            Assert.Null(issued.GetClaim(Claims.Email));
            Assert.NotNull(issued.GetClaim(Claims.Subject));
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("openid", false)]
        [InlineData("extra", true)]
        public async Task Refresh_ShouldIntersectOriginalGrantWithCurrentPermissions(string? requested, bool invalid)
        {
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "refresh-user", Email = "user@example.test", IsActive = true };
            var principal = SetupRefreshGrant(user);
            principal.SetClaim(Claims.AuthenticationTime, 1700000000L);
            principal.SetScopes("openid", "profile", "email", "removed");
            _mockApplicationManager.Setup(m => m.GetPermissionsAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ImmutableArray.Create(OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                    OpenIddictConstants.Permissions.Prefixes.Scope + "openid", OpenIddictConstants.Permissions.Prefixes.Scope + "email"));
            var request = CreateRequest(GrantTypes.RefreshToken, refreshToken: "dummy");
            request.Scope = requested;
            var result = await _service.HandleTokenRequestAsync(request, principal);
            if (invalid)
                Assert.Equal(Errors.InvalidScope, Assert.IsType<ForbidResult>(result).Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
            else
            {
                var issued = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result).Principal!;
                Assert.Equal(requested == null ? new[] { "email", "openid" } : new[] { "openid" }, issued.GetScopes().Order());
                Assert.Null(issued.GetClaim(Claims.Name));
                Assert.Equal(requested == null ? user.Email : null, issued.GetClaim(Claims.Email));
                var authenticationTime = Assert.Single(issued.FindAll(Claims.AuthenticationTime));
                Assert.Equal("1700000000", authenticationTime.Value);
                Assert.Equal(ClaimValueTypes.Integer64, authenticationTime.ValueType);
            }
        }

        [Theory]
        [InlineData("openid", false, false)]
        [InlineData("openid profile", true, false)]
        [InlineData("openid email", false, true)]
        public async Task Password_ShouldIssueOnlyScopeAuthorizedProfileClaims(string scopes, bool profile, bool email)
        {
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "profile-user", Email = "user@example.test", IsActive = true };
            SetupPasswordGrant(user);
            var result = await _service.HandleTokenRequestAsync(CreateRequest(GrantTypes.Password,
                username: user.UserName, password: "${TEST_FIXTURE_001}", scope: scopes), null);
            var principal = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result).Principal!;
            Assert.Equal(profile ? user.UserName : null, principal.GetClaim(Claims.Name));
            Assert.Equal(profile ? user.UserName : null, principal.GetClaim(Claims.PreferredUsername));
            Assert.Equal(email ? user.Email : null, principal.GetClaim(Claims.Email));
            Assert.Equal(user.Id.ToString(), principal.GetClaim(Claims.Subject));
        }

        [Fact]
        public async Task HandleTokenRequestAsync_ShouldRejectPasswordForClientRequiringMfaWithoutEnrolledFactors()
        {
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "factor-free", IsActive = true };
            SetupPasswordGrant(user);
            _mockApplicationManager.Setup(m => m.GetPropertiesAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateClientProperties(requireMfa: true));
            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(GrantTypes.Password, username: user.UserName, password: "${TEST_FIXTURE_001}"), null);
            AssertInvalidGrant(result);
        }

        [Theory]
        [InlineData(GrantTypes.AuthorizationCode, false, "pwd")]
        [InlineData(GrantTypes.AuthorizationCode, false, "hwk")]
        [InlineData(GrantTypes.AuthorizationCode, true, "hwk")]
        [InlineData(GrantTypes.DeviceCode, false, "pwd")]
        [InlineData(GrantTypes.DeviceCode, false, "hwk")]
        [InlineData(GrantTypes.DeviceCode, true, "hwk")]
        public async Task HandleTokenRequestAsync_ShouldRecheckClientMfaAtRedemptionAndPreserveProof(string grant, bool hasMfa, string primaryAmr)
        {
            var person = new Person { Id = Guid.NewGuid(), Status = PersonStatus.Active };
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "device-or-code-user", IsActive = true, PersonId = person.Id };
            var principal = grant == GrantTypes.DeviceCode ? SetupDeviceCodeGrant(user) : SetupAuthorizationCodeGrant(user);
            SetupMockPersons(person);
            principal.SetClaims("amr", hasMfa ? ImmutableArray.Create(primaryAmr, "mfa") : ImmutableArray.Create(primaryAmr, "user"));
            _mockApplicationManager.Setup(m => m.GetPropertiesAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateClientProperties(requireMfa: true));
            var result = await _service.HandleTokenRequestAsync(CreateRequest(grant, code: "code"), principal);
            if (hasMfa)
                Assert.Contains("mfa", Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result).Principal!.GetClaims("amr"));
            else AssertInvalidGrant(result);
        }

        [Theory]
        [InlineData(GrantTypes.Password)]
        [InlineData(GrantTypes.AuthorizationCode)]
        public async Task HandleTokenRequestAsync_ShouldExcludeGlobalIdpAuthorityFromUserTokens(string grant)
        {
            var person = new Person { Id = Guid.NewGuid(), Status = PersonStatus.Active };
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "admin-user", IsActive = true, PersonId = person.Id };
            ClaimsPrincipal? principal = null;
            if (grant == GrantTypes.Password) SetupPasswordGrant(user);
            else
            {
                principal = SetupAuthorizationCodeGrant(user);
                principal.SetClaim("role", "Admin").SetClaim("permission", "users.delete")
                    .SetClaim("active_role", "Admin");
                SetupMockPersons(person);
            }
            _mockUserManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(["Admin"]);
            SetupMockPersons(person);
            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(grant, username: user.UserName, password: "${TEST_FIXTURE_001}", code: "test-code"), principal);
            var issued = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result).Principal!;
            Assert.DoesNotContain(issued.Claims, c => c.Type is "role" or "permission" or "active_role" || c.Type == ClaimTypes.Role);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_ShouldIssueAdministrativeApprovalBoundToApplicationRecordOnlyForM2m()
        {
            var application = new object();
            _mockApplicationManager.Setup(m => m.FindByClientIdAsync("service-client", It.IsAny<CancellationToken>())).ReturnsAsync(application);
            _mockApplicationManager.Setup(m => m.GetPermissionsAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ImmutableArray.Create(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials));
            _mockApplicationManager.Setup(m => m.GetClientIdAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("service-client");
            _mockApplicationManager.Setup(m => m.GetIdAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("immutable-record");
            _mockApplicationManager.Setup(m => m.GetPropertiesAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync(
                ImmutableDictionary<string, JsonElement>.Empty.Add(global::Infrastructure.Authorization.AdministrativeClientGrant.PermissionsProperty,
                    JsonSerializer.SerializeToElement(new[] { "users.read" })));
            var result = await _service.HandleTokenRequestAsync(CreateRequest(GrantTypes.ClientCredentials, clientId: "service-client", scope: "users.read"), null);
            var principal = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result).Principal!;
            Assert.Equal("immutable-record", principal.GetClaim(global::Infrastructure.Authorization.AdministrativeClientGrant.ApplicationClaim));
        }

    private readonly Mock<global::Web.IdP.Services.ICurrentUserLifecycleEligibility> _lifecycle = new();

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
        private readonly Mock<ICredentialMigrationStateStore> _mockCredentialMigrationStateStore;
        private readonly Mock<IStage2CredentialMigrationService> _mockStage2CredentialMigrationService;
        private readonly Mock<IMigrationIssuanceGuard> _mockMigrationIssuanceGuard;
        private readonly DirectoryIntegrationOptions _directoryIntegrationOptions;
        private readonly CredentialMigrationOptions _credentialMigrationOptions;
        private readonly TokenService _service;

        public TokenServiceTests()
        {
        _lifecycle.Setup(policy => policy.IsEligibleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

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
            _mockCredentialMigrationStateStore = new Mock<ICredentialMigrationStateStore>();
            _mockCredentialMigrationStateStore
                .Setup(store => store.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CredentialMigrationRecord?)null);
            _mockStage2CredentialMigrationService = new Mock<IStage2CredentialMigrationService>();
            _mockMigrationIssuanceGuard = new Mock<IMigrationIssuanceGuard>();
            _mockMigrationIssuanceGuard
                .Setup(guard => guard.CanIssueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            
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
                _lifecycle.Object,
                _mockUserManager.Object,
                _mockSignInManager.Object,
                _mockRoleManager.Object,
                _mockApiResourceService.Object,
                _mockAuditService.Object,
                _mockSecurityPolicyService.Object,
                _mockDbContext.Object,
                _mockApplicationManager.Object,
                _mockLogger.Object,
                _mockClaimsEnricher.Object,
                Options.Create(_directoryIntegrationOptions = new DirectoryIntegrationOptions()),
                Options.Create(_credentialMigrationOptions = new CredentialMigrationOptions()),
                _mockCredentialMigrationStateStore.Object,
                _mockStage2CredentialMigrationService.Object,
                _mockMigrationIssuanceGuard.Object);
        }

        [Theory]
    [InlineData(GrantTypes.Password, false)]
    [InlineData(GrantTypes.Password, true)]
    [InlineData(GrantTypes.AuthorizationCode, false)]
    [InlineData(GrantTypes.AuthorizationCode, true)]
    [InlineData(GrantTypes.RefreshToken, false)]
    [InlineData(GrantTypes.RefreshToken, true)]
    [InlineData(GrantTypes.DeviceCode, false)]
    [InlineData(GrantTypes.DeviceCode, true)]
    public async Task UserGrants_ShouldDeny_WhenLifecycleFailsAtInitialOrFinalCheckpoint(string grant, bool initiallyEligible)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "lifecycle-user", IsActive = true };
        ClaimsPrincipal? principal = null;
        switch (grant)
        {
            case GrantTypes.Password: SetupPasswordGrant(user); break;
            case GrantTypes.AuthorizationCode: principal = SetupAuthorizationCodeGrant(user); break;
            case GrantTypes.RefreshToken: principal = SetupRefreshGrant(user); break;
            case GrantTypes.DeviceCode: principal = SetupDeviceCodeGrant(user); break;
        }
        _lifecycle.SetupSequence(policy => policy.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(initiallyEligible).ReturnsAsync(false);
        var result = await _service.HandleTokenRequestAsync(
            CreateRequest(grant, username: user.UserName, password: "${TEST_FIXTURE_001}", refreshToken: "${TEST_FIXTURE_002}"), principal);
        AssertInvalidGrant(result);
        _lifecycle.Verify(policy => policy.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>()),
            Times.Exactly(initiallyEligible ? 2 : 1));
        _mockUserManager.Verify(manager => manager.UpdateSecurityStampAsync(It.IsAny<ApplicationUser>()), Times.Never);
        _mockStage2CredentialMigrationService.VerifyNoOtherCalls();
        Assert.True(user.IsActive);
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
        public async Task HandleTokenRequestAsync_ClientCredentials_RemainsIsolatedFromMfaPolicies()
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
            _mockApplicationManager
                .Setup(m => m.GetPropertiesAsync(clientApp, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateClientProperties(requireMfa: true));
            _mockSecurityPolicyService
                .Setup(service => service.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy { EnforceMandatoryMfaEnrollment = true });

            // Act
            var result = await _service.HandleTokenRequestAsync(request, null);

            // Assert
            var signInResult = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
            Assert.Equal(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, signInResult.AuthenticationScheme);
            Assert.NotNull(signInResult.Principal);
            Assert.True(signInResult.Principal.HasClaim(Claims.Subject, "service-client"));
            _mockApplicationManager.Verify(
                manager => manager.GetPropertiesAsync(clientApp, It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.False(signInResult.Principal.HasClaim(c => c.Type == global::Infrastructure.Authorization.AdministrativeClientGrant.ApplicationClaim));
        }

        [Fact]
        public async Task HandleTokenRequestAsync_Password_ValidCredentials_ReturnsSignInResult()
        {
            // Arrange
            var request = CreateRequest(GrantTypes.Password, username: "user", password: "${TEST_FIXTURE_003}", scope: "openid");
            var userId = Guid.NewGuid();
            var user = new ApplicationUser { Id = userId, UserName = "user", Email = "user@test.com" };
            
            _mockUserManager.Setup(m => m.FindByNameAsync("user")).ReturnsAsync(user);
            _mockUserManager.Setup(m => m.GetUserIdAsync(user)).ReturnsAsync(user.Id.ToString());
            _mockUserManager.Setup(m => m.GetEmailAsync(user)).ReturnsAsync(user.Email);
            _mockUserManager.Setup(m => m.GetUserNameAsync(user)).ReturnsAsync(user.UserName);
            _mockUserManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(new List<string>());
            
            // Correctly mock CheckPasswordAsync instead of SignInManager
            _mockUserManager.Setup(m => m.CheckPasswordAsync(user, "${TEST_FIXTURE_003}")).ReturnsAsync(true);
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
        public async Task HandleTokenRequestAsync_Password_MustChangeLocalCredential_ReturnsInvalidGrant()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "must-change-user",
                IsActive = true,
                RequiresPasswordChange = true
            };
            SetupPasswordGrant(user);

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(
                    GrantTypes.Password,
                    username: user.UserName,
                    password: "${TEST_FIXTURE_001}"),
                null);

            AssertPasswordGrantRejected(result);
            _mockUserManager.Verify(manager => manager.ResetAccessFailedCountAsync(user), Times.Never);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_Password_InvalidUser_ReturnsForbidResult()
        {
            // Arrange
            var request = CreateRequest(GrantTypes.Password, username: "unknown", password: "${TEST_FIXTURE_003}");
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
                    password: "${TEST_FIXTURE_001}"),
                null);

            AssertPasswordGrantRejected(result);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_Password_IncompleteMigration_WhenAllMigrationSwitchesAreDisabled_ReturnsInvalidGrant()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "migration-user",
                IsActive = true
            };
            SetupPasswordGrant(user, isLockedOut: false);
            _mockCredentialMigrationStateStore
                .Setup(store => store.FindAsync(user.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMigrationRecord(user.Id, CredentialMigrationState.Required));

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(
                    GrantTypes.Password,
                    username: user.UserName,
                    password: "${TEST_FIXTURE_001}"),
                null);

            AssertPasswordGrantRejected(result);
            _mockUserManager.Verify(manager => manager.CheckPasswordAsync(user, It.IsAny<string>()), Times.Never);
            _mockStage2CredentialMigrationService.Verify(
                service => service.AuthenticateCompletedAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_Password_FinalizedMigration_UsesDirectoryAuthentication()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "directory-user",
                IsActive = true
            };
            SetupPasswordGrant(user, passwordIsValid: false);
            EnableDirectoryAuthentication();
            _mockCredentialMigrationStateStore
                .Setup(store => store.FindAsync(user.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMigrationRecord(user.Id, CredentialMigrationState.LocalFinalized));
            _mockStage2CredentialMigrationService
                .Setup(service => service.AuthenticateCompletedAsync(
                    user.Id,
                    "${TEST_FIXTURE_004}",
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DirectoryCredentialResult(DirectoryCredentialOutcome.Authenticated));

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(
                    GrantTypes.Password,
                    username: user.UserName,
                    password: "${TEST_FIXTURE_004}"),
                null);

            Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
            _mockUserManager.Verify(manager => manager.CheckPasswordAsync(user, It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_Password_FinalizedMigration_DeniesLocalPasswordOnly()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "directory-user",
                IsActive = true
            };
            SetupPasswordGrant(user, passwordIsValid: true);
            EnableDirectoryAuthentication();
            _mockCredentialMigrationStateStore
                .Setup(store => store.FindAsync(user.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMigrationRecord(user.Id, CredentialMigrationState.LocalFinalized));
            _mockStage2CredentialMigrationService
                .Setup(service => service.AuthenticateCompletedAsync(
                    user.Id,
                    "${TEST_FIXTURE_001}",
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DirectoryCredentialResult(DirectoryCredentialOutcome.InvalidCredentials));

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(
                    GrantTypes.Password,
                    username: user.UserName,
                    password: "${TEST_FIXTURE_001}"),
                null);

            AssertPasswordGrantRejected(result);
            _mockUserManager.Verify(manager => manager.CheckPasswordAsync(user, It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_Password_FinalizedMigration_WhenDirectoryAuthenticationDisabled_Denies()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "directory-user",
                IsActive = true
            };
            SetupPasswordGrant(user, passwordIsValid: true);
            _directoryIntegrationOptions.Enabled = true;
            _mockCredentialMigrationStateStore
                .Setup(store => store.FindAsync(user.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMigrationRecord(user.Id, CredentialMigrationState.LocalFinalized));

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(
                    GrantTypes.Password,
                    username: user.UserName,
                    password: "${TEST_FIXTURE_001}"),
                null);

            AssertPasswordGrantRejected(result);
            _mockUserManager.Verify(manager => manager.CheckPasswordAsync(user, It.IsAny<string>()), Times.Never);
            _mockStage2CredentialMigrationService.Verify(
                service => service.AuthenticateCompletedAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_Password_FinalizedMigration_WhenDirectoryAuthenticationFails_Denies()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "directory-user",
                IsActive = true
            };
            SetupPasswordGrant(user, passwordIsValid: true);
            EnableDirectoryAuthentication();
            _mockCredentialMigrationStateStore
                .Setup(store => store.FindAsync(user.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMigrationRecord(user.Id, CredentialMigrationState.LocalFinalized));
            _mockStage2CredentialMigrationService
                .Setup(service => service.AuthenticateCompletedAsync(
                    user.Id,
                    "${TEST_FIXTURE_001}",
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException());

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(
                    GrantTypes.Password,
                    username: user.UserName,
                    password: "${TEST_FIXTURE_001}"),
                null);

            AssertPasswordGrantRejected(result);
            _mockUserManager.Verify(manager => manager.CheckPasswordAsync(user, It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_Password_WhenAllMigrationSwitchesAreDisabled_UsesLocalPassword()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "local-user",
                IsActive = true
            };
            SetupPasswordGrant(user, passwordIsValid: true);

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(
                    GrantTypes.Password,
                    username: user.UserName,
                    password: "${TEST_FIXTURE_001}"),
                null);

            Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
            _mockUserManager.Verify(manager => manager.CheckPasswordAsync(user, "${TEST_FIXTURE_001}"), Times.Once);
            _mockCredentialMigrationStateStore.Verify(
                store => store.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_Password_FinalizedMigration_WhenAllMigrationSwitchesAreDisabled_Denies()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "directory-user",
                IsActive = true
            };
            SetupPasswordGrant(user, passwordIsValid: true);
            _mockCredentialMigrationStateStore
                .Setup(store => store.FindAsync(user.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMigrationRecord(user.Id, CredentialMigrationState.LocalFinalized));

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(
                    GrantTypes.Password,
                    username: user.UserName,
                    password: "${TEST_FIXTURE_001}"),
                null);

            AssertPasswordGrantRejected(result);
            _mockUserManager.Verify(manager => manager.CheckPasswordAsync(user, It.IsAny<string>()), Times.Never);
            _mockStage2CredentialMigrationService.Verify(
                service => service.AuthenticateCompletedAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
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
                    password: "${TEST_FIXTURE_001}"),
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
                    password: "${TEST_FIXTURE_001}"),
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
                    password: "${TEST_FIXTURE_001}"),
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
                    password: "${TEST_FIXTURE_001}"),
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
                    password: "${TEST_FIXTURE_001}"),
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
                    password: "${TEST_FIXTURE_001}"),
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
                    password: "${TEST_FIXTURE_001}"),
                null);

            AssertPasswordGrantRejected(result);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task HandleTokenRequestAsync_Password_MandatoryMfaWithPasskey_ShouldCountOnlyActiveCredentials(bool disabled)
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "password-user",
                IsActive = true,
                MfaRequirementNotifiedAt = DateTime.UtcNow.AddDays(-30)
            };
            SetupPasswordGrant(user);
            SetupMockUserCredentials(new UserCredential { UserId = user.Id, DisabledAtUtc = disabled ? DateTime.UtcNow : null });
            SetupMandatoryMfaPolicy(gracePeriodDays: 3);

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(
                    GrantTypes.Password,
                    username: user.UserName,
                    password: "${TEST_FIXTURE_001}"),
                null);

            if (disabled)
            {
                AssertPasswordGrantRejected(result);
            }
            else
            {
                Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
            }
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
                    password: "${TEST_FIXTURE_001}"),
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
                CreateRequest(GrantTypes.RefreshToken, refreshToken: "${TEST_FIXTURE_005}"),
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
                CreateRequest(GrantTypes.RefreshToken, refreshToken: "${TEST_FIXTURE_005}"),
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
                CreateRequest(GrantTypes.RefreshToken, refreshToken: "${TEST_FIXTURE_005}"),
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
                CreateRequest(GrantTypes.RefreshToken, refreshToken: "${TEST_FIXTURE_005}"),
                principal);

            var signInResult = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
            Assert.Equal(
                user.Id.ToString(),
                signInResult.Principal!.GetClaim(Claims.Subject));
        }

        [Fact]
        public async Task HandleTokenRequestAsync_RefreshToken_MultipleAmrClaims_PreservesDistinctValues()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "refresh-user",
                Email = "refresh-user@test.local",
                IsActive = true
            };
            var principal = SetupRefreshGrant(user);
            var identity = Assert.IsType<ClaimsIdentity>(principal.Identity);
            identity.AddClaim(new Claim(AuthConstants.ClaimTypes.Amr, AuthConstants.Amr.Password));
            identity.AddClaim(new Claim(AuthConstants.ClaimTypes.Amr, AuthConstants.Amr.Mfa));
            identity.AddClaim(new Claim(AuthConstants.ClaimTypes.Amr, AuthConstants.Amr.Mfa));

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(GrantTypes.RefreshToken, refreshToken: "${TEST_FIXTURE_005}"),
                principal);

            var signInResult = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
            Assert.Equal(
                [AuthConstants.Amr.Mfa, AuthConstants.Amr.Password],
                signInResult.Principal!.GetClaims(AuthConstants.ClaimTypes.Amr).Order());
        }

        [Fact]
        public async Task HandleTokenRequestAsync_RefreshToken_ClientMfaPolicyEnabledAfterIssuanceWithoutMfa_ReturnsInvalidGrant()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "refresh-user",
                IsActive = true
            };
            var principal = SetupRefreshGrant(user, clientRequiresMfa: true);

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(GrantTypes.RefreshToken, refreshToken: "${TEST_FIXTURE_005}"),
                principal);

            AssertInvalidGrant(result);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_RefreshToken_GlobalMfaPolicyEnabledForClientWithoutFlag_ReturnsInvalidGrant()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "refresh-user",
                IsActive = true
            };
            var principal = SetupRefreshGrant(user);
            _mockSecurityPolicyService
                .Setup(service => service.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy { EnforceMandatoryMfaEnrollment = true });

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(GrantTypes.RefreshToken, refreshToken: "${TEST_FIXTURE_005}"),
                principal);

            AssertInvalidGrant(result);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_RefreshToken_WithoutMfaPolicy_PreservesBaselineSuccess()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "refresh-user",
                Email = "refresh-user@test.local",
                IsActive = true
            };
            var principal = SetupRefreshGrant(user);

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(GrantTypes.RefreshToken, refreshToken: "${TEST_FIXTURE_005}"),
                principal);

            Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
        }

        [Theory]
        [InlineData(AuthConstants.Amr.Mfa, true)]
        [InlineData(AuthConstants.Amr.HardwareKey, false)]
        public async Task HandleTokenRequestAsync_RefreshToken_ClientMfaPolicyWithMfaEvidence_ReturnsSignInResult(
            string amrValue, bool satisfiesMfa)
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "refresh-user",
                Email = "refresh-user@test.local",
                IsActive = true
            };
            var principal = SetupRefreshGrant(user, clientRequiresMfa: true);
            Assert.IsType<ClaimsIdentity>(principal.Identity)
                .AddClaim(new Claim(AuthConstants.ClaimTypes.Amr, amrValue));

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(GrantTypes.RefreshToken, refreshToken: "${TEST_FIXTURE_005}"),
                principal);

            if (satisfiesMfa) Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
            else AssertInvalidGrant(result);
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(true, true, false)]
        [InlineData(true, false, true)]
        public async Task HandleTokenRequestAsync_AuthorizationCode_RestrictedAccount_ReturnsInvalidGrant(
            bool isActive,
            bool isDeleted,
            bool isLockedOut)
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "authorization-code-user",
                IsActive = isActive,
                IsDeleted = isDeleted
            };
            var principal = SetupAuthorizationCodeGrant(user, isLockedOut);

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(GrantTypes.AuthorizationCode, code: "opaque-authorization-code"),
                principal);

            AssertInvalidGrant(result);
        }

        [Theory]
        [InlineData(PersonStatus.Suspended, null, null, false)]
        [InlineData(PersonStatus.Active, 1, null, false)]
        [InlineData(PersonStatus.Active, null, -1, false)]
        [InlineData(PersonStatus.Active, null, null, true)]
        public async Task HandleTokenRequestAsync_AuthorizationCode_IneligiblePerson_ReturnsInvalidGrant(
            PersonStatus status,
            int? startDateOffsetDays,
            int? endDateOffsetDays,
            bool isDeleted)
        {
            var personId = Guid.NewGuid();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "authorization-code-user",
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
            var principal = SetupAuthorizationCodeGrant(user);
            SetupMockPersons(person);

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(GrantTypes.AuthorizationCode, code: "opaque-authorization-code"),
                principal);

            AssertInvalidGrant(result);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_AuthorizationCode_MissingLinkedPerson_ReturnsInvalidGrant()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "authorization-code-user",
                IsActive = true,
                PersonId = Guid.NewGuid()
            };
            var principal = SetupAuthorizationCodeGrant(user);
            SetupMockPersons();

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(GrantTypes.AuthorizationCode, code: "opaque-authorization-code"),
                principal);

            AssertInvalidGrant(result);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_AuthorizationCode_EligibleLinkedUser_ReturnsOriginalPrincipal()
        {
            var personId = Guid.NewGuid();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "authorization-code-user",
                IsActive = true,
                PersonId = personId
            };
            var principal = SetupAuthorizationCodeGrant(user);
            SetupMockPersons(new Person
            {
                Id = personId,
                Status = PersonStatus.Active
            });

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(GrantTypes.AuthorizationCode, code: "opaque-authorization-code"),
                principal);

            var signInResult = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
            Assert.Same(principal, signInResult.Principal);
            Assert.Equal(user.Id.ToString(), signInResult.Principal!.GetClaim(Claims.Subject));
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(true, true, false)]
        [InlineData(true, false, true)]
        public async Task HandleTokenRequestAsync_DeviceCode_RestrictedAccount_ReturnsInvalidGrant(
            bool isActive,
            bool isDeleted,
            bool isLockedOut)
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "device-code-user",
                IsActive = isActive,
                IsDeleted = isDeleted
            };
            var principal = SetupDeviceCodeGrant(user, isLockedOut);

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(GrantTypes.DeviceCode),
                principal);

            AssertInvalidGrant(result);
        }

        [Theory]
        [InlineData(PersonStatus.Suspended, null, null, false)]
        [InlineData(PersonStatus.Active, 1, null, false)]
        [InlineData(PersonStatus.Active, null, -1, false)]
        [InlineData(PersonStatus.Active, null, null, true)]
        public async Task HandleTokenRequestAsync_DeviceCode_IneligiblePerson_ReturnsInvalidGrant(
            PersonStatus status,
            int? startDateOffsetDays,
            int? endDateOffsetDays,
            bool isDeleted)
        {
            var personId = Guid.NewGuid();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "device-code-user",
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
            var principal = SetupDeviceCodeGrant(user);
            SetupMockPersons(person);

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(GrantTypes.DeviceCode),
                principal);

            AssertInvalidGrant(result);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_DeviceCode_MissingLinkedPerson_ReturnsInvalidGrant()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "device-code-user",
                IsActive = true,
                PersonId = Guid.NewGuid()
            };
            var principal = SetupDeviceCodeGrant(user);
            SetupMockPersons();

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(GrantTypes.DeviceCode),
                principal);

            AssertInvalidGrant(result);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_DeviceCode_EligibleLinkedUser_PreservesScopes()
        {
            var personId = Guid.NewGuid();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "device-code-user",
                IsActive = true,
                PersonId = personId
            };
            var principal = SetupDeviceCodeGrant(user);
            SetupMockPersons(new Person
            {
                Id = personId,
                Status = PersonStatus.Active
            });

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(GrantTypes.DeviceCode),
                principal);

            var signInResult = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
            Assert.Equal(user.Id.ToString(), signInResult.Principal!.GetClaim(Claims.Subject));
            Assert.Equal(
                principal.GetScopes().Order(),
                signInResult.Principal.GetScopes().Order());
        }

        [Fact]
        public async Task HandleTokenRequestAsync_DeviceCode_EligibleUnlinkedUser_ReturnsSignInResult()
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "device-code-user",
                IsActive = true
            };
            var principal = SetupDeviceCodeGrant(user);

            var result = await _service.HandleTokenRequestAsync(
                CreateRequest(GrantTypes.DeviceCode),
                principal);

            var signInResult = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
            Assert.Equal(user.Id.ToString(), signInResult.Principal!.GetClaim(Claims.Subject));
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

        private ClaimsPrincipal SetupRefreshGrant(
            ApplicationUser user,
            bool isLockedOut = false,
            bool clientRequiresMfa = false)
        {
            var clientApp = new object();
            _mockApplicationManager
                .Setup(m => m.FindByClientIdAsync("test-client", It.IsAny<CancellationToken>()))
                .ReturnsAsync(clientApp);
            _mockApplicationManager
                .Setup(m => m.GetPermissionsAsync(clientApp, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ImmutableArray.Create(OpenIddictConstants.Permissions.GrantTypes.RefreshToken));
            _mockApplicationManager
                .Setup(m => m.GetPropertiesAsync(clientApp, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateClientProperties(clientRequiresMfa));

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

        private static ImmutableDictionary<string, JsonElement> CreateClientProperties(bool requireMfa)
        {
            return requireMfa
                ? ImmutableDictionary<string, JsonElement>.Empty.Add(
                    AuthConstants.Properties.RequireMfa,
                    JsonSerializer.SerializeToElement(true))
                : ImmutableDictionary<string, JsonElement>.Empty;
        }

        private ClaimsPrincipal SetupAuthorizationCodeGrant(
            ApplicationUser user,
            bool isLockedOut = false)
        {
            var clientApp = new object();
            _mockApplicationManager
                .Setup(m => m.FindByClientIdAsync("test-client", It.IsAny<CancellationToken>()))
                .ReturnsAsync(clientApp);
            _mockApplicationManager
                .Setup(m => m.GetPermissionsAsync(clientApp, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ImmutableArray.Create(
                    OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode));

            _mockUserManager.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
            _mockUserManager.Setup(m => m.IsLockedOutAsync(user)).ReturnsAsync(isLockedOut);
            _mockSignInManager.Setup(m => m.CanSignInAsync(user)).ReturnsAsync(true);

            return new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(Claims.Subject, user.Id.ToString())],
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));
        }

        private ClaimsPrincipal SetupDeviceCodeGrant(
            ApplicationUser user,
            bool isLockedOut = false)
        {
            var clientApp = new object();
            _mockApplicationManager
                .Setup(m => m.FindByClientIdAsync("test-client", It.IsAny<CancellationToken>()))
                .ReturnsAsync(clientApp);
            _mockApplicationManager
                .Setup(m => m.GetPermissionsAsync(clientApp, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ImmutableArray.Create(
                    OpenIddictConstants.Permissions.GrantTypes.DeviceCode,
                    OpenIddictConstants.Permissions.Prefixes.Scope + Scopes.OpenId,
                    OpenIddictConstants.Permissions.Prefixes.Scope + Scopes.Profile));

            SetupMockUsers(user);
            _mockUserManager.Setup(m => m.IsLockedOutAsync(user)).ReturnsAsync(isLockedOut);
            _mockUserManager.Setup(m => m.GetUserIdAsync(user)).ReturnsAsync(user.Id.ToString());
            _mockSignInManager.Setup(m => m.CanSignInAsync(user)).ReturnsAsync(true);
            _mockApiResourceService
                .Setup(service => service.GetAudiencesByScopesAsync(
                    It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync([]);

            var identity = new ClaimsIdentity(
                [new Claim(Claims.Subject, user.Id.ToString())],
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            identity.SetScopes(Scopes.OpenId, Scopes.Profile);
            return new ClaimsPrincipal(identity);
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
                .Setup(m => m.CheckPasswordAsync(user, "${TEST_FIXTURE_001}"))
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

        private void EnableDirectoryAuthentication()
        {
            _directoryIntegrationOptions.Enabled = true;
            _directoryIntegrationOptions.AuthenticationEnabled = true;
        }

        private static CredentialMigrationRecord CreateMigrationRecord(
            Guid userId,
            CredentialMigrationState state) =>
            new(
                userId,
                new DirectoryObjectBinding("test", "subject", Guid.NewGuid()),
                state);

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
