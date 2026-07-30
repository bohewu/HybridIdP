using System.Security.Claims;
using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using OpenIddict.Abstractions;
using Web.IdP.Controllers.Admin;

namespace Tests.Web.IdP.UnitTests.Controllers;

public class ScopesControllerRoleIsolationTests
{
    [Fact]
    public async Task Update_AppRoleAdmin_ShouldNotBypassStandardScopeRestriction()
    {
        var scopeService = new Mock<IScopeService>();
        scopeService
            .Setup(service => service.GetScopeByIdAsync(
                "scope-id",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScopeSummary
            {
                Id = "scope-id",
                Name = OpenIddictConstants.Scopes.OpenId
            });
        var controller = CreateController(scopeService.Object);

        var result = await controller.Update(
            "scope-id",
            new UpdateScopeRequest(
                OpenIddictConstants.Scopes.OpenId,
                "OpenID",
                null,
                null));

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        scopeService.Verify(
            service => service.UpdateScopeAsync(
                It.IsAny<string>(),
                It.IsAny<UpdateScopeRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static ScopesController CreateController(IScopeService scopeService)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim("permission", Permissions.Scopes.Update),
                new Claim("app_role", AuthConstants.Roles.Admin),
                new Claim(ClaimTypes.Role, AuthConstants.Roles.Admin)
            },
            authenticationType: "test",
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role);
        var controller = new ScopesController(scopeService);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        return controller;
    }
}
