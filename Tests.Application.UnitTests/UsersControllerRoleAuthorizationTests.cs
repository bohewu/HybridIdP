using System.Security.Claims;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Options;
using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Web.IdP;
using Web.IdP.Controllers.Admin;
using Web.IdP.Services;
using AspNetCoreAuthorizationService = Microsoft.AspNetCore.Authorization.IAuthorizationService;

namespace Tests.Application.UnitTests;

public class UsersControllerRoleAuthorizationTests
{
    [Fact]
    public async Task UpdateUser_ShouldForbidRoleChange_WhenCallerLacksRolesUpdate()
    {
        var userManagementService = new Mock<IUserManagementService>();
        var targetUserId = Guid.NewGuid();
        var existingUser = new UserDetailDto
        {
            Id = targetUserId,
            Email = "target@example.test",
            Roles = ["User"]
        };

        userManagementService
            .Setup(service => service.UpdateUserAsync(
                targetUserId,
                It.IsAny<UpdateUserDto>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, Array.Empty<string>()));
        userManagementService
            .Setup(service => service.GetUserByIdAsync(targetUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);

        var authorizationService = new Mock<AspNetCoreAuthorizationService>();
        authorizationService
            .Setup(service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                null,
                Permissions.Roles.Update))
            .ReturnsAsync(AuthorizationResult.Failed());
        var controller = CreateController(userManagementService, authorizationService);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = CreatePrincipal(Permissions.Users.Update)
            }
        };

        var request = new UpdateUserDto
        {
            Email = existingUser.Email,
            IsActive = true,
            Roles = ["User", "Auditor"]
        };

        var result = await controller.UpdateUser(targetUserId, request);

        Assert.IsType<ForbidResult>(result);
        userManagementService.Verify(
            service => service.UpdateUserAsync(
                It.IsAny<Guid>(),
                It.IsAny<UpdateUserDto>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateUser_ShouldPreserveMetadataUpdate_WhenRolesAreUnchanged()
    {
        var userManagementService = new Mock<IUserManagementService>();
        var authorizationService = new Mock<AspNetCoreAuthorizationService>();
        var targetUserId = Guid.NewGuid();
        var existingUser = new UserDetailDto
        {
            Id = targetUserId,
            Email = "target@example.test",
            Roles = ["User"]
        };

        userManagementService
            .Setup(service => service.GetUserByIdAsync(targetUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);
        userManagementService
            .Setup(service => service.UpdateUserWithoutRolesAsync(
                targetUserId,
                It.IsAny<UpdateUserDto>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, Array.Empty<string>()));

        var controller = CreateController(userManagementService, authorizationService);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = CreatePrincipal(Permissions.Users.Update)
            }
        };

        var request = new UpdateUserDto
        {
            Email = "renamed@example.test",
            IsActive = true,
            Roles = ["User"]
        };

        var result = await controller.UpdateUser(targetUserId, request);

        Assert.IsType<OkObjectResult>(result);
        userManagementService.Verify(
            service => service.UpdateUserWithoutRolesAsync(
                targetUserId,
                request,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        authorizationService.Verify(
            service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                null,
                Permissions.Roles.Update),
            Times.Never);
    }

    [Fact]
    public async Task UpdateUser_ShouldAllowRoleChange_WhenCallerHasRolesUpdate()
    {
        var userManagementService = new Mock<IUserManagementService>();
        var authorizationService = new Mock<AspNetCoreAuthorizationService>();
        var targetUserId = Guid.NewGuid();
        var existingUser = new UserDetailDto
        {
            Id = targetUserId,
            Email = "target@example.test",
            Roles = ["User"]
        };

        userManagementService
            .Setup(service => service.GetUserByIdAsync(targetUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);
        userManagementService
            .Setup(service => service.UpdateUserAsync(
                targetUserId,
                It.IsAny<UpdateUserDto>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, Array.Empty<string>()));
        authorizationService
            .Setup(service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                null,
                Permissions.Roles.Update))
            .ReturnsAsync(AuthorizationResult.Success());

        var controller = CreateController(userManagementService, authorizationService);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = CreatePrincipal(Permissions.Users.Update, Permissions.Roles.Update)
            }
        };

        var request = new UpdateUserDto
        {
            Email = existingUser.Email,
            IsActive = true,
            Roles = ["User", "Auditor"]
        };

        var result = await controller.UpdateUser(targetUserId, request);

        Assert.IsType<OkObjectResult>(result);
        userManagementService.Verify(
            service => service.UpdateUserAsync(
                targetUserId,
                request,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AssignRoles_ShouldForbidRoleChange_WhenCallerLacksRolesUpdate()
    {
        var userManagementService = new Mock<IUserManagementService>();
        var authorizationService = new Mock<AspNetCoreAuthorizationService>();
        var targetUserId = Guid.NewGuid();
        userManagementService
            .Setup(service => service.GetUserByIdAsync(targetUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDetailDto
            {
                Id = targetUserId,
                Email = "target@example.test",
                Roles = ["User"]
            });
        authorizationService
            .Setup(service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                null,
                Permissions.Roles.Update))
            .ReturnsAsync(AuthorizationResult.Failed());

        var controller = CreateController(userManagementService, authorizationService);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = CreatePrincipal(Permissions.Users.Update)
            }
        };

        var result = await controller.AssignRoles(
            targetUserId,
            new UsersController.AssignRolesRequest(["User", "Auditor"]));

        Assert.IsType<ForbidResult>(result);
        userManagementService.Verify(
            service => service.AssignRolesAsync(
                It.IsAny<Guid>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AssignRoles_ShouldForbidRemovingAllRoles_WhenCallerLacksRolesUpdate()
    {
        var userManagementService = new Mock<IUserManagementService>();
        var authorizationService = new Mock<AspNetCoreAuthorizationService>();
        var targetUserId = Guid.NewGuid();
        userManagementService
            .Setup(service => service.GetUserByIdAsync(targetUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDetailDto
            {
                Id = targetUserId,
                Email = "target@example.test",
                Roles = ["User"]
            });
        authorizationService
            .Setup(service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                null,
                Permissions.Roles.Update))
            .ReturnsAsync(AuthorizationResult.Failed());

        var controller = CreateController(userManagementService, authorizationService);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = CreatePrincipal(Permissions.Users.Update)
            }
        };

        var result = await controller.AssignRoles(
            targetUserId,
            new UsersController.AssignRolesRequest([]));

        Assert.IsType<ForbidResult>(result);
        userManagementService.Verify(
            service => service.AssignRolesAsync(
                It.IsAny<Guid>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateUser_ShouldAllowEmptyRoleSet_WithoutRolesUpdate()
    {
        var userManagementService = new Mock<IUserManagementService>();
        var authorizationService = new Mock<AspNetCoreAuthorizationService>();
        var createdUserId = Guid.NewGuid();
        var createdUser = new UserDetailDto
        {
            Id = createdUserId,
            Email = "new@example.test",
            Roles = []
        };
        userManagementService
            .Setup(service => service.CreateUserAsync(
                It.IsAny<CreateUserDto>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, createdUserId, Array.Empty<string>()));
        userManagementService
            .Setup(service => service.GetUserByIdAsync(createdUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdUser);

        var controller = CreateController(userManagementService, authorizationService);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = CreatePrincipal(Permissions.Users.Create)
            }
        };

        var result = await controller.CreateUser(
            new CreateUserDto
            {
                Email = createdUser.Email,
                UserName = "new-user",
                Password = "test-only-password",
                Roles = []
            });

        Assert.IsType<CreatedAtActionResult>(result);
        authorizationService.Verify(
            service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                null,
                Permissions.Roles.Update),
            Times.Never);
    }

    [Fact]
    public async Task CreateUser_ShouldForbidRoleAssignment_WhenCallerLacksRolesUpdate()
    {
        var userManagementService = new Mock<IUserManagementService>();
        var authorizationService = new Mock<AspNetCoreAuthorizationService>();
        authorizationService
            .Setup(service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                null,
                Permissions.Roles.Update))
            .ReturnsAsync(AuthorizationResult.Failed());

        var controller = CreateController(userManagementService, authorizationService);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = CreatePrincipal(Permissions.Users.Create)
            }
        };

        var result = await controller.CreateUser(
            new CreateUserDto
            {
                Email = "new@example.test",
                UserName = "new-user",
                Password = "test-only-password",
                Roles = ["Auditor"]
            });

        Assert.IsType<ForbidResult>(result);
        userManagementService.Verify(
            service => service.CreateUserAsync(
                It.IsAny<CreateUserDto>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AssignRolesByIds_ShouldForbidRoleChange_WhenCallerLacksRolesUpdate()
    {
        var userManagementService = new Mock<IUserManagementService>();
        var authorizationService = new Mock<AspNetCoreAuthorizationService>();
        var roleStore = new Mock<IRoleStore<ApplicationRole>>();
        var targetUserId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        userManagementService
            .Setup(service => service.GetUserByIdAsync(targetUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDetailDto
            {
                Id = targetUserId,
                Email = "target@example.test",
                Roles = ["User"]
            });
        roleStore
            .Setup(store => store.FindByIdAsync(roleId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationRole
            {
                Id = roleId,
                Name = "Auditor",
                NormalizedName = "AUDITOR"
            });
        authorizationService
            .Setup(service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                null,
                Permissions.Roles.Update))
            .ReturnsAsync(AuthorizationResult.Failed());

        var controller = CreateController(userManagementService, authorizationService, roleStore);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = CreatePrincipal(Permissions.Users.Update)
            }
        };

        var result = await controller.AssignRolesByIds(
            targetUserId,
            new UsersController.AssignRolesByIdRequest([roleId]));

        Assert.IsType<ForbidResult>(result);
        userManagementService.Verify(
            service => service.AssignRolesByIdAsync(
                It.IsAny<Guid>(),
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static UsersController CreateController(
        Mock<IUserManagementService> userManagementService,
        Mock<AspNetCoreAuthorizationService> authorizationService,
        Mock<IRoleStore<ApplicationRole>>? roleStore = null)
    {
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        roleStore ??= new Mock<IRoleStore<ApplicationRole>>();
        var userManager = new UserManager<ApplicationUser>(
            userStore.Object,
            Options.Create(new IdentityOptions()),
            new Mock<IPasswordHasher<ApplicationUser>>().Object,
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new Mock<ILookupNormalizer>().Object,
            new IdentityErrorDescriber(),
            new Mock<IServiceProvider>().Object,
            new Mock<ILogger<UserManager<ApplicationUser>>>().Object);
        var roleManager = new RoleManager<ApplicationRole>(
            roleStore.Object,
            Array.Empty<IRoleValidator<ApplicationRole>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new Mock<ILogger<RoleManager<ApplicationRole>>>().Object);

        return new UsersController(
            userManagementService.Object,
            userManager,
            roleManager,
            new Mock<ISessionService>().Object,
            new Mock<ILoginHistoryService>().Object,
            new Mock<IApplicationDbContext>().Object,
            new Mock<IStringLocalizer<SharedResource>>().Object,
            new Mock<IImpersonationService>().Object,
            authorizationService.Object,
            Options.Create(new PrivilegedRoleProtectionOptions()),
            new Mock<ILogger<UsersController>>().Object);
    }

    private static ClaimsPrincipal CreatePrincipal(params string[] permissions)
    {
        var claims = permissions.Select(permission => new Claim("permission", permission));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }
}
