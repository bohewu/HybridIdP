using System.Security.Claims;
using System.Text.Json;
using Core.Application.Ports;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Moq;
using Web.IdP.Attributes;
using Web.IdP.Controllers.Admin;

namespace Tests.Web.IdP.UnitTests.Controllers;

public sealed class DirectoryRecoveryReconciliationControllerTests
{
    [Fact]
    public async Task ReconcileAsync_LegacyAdministratorSecretShape_IsDeniedWithoutServiceCall()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        const string intendedPassword = "test-intended-password";
        var service = new Mock<INativeDirectoryRecoveryReconciliationService>();
        var controller = CreateController(service.Object, actorId);

        var result = await controller.ReconcileAsync(
            targetId,
            new DirectoryRecoveryReconciliationRequest(intendedPassword));

        var denied = Assert.IsType<BadRequestObjectResult>(result);
        var response = JsonSerializer.Serialize(denied.Value);
        Assert.Contains("unavailable", response);
        Assert.DoesNotContain(intendedPassword, response);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetPendingDirectoryOperationAsync_ReturnsExactAttemptBinding()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var attempt = new DirectoryCredentialOperatorResolutionAttempt(
            Guid.NewGuid(),
            2,
            Core.Domain.Entities.NativeDirectoryCredentialOperationKind.AdminTemporaryIssue,
            Core.Domain.Entities.NativeDirectoryRecoveryStatus.ReconciliationRequired,
            Guid.NewGuid());
        var operatorService = new Mock<IDirectoryCredentialOperatorResolutionService>();
        operatorService.Setup(service => service.GetPendingAsync(
                actorId, targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryCredentialOperatorResolutionLookupResult(
                DirectoryCredentialOperatorResolutionOutcome.Available,
                attempt));
        var controller = CreateController(
            Mock.Of<INativeDirectoryRecoveryReconciliationService>(),
            actorId,
            operatorService.Object);

        var result = await controller.GetPendingDirectoryOperationAsync(targetId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = JsonSerializer.Serialize(ok.Value);
        Assert.Contains(attempt.AttemptId.ToString(), response);
        Assert.Contains("AdminTemporaryIssue", response);
        Assert.Contains("ReconciliationRequired", response);
        Assert.Contains(attempt.DirectoryObjectId.ToString(), response);
    }

    [Fact]
    public async Task ResolveDirectoryOperationAsync_LegacyAdministratorCredentialShape_IsDeniedWithoutServiceCall()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        const string currentCredential = "current-directory-credential";
        var apiRequest = new DirectoryCredentialOperatorResolutionApiRequest(
            Guid.NewGuid(),
            2,
            "RequiredChange",
            "ReconciliationRequired",
            Guid.NewGuid(),
            currentCredential,
            true,
            true,
            "verified support channel",
            "recover uncertain required change");
        var operatorService = new Mock<IDirectoryCredentialOperatorResolutionService>();
        var controller = CreateController(
            Mock.Of<INativeDirectoryRecoveryReconciliationService>(),
            actorId,
            operatorService.Object);

        var result = await controller.ResolveDirectoryOperationAsync(targetId, apiRequest);

        var denied = Assert.IsType<BadRequestObjectResult>(result);
        var response = JsonSerializer.Serialize(denied.Value);
        Assert.DoesNotContain(currentCredential, response);
        operatorService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PrepareDirectorySettlementAsync_ReturnsPreparationIdUsableByCancel()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var preparationId = Guid.NewGuid();
        var request = new DirectorySettlementPreparationApiRequest(
            Guid.NewGuid(),
            3,
            "AdminTemporaryIssue",
            "ReconciliationRequired",
            Guid.NewGuid(),
            true,
            "OriginalOperationSettled",
            "ApprovedDirectoryOperation",
            "CASE:20260911-002");
        var operatorService = new Mock<IDirectoryCredentialOperatorResolutionService>();
        operatorService.Setup(service => service.PrepareAsync(
                It.Is<DirectorySettlementPreparationRequest>(candidate =>
                    candidate.ActorAccountId == actorId && candidate.LocalAccountId == targetId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectorySettlementPreparationResult(
                DirectoryCredentialOperatorResolutionOutcome.Available,
                preparationId));
        operatorService.Setup(service => service.CancelAsync(
                actorId, preparationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DirectoryCredentialOperatorResolutionOutcome.Resolved);
        var controller = CreateController(
            Mock.Of<INativeDirectoryRecoveryReconciliationService>(),
            actorId,
            operatorService.Object);

        var prepareResult = await controller.PrepareDirectorySettlementAsync(targetId, request);

        var prepared = Assert.IsType<OkObjectResult>(prepareResult);
        using var response = JsonDocument.Parse(JsonSerializer.Serialize(prepared.Value));
        var returnedPreparationId = response.RootElement.GetProperty("preparationId").GetGuid();
        var cancelResult = await controller.CancelDirectorySettlementAsync(returnedPreparationId);

        Assert.Equal(preparationId, returnedPreparationId);
        Assert.IsType<OkObjectResult>(cancelResult);
        operatorService.Verify(service => service.CancelAsync(
            actorId, preparationId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReconcileAsync_MissingActor_LegacyShapeStillDoesNotCallService()
    {
        var service = new Mock<INativeDirectoryRecoveryReconciliationService>();
        var controller = CreateController(service.Object, null);

        var result = await controller.ReconcileAsync(
            Guid.NewGuid(),
            new DirectoryRecoveryReconciliationRequest("test-intended-password"));

        Assert.IsType<BadRequestObjectResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public void ReconcileAsync_UsesExistingAdminSecurityControls()
    {
        var controllerType = typeof(DirectoryRecoveryReconciliationController);
        var method = controllerType.GetMethod(nameof(DirectoryRecoveryReconciliationController.ReconcileAsync))!;

        Assert.NotNull(controllerType.GetCustomAttributes(typeof(ApiAuthorizeAttribute), true).SingleOrDefault());
        Assert.NotNull(controllerType.GetCustomAttributes(typeof(ValidateCsrfForCookiesAttribute), true).SingleOrDefault());
        Assert.Equal(
            Permissions.Users.Update,
            Assert.IsType<HasPermissionAttribute>(
                method.GetCustomAttributes(typeof(HasPermissionAttribute), true).Single()).Policy);
        Assert.Equal(
            "login",
            Assert.IsType<EnableRateLimitingAttribute>(
                method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).Single()).PolicyName);

        foreach (var operatorMethodName in new[]
                 {
                     nameof(DirectoryRecoveryReconciliationController.GetPendingDirectoryOperationAsync),
                     nameof(DirectoryRecoveryReconciliationController.ResolveDirectoryOperationAsync)
                 })
        {
            var operatorMethod = controllerType.GetMethod(operatorMethodName)!;
            Assert.Equal(
                Permissions.Users.Update,
                Assert.IsType<HasPermissionAttribute>(
                    operatorMethod.GetCustomAttributes(typeof(HasPermissionAttribute), true).Single()).Policy);
            Assert.Equal(
                "login",
                Assert.IsType<EnableRateLimitingAttribute>(
                    operatorMethod.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).Single()).PolicyName);
        }
    }

    private static DirectoryRecoveryReconciliationController CreateController(
        INativeDirectoryRecoveryReconciliationService service,
        Guid? actorId,
        IDirectoryCredentialOperatorResolutionService? operatorService = null)
    {
        var claims = actorId is null
            ? Array.Empty<Claim>()
            : new[] { new Claim(ClaimTypes.NameIdentifier, actorId.Value.ToString()) };
        var controller = new DirectoryRecoveryReconciliationController(
            operatorService ?? Mock.Of<IDirectoryCredentialOperatorResolutionService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
                }
            }
        };
        return controller;
    }
}
