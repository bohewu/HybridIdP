using System.Security.Claims;
using OpenIddict.Abstractions;
using Web.IdP.Services;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tests.Application.UnitTests
{
    public class UserInfoServiceTests
    {
        private readonly UserInfoService _service;

        public UserInfoServiceTests()
        {
            _service = new UserInfoService();
        }

        [Fact]
        public async Task GetUserInfoAsync_NullPrincipal_ThrowsArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _service.GetUserInfoAsync(null!));
        }

        [Fact]
        public async Task GetUserInfoAsync_ReturnsExpectedClaims()
        {
            // Arrange
            var claims = new List<Claim>
            {
                new Claim(Claims.Subject, "sub123"),
                new Claim(Claims.Username, "testuser"),
                new Claim(Claims.Email, "test@example.com"),
                new Claim(Claims.EmailVerified, "true"),
                new Claim(Claims.Name, "Test User"),
                new Claim(Claims.GivenName, "Test"),
                new Claim(Claims.FamilyName, "User"),
                new Claim(Claims.Role, "admin"),
                new Claim(Claims.Role, "user")
            };
            var identity = new ClaimsIdentity(claims, "Bearer");
            var principal = new ClaimsPrincipal(identity);

            // Act
            var result = await _service.GetUserInfoAsync(principal);

            // Assert
            Assert.Equal("sub123", result[Claims.Subject]);
            Assert.Equal("testuser", result[Claims.PreferredUsername]);
            Assert.Equal("test@example.com", result[Claims.Email]);
            Assert.Equal(true, result[Claims.EmailVerified]); // boolean check
            Assert.Equal("Test User", result[Claims.Name]);
            Assert.Equal("Test", result[Claims.GivenName]);
            Assert.Equal("User", result[Claims.FamilyName]);
            
            var roles = result[Claims.Role] as IEnumerable<string>;
            Assert.NotNull(roles);
            Assert.Contains("admin", roles);
            Assert.Contains("user", roles);
        }
        
        [Fact]
        public async Task GetUserInfoAsync_MinimalClaims_ReturnsSubjectOnly()
        {
             // Arrange
            var claims = new List<Claim>
            {
                new Claim(Claims.Subject, "sub123"),
            };
            var identity = new ClaimsIdentity(claims, "Bearer");
            var principal = new ClaimsPrincipal(identity);

            // Act
            var result = await _service.GetUserInfoAsync(principal);

            // Assert
            Assert.Equal("sub123", result[Claims.Subject]);
            Assert.False(result.ContainsKey(Claims.PreferredUsername));
            Assert.False(result.ContainsKey(Claims.Email));
        }
    }
}
