using Xunit;
using Microsoft.EntityFrameworkCore;
using Moq;
using Core.Application;
using OpenIddict.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Constants;
using Infrastructure;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Web.IdP.Services;
using System.Threading.Tasks;
using Core.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System;
using System.Net;
using System.Text.Json;
using OpenIddict.Server.AspNetCore;
using System.Threading;
using System.Collections.Immutable;
using Microsoft.AspNetCore.Http.Features;

namespace Tests.Application.UnitTests
{
    public class AuthorizationServiceTests
    {
        private readonly Mock<IOpenIddictApplicationManager> _mockApplicationManager;
        private readonly Mock<IOpenIddictAuthorizationManager> _mockAuthorizationManager;
        private readonly Mock<IOpenIddictScopeManager> _mockScopeManager;
        private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
        private readonly Mock<RoleManager<ApplicationRole>> _mockRoleManager;
        private readonly Mock<IApplicationDbContext> _mockDb;
        private readonly Mock<IApiResourceService> _mockApiResourceService;
        private readonly Mock<ILocalizationService> _mockLocalizationService;
        private readonly Mock<IScopeService> _mockScopeService;
        private readonly Mock<IAuditService> _mockAuditService;
        private readonly Mock<IClientAllowedScopesService> _mockClientAllowedScopesService;
        private readonly Mock<IClientScopeRequestProcessor> _mockClientScopeProcessor;
        private readonly Mock<ILogger<AuthorizationService>> _mockLogger;
        private readonly Mock<IHttpContextAccessor> _mockHttpContextAccessor;
        private readonly Mock<IClaimsEnrichmentService> _mockClaimsEnricher;
        private readonly Mock<ISecurityPolicyService> _mockSecurityPolicyService;
        private readonly Mock<IPasskeyService> _mockPasskeyService;
        private readonly AuthorizationService _authorizationService;

        public AuthorizationServiceTests()
        {
            _mockApplicationManager = new Mock<IOpenIddictApplicationManager>();
            _mockAuthorizationManager = new Mock<IOpenIddictAuthorizationManager>();
            _mockScopeManager = new Mock<IOpenIddictScopeManager>();
            _mockUserManager = MockUserManager();
            _mockRoleManager = MockRoleManager();
            _mockDb = new Mock<IApplicationDbContext>();
            _mockApiResourceService = new Mock<IApiResourceService>();
            _mockLocalizationService = new Mock<ILocalizationService>();
            _mockScopeService = new Mock<IScopeService>();
            _mockAuditService = new Mock<IAuditService>();
            _mockClientAllowedScopesService = new Mock<IClientAllowedScopesService>();
            _mockClientScopeProcessor = new Mock<IClientScopeRequestProcessor>();
            _mockLogger = new Mock<ILogger<AuthorizationService>>();
            _mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
            _mockClaimsEnricher = new Mock<IClaimsEnrichmentService>();
            _mockSecurityPolicyService = new Mock<ISecurityPolicyService>();
            _mockPasskeyService = new Mock<IPasskeyService>();
            _mockPasskeyService
                .Setup(service => service.GetUserPasskeysAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            // Default setup
            _mockClaimsEnricher.Setup(x => x.AddScopeMappedClaimsAsync(It.IsAny<ClaimsIdentity>(), It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
                .Returns(Task.CompletedTask);
            _mockClaimsEnricher.Setup(x => x.AddPermissionClaimsAsync(It.IsAny<ClaimsIdentity>(), It.IsAny<ApplicationUser>()))
                .Returns(Task.CompletedTask);

            _authorizationService = new AuthorizationService(
                _mockApplicationManager.Object,
                _mockAuthorizationManager.Object,
                _mockScopeManager.Object,
                _mockUserManager.Object,
                _mockRoleManager.Object,
                _mockDb.Object,
                _mockApiResourceService.Object,
                _mockLocalizationService.Object,
                _mockScopeService.Object,
                _mockAuditService.Object,
                _mockClientAllowedScopesService.Object,
                _mockClientScopeProcessor.Object,
                _mockLogger.Object,
                _mockHttpContextAccessor.Object,
                _mockClaimsEnricher.Object,
                _mockSecurityPolicyService.Object,
                _mockPasskeyService.Object
            );
        }

        private static Mock<UserManager<ApplicationUser>> MockUserManager()
        {
            var store = new Mock<IUserStore<ApplicationUser>>();
            return new Mock<UserManager<ApplicationUser>>(store.Object, null, null, null, null, null, null, null, null);
        }

        private static Mock<RoleManager<ApplicationRole>> MockRoleManager()
        {
            var store = new Mock<IRoleStore<ApplicationRole>>();
            return new Mock<RoleManager<ApplicationRole>>(store.Object, null, null, null, null);
        }


        [Fact]
        public async Task HandleAuthorizeRequestAsync_ShouldChallenge_WhenUserNotAuthenticated()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity()); // Unauthenticated
#pragma warning disable CA2254 // Template should be a static expression
            _mockLogger.Setup(x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()));
#pragma warning restore CA2254 // Template should be a static expression

            var request = new OpenIddictRequest();
            var context = new DefaultHttpContext();
            
            var httpContextAccessor = new Mock<IHttpContextAccessor>();
            httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

             var authService = new AuthorizationService(
                _mockApplicationManager.Object,
                _mockAuthorizationManager.Object,
                _mockScopeManager.Object,
                _mockUserManager.Object,
                _mockRoleManager.Object,
                _mockDb.Object,
                _mockApiResourceService.Object,
                _mockLocalizationService.Object,
                _mockScopeService.Object,
                _mockAuditService.Object,
                _mockClientAllowedScopesService.Object,
                _mockClientScopeProcessor.Object,
                _mockLogger.Object,
                httpContextAccessor.Object,
                _mockClaimsEnricher.Object,
                _mockSecurityPolicyService.Object,
                _mockPasskeyService.Object
            );

            // Act
            var result = await authService.HandleAuthorizeRequestAsync(user, request, null);

            // Assert
            var challengeResult = Assert.IsType<ChallengeResult>(result);
            Assert.Contains(IdentityConstants.ApplicationScheme, challengeResult.AuthenticationSchemes);
        }

        [Fact]
        public async Task HandleAuthorizeRequestAsync_WithPromptNoneAndUnauthenticatedUser_ReturnsLoginRequired()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity()); // Unauthenticated
            var request = new OpenIddictRequest
            {
                ClientId = "client",
                Prompt = "none"
            };

            var context = new DefaultHttpContext();

            var httpContextAccessor = new Mock<IHttpContextAccessor>();
            httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            var authService = new AuthorizationService(
                _mockApplicationManager.Object,
                _mockAuthorizationManager.Object,
                _mockScopeManager.Object,
                _mockUserManager.Object,
                _mockRoleManager.Object,
                _mockDb.Object,
                _mockApiResourceService.Object,
                _mockLocalizationService.Object,
                _mockScopeService.Object,
                _mockAuditService.Object,
                _mockClientAllowedScopesService.Object,
                _mockClientScopeProcessor.Object,
                _mockLogger.Object,
                httpContextAccessor.Object,
                _mockClaimsEnricher.Object,
                _mockSecurityPolicyService.Object,
                _mockPasskeyService.Object
            );

            // Act
            var result = await authService.HandleAuthorizeRequestAsync(user, request, request.Prompt);

            // Assert
            var forbidResult = Assert.IsType<ForbidResult>(result);
            Assert.NotNull(forbidResult.Properties);
            Assert.Equal(
                OpenIddictConstants.Errors.LoginRequired,
                forbidResult.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
        }

        [Fact]
        public async Task HandleAuthorizeRequestAsync_WithPromptNoneAndUnauthenticatedUser_LogsSecurityTelemetry()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity()); // Unauthenticated
            var request = new OpenIddictRequest
            {
                ClientId = "probe-client",
                Prompt = "none",
                Scope = "openid profile"
            };

            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
            context.Request.Headers["User-Agent"] = "security-probe-agent";

            _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            // Act
            var result = await _authorizationService.HandleAuthorizeRequestAsync(user, request, request.Prompt);

            // Assert
            var forbidResult = Assert.IsType<ForbidResult>(result);
            Assert.Equal(
                OpenIddictConstants.Errors.LoginRequired,
                forbidResult.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);

            _mockAuditService.Verify(a => a.LogEventAsync(
                "AuthorizationPromptNoneProbe",
                null,
                It.Is<string>(details =>
                    details.Contains("\"clientId\":\"probe-client\"") &&
                    details.Contains("\"prompt\":\"none\"") &&
                    details.Contains("\"scope\":\"openid profile\"") &&
                    details.Contains("\"ip\":\"203.0.113.10\"") &&
                    details.Contains("\"userAgent\":\"security-probe-agent\"")),
                "203.0.113.10",
                "security-probe-agent",
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleAuthorizeRequestAsync_WithInvalidPromptCombination_ReturnsInvalidRequest()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity()); // Unauthenticated
            var request = new OpenIddictRequest
            {
                ClientId = "client",
                Prompt = "none login"
            };

            var context = new DefaultHttpContext();

            var httpContextAccessor = new Mock<IHttpContextAccessor>();
            httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            var authService = new AuthorizationService(
                _mockApplicationManager.Object,
                _mockAuthorizationManager.Object,
                _mockScopeManager.Object,
                _mockUserManager.Object,
                _mockRoleManager.Object,
                _mockDb.Object,
                _mockApiResourceService.Object,
                _mockLocalizationService.Object,
                _mockScopeService.Object,
                _mockAuditService.Object,
                _mockClientAllowedScopesService.Object,
                _mockClientScopeProcessor.Object,
                _mockLogger.Object,
                httpContextAccessor.Object,
                _mockClaimsEnricher.Object,
                _mockSecurityPolicyService.Object,
                _mockPasskeyService.Object
            );

            // Act
            var result = await authService.HandleAuthorizeRequestAsync(user, request, request.Prompt);

            // Assert
            var forbidResult = Assert.IsType<ForbidResult>(result);
            Assert.NotNull(forbidResult.Properties);
            Assert.Equal(
                OpenIddictConstants.Errors.InvalidRequest,
                forbidResult.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
        }

        [Fact]
        public async Task HandleAuthorizeRequestAsync_ShouldThrow_WhenRequestIsNull()
        {
             // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity("Test"));
            var context = new DefaultHttpContext();
            // No OpenIddict feature set
            
            var httpContextAccessor = new Mock<IHttpContextAccessor>();
            httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

             var authService = new AuthorizationService(
                _mockApplicationManager.Object,
                _mockAuthorizationManager.Object,
                _mockScopeManager.Object,
                _mockUserManager.Object,
                _mockRoleManager.Object,
                _mockDb.Object,
                _mockApiResourceService.Object,
                _mockLocalizationService.Object,
                _mockScopeService.Object,
                _mockAuditService.Object,
                _mockClientAllowedScopesService.Object,
                _mockClientScopeProcessor.Object,
                _mockLogger.Object,
                httpContextAccessor.Object,
                _mockClaimsEnricher.Object,
                _mockSecurityPolicyService.Object,
                _mockPasskeyService.Object
            );

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => authService.HandleAuthorizeRequestAsync(user, null!, null));
        }

        [Fact]
        public async Task HandleAuthorizeRequestAsync_WithCodeResponseType_MissingPermission_ReturnsForbid()
        {
            // Arrange
            // Arrange
            var userId = Guid.NewGuid();
            var identity = new ClaimsIdentity("Test");
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, userId.ToString()));
            var user = new ClaimsPrincipal(identity);

            var request = new OpenIddictRequest
            {
                ClientId = "client",
                ResponseType = OpenIddictConstants.ResponseTypes.Code,
                Scope = "openid"
            };

            // Setup Context
            var context = new DefaultHttpContext();
            _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            // Setup Client
            var client = new object();
            _mockApplicationManager.Setup(m => m.FindByClientIdAsync("client", default)).ReturnsAsync(client);
            _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(client, default)).ReturnsAsync("TestApp");
            _mockApplicationManager.Setup(m => m.GetIdAsync(client, default)).ReturnsAsync("client-id-guid");

            // Setup Missing Permission
            _mockApplicationManager.Setup(m => m.GetPermissionsAsync(client, default))
                .ReturnsAsync(ImmutableArray.Create(OpenIddictConstants.Permissions.ResponseTypes.Token)); // Has Token but needs Code

            // Setup User
            var appUser = new ApplicationUser { Id = userId };
            _mockUserManager.Setup(m => m.GetUserAsync(user)).ReturnsAsync(appUser);
            SetupMockUsers(appUser);
            _mockSecurityPolicyService.Setup(x => x.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy());

            // Act
            var result = await _authorizationService.HandleAuthorizeRequestAsync(user, request, null);

            // Assert
            var forbidResult = Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task HandleAuthorizeRequestAsync_WithTokenResponseType_MissingPermission_ReturnsForbid()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var identity = new ClaimsIdentity("Test");
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, userId.ToString()));
            var user = new ClaimsPrincipal(identity);

            var request = new OpenIddictRequest
            {
                ClientId = "client",
                ResponseType = OpenIddictConstants.ResponseTypes.Token,
                Scope = "openid"
            };

            // Setup Context
            var context = new DefaultHttpContext();
            _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            // Setup Client
            var client = new object();
            _mockApplicationManager.Setup(m => m.FindByClientIdAsync("client", default)).ReturnsAsync(client);
            _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(client, default)).ReturnsAsync("TestApp");
            _mockApplicationManager.Setup(m => m.GetIdAsync(client, default)).ReturnsAsync("client-id-guid");

            // Setup Missing Permission
            _mockApplicationManager.Setup(m => m.GetPermissionsAsync(client, default))
                .ReturnsAsync(ImmutableArray.Create(OpenIddictConstants.Permissions.ResponseTypes.Code)); // Has Code but needs Token

            // Setup User
            var appUser = new ApplicationUser { Id = userId };
            _mockUserManager.Setup(m => m.GetUserAsync(user)).ReturnsAsync(appUser);
            SetupMockUsers(appUser);
            _mockSecurityPolicyService.Setup(x => x.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy());

            // Act
            var result = await _authorizationService.HandleAuthorizeRequestAsync(user, request, null);

            // Assert
            var forbidResult = Assert.IsType<ForbidResult>(result);
        }

        [Theory]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, true)]
        public async Task HandleAuthorizeRequestAsync_WhenClientGlobalOrAcrRequiresMfa_RedirectsToMfaSetup(
            bool clientRequiresMfa,
            bool globallyRequiresMfa,
            bool acrRequiresMfa)
        {
            var user = new ApplicationUser { Id = Guid.NewGuid() };
            SetupMockUsers(user);

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(OpenIddictConstants.Claims.Subject, user.Id.ToString())],
                "Test"));
            var request = new OpenIddictRequest
            {
                ClientId = "client",
                ResponseType = OpenIddictConstants.ResponseTypes.Code,
                Scope = "openid",
                AcrValues = acrRequiresMfa ? "mfa" : null
            };
            var application = new object();
            _mockApplicationManager
                .Setup(manager => manager.FindByClientIdAsync("client", It.IsAny<CancellationToken>()))
                .ReturnsAsync(application);
            _mockApplicationManager
                .Setup(manager => manager.GetPropertiesAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateClientProperties(clientRequiresMfa));
            _mockSecurityPolicyService
                .Setup(service => service.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy
                {
                    EnforceMandatoryMfaEnrollment = globallyRequiresMfa
                });

            var context = new DefaultHttpContext();
            context.Features.Set<ISessionFeature>(new TestSessionFeature
            {
                Session = new Mock<ISession>().Object
            });
            _mockHttpContextAccessor.Setup(accessor => accessor.HttpContext).Returns(context);

            var result = await _authorizationService.HandleAuthorizeRequestAsync(principal, request, null);

            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Contains("/Account/MfaSetup", redirect.Url);
        }

        [Fact]
        public async Task HandleAuthorizeRequestAsync_WhenNoMfaPolicyApplies_PreservesBaselineAuthorizationPath()
        {
            var user = new ApplicationUser { Id = Guid.NewGuid() };
            SetupMockUsers(user);

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(OpenIddictConstants.Claims.Subject, user.Id.ToString())],
                "Test"));
            var request = new OpenIddictRequest
            {
                ClientId = "client",
                ResponseType = OpenIddictConstants.ResponseTypes.Code,
                Scope = "openid"
            };
            var application = new object();
            SetupAuthorizationClient(application, false);
            _mockSecurityPolicyService
                .Setup(service => service.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy());
            _mockHttpContextAccessor
                .Setup(accessor => accessor.HttpContext)
                .Returns(new DefaultHttpContext());

            var result = await _authorizationService.HandleAuthorizeRequestAsync(principal, request, null);

            Assert.IsType<ForbidResult>(result);
        }

        [Theory]
        [InlineData(AuthConstants.Amr.Mfa)]
        [InlineData(AuthConstants.Amr.HardwareKey)]
        public async Task HandleAuthorizeRequestAsync_WhenClientRequiresMfaAndPrincipalHasEvidence_DoesNotRedirect(
            string amrValue)
        {
            var user = new ApplicationUser { Id = Guid.NewGuid() };
            SetupMockUsers(user);

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(OpenIddictConstants.Claims.Subject, user.Id.ToString()),
                    new Claim(AuthConstants.ClaimTypes.Amr, amrValue)
                ],
                "Test"));
            var request = new OpenIddictRequest
            {
                ClientId = "client",
                ResponseType = OpenIddictConstants.ResponseTypes.Code,
                Scope = "openid"
            };
            var application = new object();
            SetupAuthorizationClient(application, true);
            _mockSecurityPolicyService
                .Setup(service => service.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy());
            _mockHttpContextAccessor
                .Setup(accessor => accessor.HttpContext)
                .Returns(new DefaultHttpContext());

            var result = await _authorizationService.HandleAuthorizeRequestAsync(principal, request, null);

            Assert.IsType<ForbidResult>(result);
        }

        private void SetupAuthorizationClient(object application, bool requireMfa)
        {
            _mockApplicationManager
                .Setup(manager => manager.FindByClientIdAsync("client", It.IsAny<CancellationToken>()))
                .ReturnsAsync(application);
            _mockApplicationManager
                .Setup(manager => manager.GetPropertiesAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateClientProperties(requireMfa));
            _mockApplicationManager
                .Setup(manager => manager.GetIdAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync("client-id-guid");
            _mockApplicationManager
                .Setup(manager => manager.GetDisplayNameAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync("Test client");
            _mockApplicationManager
                .Setup(manager => manager.GetPermissionsAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ImmutableArray<string>.Empty);
        }

        private static ImmutableDictionary<string, JsonElement> CreateClientProperties(bool requireMfa)
        {
            return requireMfa
                ? ImmutableDictionary<string, JsonElement>.Empty.Add(
                    AuthConstants.Properties.RequireMfa,
                    JsonSerializer.SerializeToElement(true))
                : ImmutableDictionary<string, JsonElement>.Empty;
        }

        private sealed class TestSessionFeature : ISessionFeature
        {
            public required ISession Session { get; set; }
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

            _mockDb.Setup(c => c.Users).Returns(mockSet.Object);
        }
    }

}
