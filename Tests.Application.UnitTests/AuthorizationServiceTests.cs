using Xunit;
using Moq;
using Core.Application;
using OpenIddict.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Core.Domain;
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
using OpenIddict.Server.AspNetCore;
using System.Threading;
using System.Collections.Immutable;

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
            var user = new ClaimsPrincipal(new ClaimsIdentity("Test"));
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

            // Act
            var result = await _authorizationService.HandleAuthorizeRequestAsync(user, request, null);

            // Assert
            var forbidResult = Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task HandleAuthorizeRequestAsync_WithTokenResponseType_MissingPermission_ReturnsForbid()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity("Test"));
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

            // Act
            var result = await _authorizationService.HandleAuthorizeRequestAsync(user, request, null);

            // Assert
            var forbidResult = Assert.IsType<ForbidResult>(result);
        }
    }
}
