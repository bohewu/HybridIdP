using System.Security.Claims;
using System.Text.Json;
using Core.Application.Ports;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Web.IdP.Attributes;
using Web.IdP.Controllers.Admin;
using Xunit;

namespace Tests.Web.IdP.UnitTests.Controllers;

public sealed class RecoveryAssistanceContextControllerTests
{
    [Fact]
    public async Task GetAsync_AuthorizedActor_ReturnsSanitizedNoStoreContext()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var context = new RecoveryAssistanceContextDto(
            new("available", true, true, true),
            new("disabled", false),
            new("unavailable", false, false, false),
            new("unavailable", false, false));
        var service = new Mock<IRecoveryAssistanceContextService>();
        service.Setup(candidate => candidate.GetAsync(actorId, targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecoveryAssistanceContextResult(
                RecoveryAssistanceContextOutcome.Available,
                context));
        var controller = CreateController(service.Object, actorId);

        var result = await controller.GetAsync(targetId);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(context, ok.Value);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);
        Assert.Equal("no-cache", controller.Response.Headers.Pragma);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.DoesNotContain(actorId.ToString(), json);
        Assert.DoesNotContain(targetId.ToString(), json);
        Assert.DoesNotContain("attemptId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("directoryObjectId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evidence", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAsync_MissingActor_ReturnsUnauthorizedWithoutServiceCall()
    {
        var service = new Mock<IRecoveryAssistanceContextService>(MockBehavior.Strict);
        var controller = CreateController(service.Object, null);

        var result = await controller.GetAsync(Guid.NewGuid());

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task GetAsync_LostHighAssurance_ReturnsForbidden()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var service = new Mock<IRecoveryAssistanceContextService>();
        service.Setup(candidate => candidate.GetAsync(actorId, targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecoveryAssistanceContextResult(RecoveryAssistanceContextOutcome.Unauthorized));
        var controller = CreateController(service.Object, actorId);

        var result = await controller.GetAsync(targetId);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public void Controller_PreservesUsersUpdateAndCookieCsrfGuards()
    {
        var controllerType = typeof(RecoveryAssistanceContextController);
        Assert.NotNull(controllerType.GetCustomAttributes(typeof(ApiAuthorizeAttribute), true).SingleOrDefault());
        Assert.NotNull(controllerType.GetCustomAttributes(typeof(ValidateCsrfForCookiesAttribute), true).SingleOrDefault());
        var method = controllerType.GetMethod(nameof(RecoveryAssistanceContextController.GetAsync))!;
        var permission = Assert.Single(method.GetCustomAttributes(typeof(HasPermissionAttribute), true)
            .Cast<HasPermissionAttribute>());
        Assert.Equal(Permissions.Users.Update, permission.Policy);
    }

    private static RecoveryAssistanceContextController CreateController(
        IRecoveryAssistanceContextService service,
        Guid? actorId)
    {
        var claims = actorId is { } id
            ? new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()) }
            : [];
        var controller = new RecoveryAssistanceContextController(service)
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
