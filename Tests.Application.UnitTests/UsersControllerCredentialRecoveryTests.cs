using System.Security.Claims;
using System.Text.Json;
using Core.Application;
using Core.Application.Options;
using Core.Application.Ports;
using Core.Domain;
using Infrastructure.Authorization;
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

public sealed class UsersControllerCredentialRecoveryTests
{
    [Fact]
    public async Task ReplaceRecoveryEmail_UsesCurrentActorAndSanitizesTargetFailure()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var assistance = new Mock<IRecoveryAssistanceService>(MockBehavior.Strict);
        assistance.Setup(service => service.ReplaceRecoveryEmailAsync(
                It.Is<AdminRecoveryEmailReplacementRequest>(request =>
                    request.ActorAccountId == actorId &&
                    request.TargetAccountId == targetId &&
                    request.CandidateAddress == "candidate@example.test"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecoveryEmailChangeResult(RecoveryProofOutcome.Missing));
        var controller = CreateController(actorId, assistance.Object);

        var result = await controller.ReplaceCredentialRecoveryEmail(
            targetId,
            new AdminRecoveryEmailReplacementApiRequest(
                "candidate@example.test",
                "identity-ticket",
                "assistance reason"));

        var rejected = Assert.IsType<BadRequestObjectResult>(result);
        var response = JsonSerializer.Serialize(rejected.Value);
        Assert.Contains("unavailable", response);
        Assert.DoesNotContain("candidate@example.test", response);
        Assert.DoesNotContain("identity-ticket", response);
        Assert.DoesNotContain("assistance reason", response);
    }

    [Fact]
    public async Task ApproveReset_ServiceRejectsCurrentSessionAssurance_ReturnsUsableOutcome()
    {
        var actorId = Guid.NewGuid();
        var assistance = new Mock<IRecoveryAssistanceService>(MockBehavior.Strict);
        assistance.Setup(service => service.IssueResetApprovalAsync(
                It.IsAny<AdminResetApprovalRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResetApprovalIssueResult(RecoveryProofOutcome.Unauthorized));
        var controller = CreateController(actorId, assistance.Object);

        var result = await controller.ApproveCredentialRecoveryReset(
            Guid.NewGuid(),
            new AdminResetApprovalApiRequest("identity-ticket", "assistance reason"));

        var denied = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, denied.StatusCode);
        Assert.Contains("highAssuranceRequired", JsonSerializer.Serialize(denied.Value));
    }

    [Fact]
    public async Task ResendNativeRecoveryOtp_UsesCurrentActorAndSanitizesTargetFailure()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var migrationAssistance = new Mock<IRecoveryAssistanceService>(MockBehavior.Strict);
        var nativeAssistance = new Mock<INativeRecoveryAssistanceService>(MockBehavior.Strict);
        nativeAssistance.Setup(service => service.ResendAsync(
                It.Is<AdminNativeRecoveryResendRequest>(request =>
                    request.ActorAccountId == actorId && request.TargetAccountId == targetId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NativeRecoveryAssistanceResult(RecoveryProofOutcome.Unavailable));
        var controller = CreateController(actorId, migrationAssistance.Object, nativeAssistance.Object);

        var result = await controller.ResendNativeRecoveryOtp(targetId);

        var rejected = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("unavailable", JsonSerializer.Serialize(rejected.Value));
    }

    private static UsersController CreateController(
        Guid actorId,
        IRecoveryAssistanceService assistance,
        INativeRecoveryAssistanceService? nativeAssistance = null)
    {
        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
        var roleManager = new Mock<RoleManager<ApplicationRole>>(
            Mock.Of<IRoleStore<ApplicationRole>>(),
            null,
            null,
            null,
            null);
        var controller = new UsersController(
            Mock.Of<IUserManagementService>(),
            userManager.Object,
            roleManager.Object,
            Mock.Of<ISessionService>(),
            Mock.Of<ILoginHistoryService>(),
            Mock.Of<IApplicationDbContext>(),
            Mock.Of<IStringLocalizer<SharedResource>>(),
            Mock.Of<IImpersonationService>(),
            Mock.Of<AspNetCoreAuthorizationService>(),
            Options.Create(new PrivilegedRoleProtectionOptions()),
            Mock.Of<ILogger<UsersController>>(),
            assistance,
            nativeAssistance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, actorId.ToString())],
                    "Identity.Application"))
            }
        };
        return controller;
    }
}
