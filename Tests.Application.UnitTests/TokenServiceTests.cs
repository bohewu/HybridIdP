using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using Core.Application;
using Core.Domain;
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
        private readonly Mock<IApplicationDbContext> _mockDbContext;
        private readonly Mock<ILogger<TokenService>> _mockLogger;
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
            _mockDbContext = new Mock<IApplicationDbContext>();
            _mockLogger = new Mock<ILogger<TokenService>>();

            _service = new TokenService(
                _mockUserManager.Object,
                _mockSignInManager.Object,
                _mockRoleManager.Object,
                _mockApiResourceService.Object,
                _mockAuditService.Object,
                _mockDbContext.Object,
                _mockLogger.Object);
        }

        [Fact]
        public async Task HandleTokenRequestAsync_NullRequest_ThrowsArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _service.HandleTokenRequestAsync(null!, null));
        }

        [Fact]
        public async Task HandleTokenRequestAsync_UnsupportedGrantType_ThrowsInvalidOperationException()
        {
            var request = new OpenIddictRequest
            {
                GrantType = "unsupported_grant_type"
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.HandleTokenRequestAsync(request, null));
        }

        [Fact]
        public async Task HandleTokenRequestAsync_ClientCredentials_ReturnsSignInResult()
        {
            // Arrange
            var request = CreateRequest(GrantTypes.ClientCredentials, clientId: "service-client", scope: "api:read");
            _mockApiResourceService.Setup(s => s.GetAudiencesByScopesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new List<string> { "api1" });

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

            _mockSignInManager.Setup(m => m.CheckPasswordSignInAsync(user, "password", true))
                .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

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

            // Act
            var result = await _service.HandleTokenRequestAsync(request, null);

            // Assert
            var signInResult = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
            Assert.NotNull(signInResult.Principal);
            Assert.True(signInResult.Principal.HasClaim(Claims.Subject, userId.ToString()));
        }

        [Fact]
        public async Task HandleTokenRequestAsync_Password_InvalidUser_ReturnsForbidResult()
        {
            // Arrange
            var request = CreateRequest(GrantTypes.Password, username: "unknown", password: "password");
            _mockUserManager.Setup(m => m.FindByNameAsync("unknown")).ReturnsAsync((ApplicationUser?)null);

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
    }
}
