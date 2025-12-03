using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Services;
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
        var claimsFactoryMock = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
        _signInManagerMock = new Mock<SignInManager<ApplicationUser>>(
            _userManagerMock.Object,
            contextAccessorMock.Object,
            claimsFactoryMock.Object,
            null, null, null, null);

        // Mock SessionService and AuditService
        _sessionServiceMock = new Mock<ISessionService>();
        _auditServiceMock = new Mock<IAuditService>();

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
}
