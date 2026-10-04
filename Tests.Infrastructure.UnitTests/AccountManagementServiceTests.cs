using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Interfaces;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public class AccountManagementServiceTests
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<RoleManager<ApplicationRole>> _roleManagerMock;
    private readonly Mock<SignInManager<ApplicationUser>> _signInManagerMock;
    private readonly Mock<ISessionService> _sessionServiceMock;
    private readonly Mock<IAuditService> _auditServiceMock;
    private readonly Mock<ILoginService> _loginServiceMock;
    private readonly Mock<ISecurityPolicyService> _securityPolicyServiceMock;
    private readonly Mock<IPasskeyService> _passkeyServiceMock;
    private readonly DefaultHttpContext _httpContext;
    private readonly AccountManagementService _service;

    public AccountManagementServiceTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);

        // Mock UserManager
        var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStoreMock.Object,
            null, null, null, null, null, null, null, null);

        // Mock RoleManager
        var roleStoreMock = new Mock<IRoleStore<ApplicationRole>>();
        _roleManagerMock = new Mock<RoleManager<ApplicationRole>>(
            roleStoreMock.Object,
            null, null, null, null);

        // Mock SignInManager
        var contextAccessorMock = new Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        _httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(AuthConstants.ClaimTypes.Amr, AuthConstants.Amr.Password)],
                "Test"))
        };
        contextAccessorMock.Setup(accessor => accessor.HttpContext)
            .Returns(_httpContext);
        var claimsFactoryMock = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
        _signInManagerMock = new Mock<SignInManager<ApplicationUser>>(
            _userManagerMock.Object,
            contextAccessorMock.Object,
            claimsFactoryMock.Object,
            null, null, null, null);

        // Mock SessionService and AuditService
        _sessionServiceMock = new Mock<ISessionService>();
        _auditServiceMock = new Mock<IAuditService>();
        _loginServiceMock = new Mock<ILoginService>();
        _securityPolicyServiceMock = new Mock<ISecurityPolicyService>();
        _passkeyServiceMock = new Mock<IPasskeyService>();
        _loginServiceMock
            .Setup(service => service.ValidateExternalUserSignInAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationUser user, CancellationToken _) =>
                LoginResult.Success(user));
        _signInManagerMock
            .Setup(manager => manager.CanSignInAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(true);
        _securityPolicyServiceMock
            .Setup(service => service.GetCurrentPolicyAsync())
            .ReturnsAsync(new SecurityPolicy
            {
                EnforceMandatoryMfaEnrollment = false
            });

        // Create test logger factory for debugging
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var logger = loggerFactory.CreateLogger<AccountManagementService>();

        _service = new AccountManagementService(
            _db,
            _db, // Pass same instance as ApplicationDbContext
            _userManagerMock.Object,
            _roleManagerMock.Object,
            _signInManagerMock.Object,
            _sessionServiceMock.Object,
            _auditServiceMock.Object,
            _loginServiceMock.Object,
            _securityPolicyServiceMock.Object,
            _passkeyServiceMock.Object,
            logger);
    }

    [Fact]
    public async Task GetMyLinkedAccountsAsync_WithMultipleAccounts_ShouldReturnAllLinkedAccounts()
    {
        // Arrange: Create person with 2 linked accounts
        var personId = Guid.NewGuid();
        var person = new Person
        {
            Id = personId,
            FirstName = "John",
            LastName = "Doe",
            Birthdate = "1990-01-01",
            Locale = "en-US"
        };

        var user1Id = Guid.NewGuid();
        var user2Id = Guid.NewGuid();
        var role1Id = Guid.NewGuid();
        var role2Id = Guid.NewGuid();

        var role1 = new ApplicationRole { Id = role1Id, Name = "Member", NormalizedName = "MEMBER" };
        var role2 = new ApplicationRole { Id = role2Id, Name = "Staff", NormalizedName = "STAFF" };

        var user1 = new ApplicationUser
        {
            Id = user1Id,
            UserName = "john.member@example.com",
            Email = "john.member@example.com",
            PersonId = personId,
            Person = person
        };

        var user2 = new ApplicationUser
        {
            Id = user2Id,
            UserName = "john.staff@example.com",
            Email = "john.staff@example.com",
            PersonId = personId,
            Person = person
        };

        _db.Persons.Add(person);
        _db.Roles.AddRange(role1, role2);
        _db.Users.AddRange(user1, user2);
        _db.UserRoles.AddRange(
            new IdentityUserRole<Guid> { UserId = user1Id, RoleId = role1Id },
            new IdentityUserRole<Guid> { UserId = user2Id, RoleId = role2Id }
        );
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetMyLinkedAccountsAsync(user1Id);

        // Assert
        Assert.Equal(2, result.Count());
        var accounts = result.ToList();

        var account1 = accounts.FirstOrDefault(a => a.UserId == user1Id);
        Assert.NotNull(account1);
        Assert.Equal("john.member@example.com", account1.UserName);
        Assert.Contains("Member", account1.Roles);
        Assert.True(account1.IsCurrentAccount);

        var account2 = accounts.FirstOrDefault(a => a.UserId == user2Id);
        Assert.NotNull(account2);
        Assert.Equal("john.staff@example.com", account2.UserName);
        Assert.Contains("Staff", account2.Roles);
        Assert.False(account2.IsCurrentAccount);
    }

    [Fact]
    public async Task GetMyLinkedAccountsAsync_WithNoLinkedAccounts_ShouldReturnOnlyCurrentUser()
    {
        // Arrange: Single account with no other linked accounts
        var personId = Guid.NewGuid();
        var person = new Person
        {
            Id = personId,
            FirstName = "Jane",
            LastName = "Smith",
            Birthdate = "1995-05-15",
            Locale = "en-US"
        };

        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var role = new ApplicationRole { Id = roleId, Name = "Member", NormalizedName = "MEMBER" };

        var user = new ApplicationUser
        {
            Id = userId,
            UserName = "jane@example.com",
            Email = "jane@example.com",
            PersonId = personId,
            Person = person
        };

        _db.Persons.Add(person);
        _db.Roles.Add(role);
        _db.Users.Add(user);
        _db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = roleId });
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetMyLinkedAccountsAsync(userId);

        // Assert
        var singleAccount = Assert.Single(result);
        Assert.Equal(userId, singleAccount.UserId);
        Assert.True(singleAccount.IsCurrentAccount);
    }

    [Fact]
    public async Task SwitchToAccountAsync_WithSamePersonId_ShouldSucceedAndAuditLog()
    {
        // Arrange: Two accounts belonging to same person
        var personId = Guid.NewGuid();
        var currentUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var memberRoleId = Guid.NewGuid();
        var staffRoleId = Guid.NewGuid();

        var person = new Person
        {
            Id = personId,
            FirstName = "Henry",
            LastName = "MultiAccount",
            Birthdate = "1986-08-12",
            Locale = "en-US"
        };

        var currentUser = new ApplicationUser
        {
            Id = currentUserId,
            UserName = "henry.member@example.com",
            Email = "henry.member@example.com",
            PersonId = personId,
            Person = person
        };

        var targetUser = new ApplicationUser
        {
            Id = targetUserId,
            UserName = "henry.staff@example.com",
            Email = "henry.staff@example.com",
            PersonId = personId,
            Person = person
        };

        var memberRole = new ApplicationRole { Id = memberRoleId, Name = "Member", NormalizedName = "MEMBER" };
        var staffRole = new ApplicationRole { Id = staffRoleId, Name = "Staff", NormalizedName = "STAFF" };

        _db.Persons.Add(person);
        _db.Users.AddRange(currentUser, targetUser);
        _db.Roles.AddRange(memberRole, staffRole);
        _db.UserRoles.AddRange(
            new IdentityUserRole<Guid> { UserId = currentUserId, RoleId = memberRoleId },
            new IdentityUserRole<Guid> { UserId = targetUserId, RoleId = staffRoleId }
        );
        await _db.SaveChangesAsync();

        // Mock SignInManager
        _userManagerMock.Setup(um => um.FindByIdAsync(currentUserId.ToString()))
            .ReturnsAsync(currentUser);
        _userManagerMock.Setup(um => um.FindByIdAsync(targetUserId.ToString()))
            .ReturnsAsync(targetUser);
        _signInManagerMock.Setup(sm => sm.SignOutAsync())
            .Returns(Task.CompletedTask);
        _signInManagerMock.Setup(sm => sm.SignInAsync(targetUser, true, null))
            .Returns(Task.CompletedTask);

        var reason = "Switching to staff account";

        // Act
        var result = await _service.SwitchToAccountAsync(currentUserId, targetUserId, reason);

        // Assert
        Assert.True(result);
        _signInManagerMock.Verify(sm => sm.SignOutAsync(), Times.Once);
        _signInManagerMock.Verify(sm => sm.SignInAsync(targetUser, true, null), Times.Once);
        _auditServiceMock.Verify(a => a.LogAccountSwitchAsync(
            currentUserId,
            targetUserId,
            reason,
            It.IsAny<string>(),
            It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task SwitchToAccountAsync_WithInactiveTarget_ShouldFailWithoutSigningIn()
    {
        // Arrange
        var personId = Guid.NewGuid();
        var currentUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var currentUser = new ApplicationUser
        {
            Id = currentUserId,
            UserName = "active.current@example.com",
            PersonId = personId,
            IsActive = true
        };
        var targetUser = new ApplicationUser
        {
            Id = targetUserId,
            UserName = "inactive.target@example.com",
            PersonId = personId,
            IsActive = false
        };

        _userManagerMock.Setup(manager => manager.FindByIdAsync(currentUserId.ToString()))
            .ReturnsAsync(currentUser);
        _userManagerMock.Setup(manager => manager.FindByIdAsync(targetUserId.ToString()))
            .ReturnsAsync(targetUser);
        _loginServiceMock
            .Setup(service => service.ValidateExternalUserSignInAsync(
                targetUser,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginResult.UserInactive());
        _signInManagerMock.Setup(manager => manager.SignOutAsync())
            .Returns(Task.CompletedTask);
        _signInManagerMock.Setup(manager => manager.SignInAsync(targetUser, true, null))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.SwitchToAccountAsync(
            currentUserId,
            targetUserId,
            "Attempting switch to inactive target");

        // Assert
        Assert.False(result);
        _signInManagerMock.Verify(manager => manager.SignOutAsync(), Times.Never);
        _signInManagerMock.Verify(
            manager => manager.SignInAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<bool>(),
                It.IsAny<string?>()),
            Times.Never);
        _auditServiceMock.Verify(
            service => service.LogAccountSwitchAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task SwitchToAccountAsync_WithPersonIneligibleTarget_ShouldFailWithoutSigningIn()
    {
        // Arrange
        var (currentUser, targetUser) = ArrangeSamePersonSwitch();
        _loginServiceMock
            .Setup(service => service.ValidateExternalUserSignInAsync(
                targetUser,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginResult.PersonInactive("Person is suspended"));

        // Act
        var result = await _service.SwitchToAccountAsync(
            currentUser.Id,
            targetUser.Id,
            "Attempting switch to Person-ineligible target");

        // Assert
        Assert.False(result);
        VerifyNoSwitchSideEffects();
    }

    [Fact]
    public async Task SwitchToAccountAsync_WithLockedOutTarget_ShouldFailWithoutSigningIn()
    {
        // Arrange
        var (currentUser, targetUser) = ArrangeSamePersonSwitch();
        _loginServiceMock
            .Setup(service => service.ValidateExternalUserSignInAsync(
                targetUser,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginResult.LockedOut());

        // Act
        var result = await _service.SwitchToAccountAsync(
            currentUser.Id,
            targetUser.Id,
            "Attempting switch to locked target");

        // Assert
        Assert.False(result);
        VerifyNoSwitchSideEffects();
    }

    [Fact]
    public async Task SwitchToAccountAsync_WithIneligibleCurrentSessionUser_ShouldFailWithoutSigningIn()
    {
        // Arrange
        var (currentUser, targetUser) = ArrangeSamePersonSwitch();
        _loginServiceMock
            .Setup(service => service.ValidateExternalUserSignInAsync(
                currentUser,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginResult.UserInactive());

        // Act
        var result = await _service.SwitchToAccountAsync(
            currentUser.Id,
            targetUser.Id,
            "Attempting switch from stale inactive session");

        // Assert
        Assert.False(result);
        VerifyNoSwitchSideEffects();
        _loginServiceMock.Verify(
            service => service.ValidateExternalUserSignInAsync(
                targetUser,
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SwitchToAccountAsync_WithIdentityPolicyDeniedTarget_ShouldFailWithoutSigningIn()
    {
        // Arrange
        var (currentUser, targetUser) = ArrangeSamePersonSwitch();
        _signInManagerMock
            .Setup(manager => manager.CanSignInAsync(targetUser))
            .ReturnsAsync(false);

        // Act
        var result = await _service.SwitchToAccountAsync(
            currentUser.Id,
            targetUser.Id,
            "Attempting switch to Identity-ineligible target");

        // Assert
        Assert.False(result);
        VerifyNoSwitchSideEffects();
    }

    [Fact]
    public async Task SwitchToAccountAsync_WithMfaEnabledTargetAndPasswordOnlySession_ShouldFail()
    {
        // Arrange
        var (currentUser, targetUser) = ArrangeSamePersonSwitch();
        targetUser.TwoFactorEnabled = true;

        // Act
        var result = await _service.SwitchToAccountAsync(
            currentUser.Id,
            targetUser.Id,
            "Attempting switch without session MFA");

        // Assert
        Assert.False(result);
        VerifyNoSwitchSideEffects();
    }

    [Fact]
    public async Task SwitchToAccountAsync_WithMfaEnabledTargetAndMfaSession_ShouldSucceed()
    {
        // Arrange
        var (currentUser, targetUser) = ArrangeSamePersonSwitch();
        targetUser.EmailMfaEnabled = true;
        _httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(AuthConstants.ClaimTypes.Amr, AuthConstants.Amr.Mfa)],
            "Test"));
        SetupSuccessfulSwitch(targetUser);

        // Act
        var result = await _service.SwitchToAccountAsync(
            currentUser.Id,
            targetUser.Id,
            "Switching with session MFA");

        // Assert
        Assert.True(result);
        _signInManagerMock.Verify(manager => manager.SignOutAsync(), Times.Once);
        _signInManagerMock.Verify(
            manager => manager.SignInAsync(targetUser, true, null),
            Times.Once);
        _auditServiceMock.Verify(
            service => service.LogAccountSwitchAsync(
                currentUser.Id,
                targetUser.Id,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task SwitchToAccountAsync_WithExpiredMandatoryMfaGrace_ShouldFail()
    {
        // Arrange
        var (currentUser, targetUser) = ArrangeSamePersonSwitch();
        targetUser.MfaRequirementNotifiedAt = DateTime.UtcNow.AddDays(-2);
        _securityPolicyServiceMock
            .Setup(service => service.GetCurrentPolicyAsync())
            .ReturnsAsync(new SecurityPolicy
            {
                EnforceMandatoryMfaEnrollment = true,
                MfaEnforcementGracePeriodDays = 1
            });
        _passkeyServiceMock
            .Setup(service => service.GetUserPasskeysAsync(
                targetUser.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _service.SwitchToAccountAsync(
            currentUser.Id,
            targetUser.Id,
            "Attempting switch after mandatory MFA grace expired");

        // Assert
        Assert.False(result);
        VerifyNoSwitchSideEffects();
    }

    [Fact]
    public async Task SwitchToAccountAsync_WithActiveMandatoryMfaGrace_ShouldSucceed()
    {
        // Arrange
        var (currentUser, targetUser) = ArrangeSamePersonSwitch();
        targetUser.MfaRequirementNotifiedAt = DateTime.UtcNow;
        _securityPolicyServiceMock
            .Setup(service => service.GetCurrentPolicyAsync())
            .ReturnsAsync(new SecurityPolicy
            {
                EnforceMandatoryMfaEnrollment = true,
                MfaEnforcementGracePeriodDays = 1
            });
        _passkeyServiceMock
            .Setup(service => service.GetUserPasskeysAsync(
                targetUser.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        SetupSuccessfulSwitch(targetUser);

        // Act
        var result = await _service.SwitchToAccountAsync(
            currentUser.Id,
            targetUser.Id,
            "Switching during mandatory MFA grace");

        // Assert
        Assert.True(result);
        _signInManagerMock.Verify(manager => manager.SignInAsync(targetUser, true, null), Times.Once);
    }

    [Fact]
    public async Task SwitchToAccountAsync_WithDifferentPersonId_ShouldFail()
    {
        // Arrange: Two accounts belonging to different persons
        var person1Id = Guid.NewGuid();
        var person2Id = Guid.NewGuid();
        var currentUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var memberRoleId = Guid.NewGuid();

        var person1 = new Person
        {
            Id = person1Id,
            FirstName = "Isaac",
            LastName = "User1",
            Birthdate = "1989-10-22",
            Locale = "en-US"
        };

        var person2 = new Person
        {
            Id = person2Id,
            FirstName = "Julia",
            LastName = "User2",
            Birthdate = "1994-03-08",
            Locale = "en-US"
        };

        var currentUser = new ApplicationUser
        {
            Id = currentUserId,
            UserName = "isaac@example.com",
            Email = "isaac@example.com",
            PersonId = person1Id,
            Person = person1
        };

        var targetUser = new ApplicationUser
        {
            Id = targetUserId,
            UserName = "julia@example.com",
            Email = "julia@example.com",
            PersonId = person2Id,
            Person = person2
        };

        var memberRole = new ApplicationRole { Id = memberRoleId, Name = "Member", NormalizedName = "MEMBER" };

        _db.Persons.AddRange(person1, person2);
        _db.Users.AddRange(currentUser, targetUser);
        _db.Roles.Add(memberRole);
        _db.UserRoles.AddRange(
            new IdentityUserRole<Guid> { UserId = currentUserId, RoleId = memberRoleId },
            new IdentityUserRole<Guid> { UserId = targetUserId, RoleId = memberRoleId }
        );
        await _db.SaveChangesAsync();

        // Mock UserManager
        _userManagerMock.Setup(um => um.FindByIdAsync(currentUserId.ToString()))
            .ReturnsAsync(currentUser);
        _userManagerMock.Setup(um => um.FindByIdAsync(targetUserId.ToString()))
            .ReturnsAsync(targetUser);

        // Act
        var result = await _service.SwitchToAccountAsync(currentUserId, targetUserId, "Attempting unauthorized switch");

        // Assert
        Assert.False(result); // Should fail - different PersonId
        _signInManagerMock.Verify(sm => sm.SignOutAsync(), Times.Never); // Should not sign out
        _auditServiceMock.Verify(a => a.LogAccountSwitchAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()),
            Times.Never); // Should not log audit
    }

    [Fact]
    public async Task SwitchToAccountAsync_WithNonExistentTargetUser_ShouldFail()
    {
        // Arrange: Target user doesn't exist
        var currentUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var personId = Guid.NewGuid();

        var person = new Person
        {
            Id = personId,
            FirstName = "Kevin",
            LastName = "Alone",
            Birthdate = "1990-12-01",
            Locale = "en-US"
        };

        var currentUser = new ApplicationUser
        {
            Id = currentUserId,
            UserName = "kevin@example.com",
            Email = "kevin@example.com",
            PersonId = personId,
            Person = person
        };

        _db.Persons.Add(person);
        _db.Users.Add(currentUser);
        await _db.SaveChangesAsync();

        // Mock UserManager (target user not found)
        _userManagerMock.Setup(um => um.FindByIdAsync(currentUserId.ToString()))
            .ReturnsAsync(currentUser);
        _userManagerMock.Setup(um => um.FindByIdAsync(targetUserId.ToString()))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var result = await _service.SwitchToAccountAsync(currentUserId, targetUserId, "Switching to non-existent user");

        // Assert
        Assert.False(result);
        _signInManagerMock.Verify(sm => sm.SignOutAsync(), Times.Never);
    }

    private (ApplicationUser CurrentUser, ApplicationUser TargetUser) ArrangeSamePersonSwitch()
    {
        var personId = Guid.NewGuid();
        var currentUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "current@example.com",
            PersonId = personId,
            IsActive = true
        };
        var targetUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "target@example.com",
            PersonId = personId,
            IsActive = true
        };

        _userManagerMock.Setup(manager => manager.FindByIdAsync(currentUser.Id.ToString()))
            .ReturnsAsync(currentUser);
        _userManagerMock.Setup(manager => manager.FindByIdAsync(targetUser.Id.ToString()))
            .ReturnsAsync(targetUser);

        return (currentUser, targetUser);
    }

    private void SetupSuccessfulSwitch(ApplicationUser targetUser)
    {
        _signInManagerMock.Setup(manager => manager.SignOutAsync())
            .Returns(Task.CompletedTask);
        _signInManagerMock.Setup(manager => manager.SignInAsync(targetUser, true, null))
            .Returns(Task.CompletedTask);
    }

    private void VerifyNoSwitchSideEffects()
    {
        _signInManagerMock.Verify(manager => manager.SignOutAsync(), Times.Never);
        _signInManagerMock.Verify(
            manager => manager.SignInAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<bool>(),
                It.IsAny<string?>()),
            Times.Never);
        _auditServiceMock.Verify(
            service => service.LogAccountSwitchAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }
}
