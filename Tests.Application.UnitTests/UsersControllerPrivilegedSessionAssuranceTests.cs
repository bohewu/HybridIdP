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

public class UsersControllerPrivilegedSessionAssuranceTests
{
    [Theory]
    [InlineData("Identity.Application")]
    [InlineData("OpenIddict.Validation.AspNetCore")]
    public async Task AssignRoles_ShouldRejectPasswordOnlyPrincipal_WhenOperatorIsMfaEnrolled(
        string authenticationType)
    {
        var fixture = CreateAssignmentFixture(
            new PrivilegedRoleProtectionOptions
            {
                RequireOperatorMfaForPrivilegedRoleAssignment = true
            },
            authenticationType,
            AuthConstants.ClaimTypes.Amr,
            AuthConstants.Amr.Password);
        fixture.OperatorUser.TwoFactorEnabled = true;

        var result = await fixture.Controller.AssignRoles(
            fixture.TargetUser.Id,
            new UsersController.AssignRolesRequest([AuthConstants.Roles.Admin]));

        Assert.IsType<BadRequestObjectResult>(result);
        VerifyRoleAssignment(fixture, Times.Never());
    }

    [Theory]
    [InlineData("Identity.Application", AuthConstants.ClaimTypes.Amr)]
    [InlineData("Identity.Application", ClaimTypes.AuthenticationMethod)]
    [InlineData("OpenIddict.Validation.AspNetCore", AuthConstants.ClaimTypes.Amr)]
    [InlineData("OpenIddict.Validation.AspNetCore", ClaimTypes.AuthenticationMethod)]
    public async Task AssignRoles_ShouldAllowPrincipalThatCompletedMfaInCurrentSession(
        string authenticationType,
        string authenticationMethodClaimType)
    {
        var fixture = CreateAssignmentFixture(
            new PrivilegedRoleProtectionOptions
            {
                RequireOperatorMfaForPrivilegedRoleAssignment = true
            },
            authenticationType,
            authenticationMethodClaimType,
            AuthConstants.Amr.Mfa,
            AuthConstants.Amr.Otp);

        var result = await fixture.Controller.AssignRoles(
            fixture.TargetUser.Id,
            new UsersController.AssignRolesRequest([AuthConstants.Roles.Admin]));

        Assert.IsType<OkObjectResult>(result);
        VerifyRoleAssignment(fixture, Times.Once());
    }

    [Fact]
    public async Task AssignRoles_ShouldPreserveExistingBehavior_WhenOperatorMfaProtectionIsDisabled()
    {
        var fixture = CreateAssignmentFixture(
            new PrivilegedRoleProtectionOptions(),
            "Identity.Application",
            AuthConstants.ClaimTypes.Amr,
            AuthConstants.Amr.Password);

        var result = await fixture.Controller.AssignRoles(
            fixture.TargetUser.Id,
            new UsersController.AssignRolesRequest([AuthConstants.Roles.Admin]));

        Assert.IsType<OkObjectResult>(result);
        VerifyRoleAssignment(fixture, Times.Once());
    }

    [Fact]
    public async Task AssignRoles_ShouldNotRequireMfaForUnprotectedRole()
    {
        var fixture = CreateAssignmentFixture(
            new PrivilegedRoleProtectionOptions
            {
                RequireOperatorMfaForPrivilegedRoleAssignment = true
            },
            "Identity.Application",
            AuthConstants.ClaimTypes.Amr,
            AuthConstants.Amr.Password);

        var result = await fixture.Controller.AssignRoles(
            fixture.TargetUser.Id,
            new UsersController.AssignRolesRequest([AuthConstants.Roles.User]));

        Assert.IsType<OkObjectResult>(result);
        VerifyRoleAssignment(fixture, Times.Once());
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    public async Task AssignRoles_ShouldHonorPasskeyMfaConfiguration(
        bool countPasskeyAsMfa,
        bool includeOtp,
        bool expectRejection)
    {
        var authenticationMethods = new List<string>
        {
            AuthConstants.Amr.Mfa,
            AuthConstants.Amr.HardwareKey
        };
        if (includeOtp)
        {
            authenticationMethods.Add(AuthConstants.Amr.Otp);
        }

        var fixture = CreateAssignmentFixture(
            new PrivilegedRoleProtectionOptions
            {
                RequireOperatorMfaForPrivilegedRoleAssignment = true,
                CountPasskeyAsMfa = countPasskeyAsMfa
            },
            "Identity.Application",
            AuthConstants.ClaimTypes.Amr,
            authenticationMethods.ToArray());

        var result = await fixture.Controller.AssignRoles(
            fixture.TargetUser.Id,
            new UsersController.AssignRolesRequest([AuthConstants.Roles.Admin]));

        if (expectRejection)
        {
            Assert.IsType<BadRequestObjectResult>(result);
            VerifyRoleAssignment(fixture, Times.Never());
        }
        else
        {
            Assert.IsType<OkObjectResult>(result);
            VerifyRoleAssignment(fixture, Times.Once());
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task AssignRoles_ShouldPreserveTargetMfaEnrollmentPolicy(
        bool targetHasMfaEnrollment,
        bool expectRejection)
    {
        var fixture = CreateAssignmentFixture(
            new PrivilegedRoleProtectionOptions
            {
                RequireOperatorMfaForPrivilegedRoleAssignment = true,
                RequireTargetMfaForPrivilegedRoleAssignment = true,
                CountPasskeyAsMfa = false
            },
            "Identity.Application",
            AuthConstants.ClaimTypes.Amr,
            AuthConstants.Amr.Mfa,
            AuthConstants.Amr.Otp);
        fixture.TargetUser.TwoFactorEnabled = targetHasMfaEnrollment;

        var result = await fixture.Controller.AssignRoles(
            fixture.TargetUser.Id,
            new UsersController.AssignRolesRequest([AuthConstants.Roles.Admin]));

        if (expectRejection)
        {
            Assert.IsType<BadRequestObjectResult>(result);
            VerifyRoleAssignment(fixture, Times.Never());
        }
        else
        {
            Assert.IsType<OkObjectResult>(result);
            VerifyRoleAssignment(fixture, Times.Once());
        }
    }

    [Fact]
    public async Task CreateUser_ShouldRejectProtectedRole_WhenCurrentSessionDidNotCompleteMfa()
    {
        var operatorUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "operator",
            TwoFactorEnabled = true
        };
        var userManagementService = new Mock<IUserManagementService>();
        var userManager = CreateUserManager();
        userManager
            .Setup(manager => manager.FindByIdAsync(operatorUser.Id.ToString()))
            .ReturnsAsync(operatorUser);
        var authorizationService = CreateRoleUpdateAuthorizationService();
        var controller = CreateController(
            userManagementService,
            userManager,
            authorizationService,
            new PrivilegedRoleProtectionOptions
            {
                RequireOperatorMfaForPrivilegedRoleAssignment = true
            });
        SetPrincipal(
            controller,
            operatorUser.Id,
            "Identity.Application",
            AuthConstants.ClaimTypes.Amr,
            AuthConstants.Amr.Password);

        var result = await controller.CreateUser(new CreateUserDto
        {
            Email = "new-user@example.test",
            UserName = "new-user",
            Password = "Test-only-password-1!",
            Roles = [AuthConstants.Roles.Admin]
        });

        Assert.IsType<BadRequestObjectResult>(result);
        userManagementService.Verify(
            service => service.CreateUserAsync(
                It.IsAny<CreateUserDto>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static AssignmentFixture CreateAssignmentFixture(
        PrivilegedRoleProtectionOptions options,
        string authenticationType,
        string authenticationMethodClaimType,
        params string[] authenticationMethods)
    {
        var operatorUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "operator"
        };
        var targetUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "target"
        };
        var userDetail = new UserDetailDto
        {
            Id = targetUser.Id,
            Email = "target@example.test",
            Roles = [AuthConstants.Roles.Admin]
        };
        var userManagementService = new Mock<IUserManagementService>();
        userManagementService
            .Setup(service => service.AssignRolesAsync(
                targetUser.Id,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, Array.Empty<string>()));
        userManagementService
            .Setup(service => service.GetUserByIdAsync(
                targetUser.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(userDetail);

        var userManager = CreateUserManager();
        userManager
            .Setup(manager => manager.FindByIdAsync(targetUser.Id.ToString()))
            .ReturnsAsync(targetUser);
        userManager
            .Setup(manager => manager.FindByIdAsync(operatorUser.Id.ToString()))
            .ReturnsAsync(operatorUser);

        var controller = CreateController(
            userManagementService,
            userManager,
            CreateRoleUpdateAuthorizationService(),
            options);
        SetPrincipal(
            controller,
            operatorUser.Id,
            authenticationType,
            authenticationMethodClaimType,
            authenticationMethods);

        return new AssignmentFixture(
            controller,
            userManagementService,
            operatorUser,
            targetUser);
    }

    private static UsersController CreateController(
        Mock<IUserManagementService> userManagementService,
        Mock<UserManager<ApplicationUser>> userManager,
        Mock<AspNetCoreAuthorizationService> authorizationService,
        PrivilegedRoleProtectionOptions options)
    {
        var roleManager = new Mock<RoleManager<ApplicationRole>>(
            Mock.Of<IRoleStore<ApplicationRole>>(),
            Array.Empty<IRoleValidator<ApplicationRole>>(),
            Mock.Of<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            Mock.Of<ILogger<RoleManager<ApplicationRole>>>());

        return new UsersController(
            userManagementService.Object,
            userManager.Object,
            roleManager.Object,
            Mock.Of<ISessionService>(),
            Mock.Of<ILoginHistoryService>(),
            Mock.Of<IApplicationDbContext>(),
            Mock.Of<IStringLocalizer<SharedResource>>(),
            Mock.Of<IImpersonationService>(),
            authorizationService.Object,
            Options.Create(options),
            Mock.Of<ILogger<UsersController>>());
    }

    private static Mock<UserManager<ApplicationUser>> CreateUserManager() =>
        new(
            Mock.Of<IUserStore<ApplicationUser>>(),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

    private static Mock<AspNetCoreAuthorizationService> CreateRoleUpdateAuthorizationService()
    {
        var authorizationService = new Mock<AspNetCoreAuthorizationService>();
        authorizationService
            .Setup(service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                null,
                Permissions.Roles.Update))
            .ReturnsAsync(AuthorizationResult.Success());
        return authorizationService;
    }

    private static void SetPrincipal(
        UsersController controller,
        Guid userId,
        string authenticationType,
        string authenticationMethodClaimType,
        params string[] authenticationMethods)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString())
        };
        claims.AddRange(authenticationMethods.Select(
            value => new Claim(authenticationMethodClaimType, value)));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType))
            }
        };
    }

    private static void VerifyRoleAssignment(AssignmentFixture fixture, Times times) =>
        fixture.UserManagementService.Verify(
            service => service.AssignRolesAsync(
                fixture.TargetUser.Id,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()),
            times);

    private sealed record AssignmentFixture(
        UsersController Controller,
        Mock<IUserManagementService> UserManagementService,
        ApplicationUser OperatorUser,
        ApplicationUser TargetUser);
}
