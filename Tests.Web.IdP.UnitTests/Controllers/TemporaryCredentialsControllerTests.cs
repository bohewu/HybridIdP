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

public sealed class TemporaryCredentialsControllerTests
{
    [Fact]
    public async Task IssueAsync_SuccessReturnsSecretOnceWithNoStoreHeaders()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var service = new Mock<IAdminTemporaryCredentialService>();
        service.Setup(candidate => candidate.IssueAsync(
                It.Is<AdminTemporaryCredentialRequest>(request =>
                    request.ActorAccountId == actorId && request.TargetAccountId == targetId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminTemporaryCredentialResult(RecoveryProofOutcome.Success, "one-time-value"));
        var controller = CreateController(service.Object, actorId);

        var result = await controller.IssueAsync(
            targetId,
            new TemporaryCredentialRequest("identity check", "requested assistance"));

        var response = JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Contains("one-time-value", response);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);
        Assert.Equal("no-cache", controller.Response.Headers.Pragma);
    }

    [Fact]
    public void IssueAsync_UsesExistingAdminSecurityControls()
    {
        var type = typeof(TemporaryCredentialsController);
        var method = type.GetMethod(nameof(TemporaryCredentialsController.IssueAsync))!;
        Assert.NotNull(type.GetCustomAttributes(typeof(ApiAuthorizeAttribute), true).SingleOrDefault());
        Assert.NotNull(type.GetCustomAttributes(typeof(ValidateCsrfForCookiesAttribute), true).SingleOrDefault());
        Assert.Equal(Permissions.Users.Update,
            Assert.IsType<HasPermissionAttribute>(method.GetCustomAttributes(typeof(HasPermissionAttribute), true).Single()).Policy);
        Assert.Equal("login",
            Assert.IsType<EnableRateLimitingAttribute>(method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).Single()).PolicyName);
    }

    private static TemporaryCredentialsController CreateController(
        IAdminTemporaryCredentialService service,
        Guid actorId)
    {
        var controller = new TemporaryCredentialsController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, actorId.ToString())],
                        "test"))
                }
            }
        };
        return controller;
    }
}
