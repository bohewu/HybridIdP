using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Core.Application;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests
{
    public class LoginServiceTests : IDisposable
    {
        private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
        private readonly Mock<ISecurityPolicyService> _securityPolicyServiceMock;
        private readonly Mock<ILegacyAuthService> _legacyAuthServiceMock;
        private readonly Mock<IJitProvisioningService> _jitProvisioningServiceMock;
        private readonly Mock<ILogger<LoginService>> _loggerMock;
        private readonly ApplicationDbContext _dbContext;
        private readonly LoginService _service;

        public LoginServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new ApplicationDbContext(options);

            var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
            _userManagerMock = new Mock<UserManager<ApplicationUser>>(userStoreMock.Object, null, null, null, null, null, null, null, null);
            
            _securityPolicyServiceMock = new Mock<ISecurityPolicyService>();
            _legacyAuthServiceMock = new Mock<ILegacyAuthService>();
            _jitProvisioningServiceMock = new Mock<IJitProvisioningService>();
            _loggerMock = new Mock<ILogger<LoginService>>();

            // Default Policy
            _securityPolicyServiceMock.Setup(x => x.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy());

            _service = new LoginService(
                _userManagerMock.Object,
                _securityPolicyServiceMock.Object,
                _legacyAuthServiceMock.Object,
                _jitProvisioningServiceMock.Object,
                _dbContext,
                _loggerMock.Object
            );
        }

        public void Dispose()
        {
            _dbContext.Database.EnsureDeleted();
            _dbContext.Dispose();
        }

        [Fact]
        public async Task AuthenticateAsync_ShouldAttachPersonToUser_WhenLinkedAndActive()
        {
            // Arrange
            var personId = Guid.NewGuid();
            var user = new ApplicationUser 
            { 
                UserName = "testuser", 
                Email = "test@example.com",
                PersonId = personId 
            };
            
            var person = new Person 
            { 
                Id = personId, 
                Status = PersonStatus.Active, 
                Locale = "zh-TW" 
            };
            _dbContext.Persons.Add(person);
            await _dbContext.SaveChangesAsync();

            // Setup UserManager
            _userManagerMock.Setup(x => x.FindByEmailAsync(user.Email)).ReturnsAsync(user);
            _userManagerMock.Setup(x => x.CheckPasswordAsync(user, "password")).ReturnsAsync(true);
            _userManagerMock.Setup(x => x.IsLockedOutAsync(user)).ReturnsAsync(false);
            _userManagerMock.Setup(x => x.ResetAccessFailedCountAsync(user)).ReturnsAsync(IdentityResult.Success);

            // Act
            var result = await _service.AuthenticateAsync(user.Email, "password");

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.User);
            Assert.NotNull(result.User.Person); // Verification point: Person attached?
            Assert.Equal("zh-TW", result.User.Person.Locale); // Verification point: Locale accessible
        }
    }
}
