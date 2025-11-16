using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Web.IdP.Api;
using Xunit;

namespace Tests.Application.UnitTests;

public class UsersControllerSessionsTests
{
    private static UsersController CreateController(
        out Mock<ISessionService> sessionServiceMock)
    {
        var userMgmt = new Mock<IUserManagementService>();

        var store = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new UserManager<ApplicationUser>(
            store.Object,
            Options.Create(new IdentityOptions()),
            new Mock<IPasswordHasher<ApplicationUser>>().Object,
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new Mock<ILookupNormalizer>().Object,
            new IdentityErrorDescriber(),
            new Mock<IServiceProvider>().Object,
            new Mock<ILogger<UserManager<ApplicationUser>>>().Object);

        sessionServiceMock = new Mock<ISessionService>();

        return new UsersController(userMgmt.Object, userManager, sessionServiceMock.Object);
    }

    [Fact]
    public async Task ListSessions_ReturnsOk_WithSessions()
    {
        var controller = CreateController(out var sessMock);
        var userId = Guid.NewGuid();
        var sessions = new SessionDto[]
        {
            new SessionDto("auth-1", null, null, null, null, null),
            new SessionDto("auth-2", null, null, null, null, null)
        };
        sessMock.Setup(s => s.ListSessionsAsync(userId)).ReturnsAsync(sessions);

        var result = await controller.ListSessions(userId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsAssignableFrom<IEnumerable<SessionDto>>(ok.Value);
        Assert.Equal(2, payload.Count());
    }

    [Fact]
    public async Task RevokeSession_ReturnsNoContent_WhenSuccess()
    {
        var controller = CreateController(out var sessMock);
        var userId = Guid.NewGuid();
        sessMock.Setup(s => s.RevokeSessionAsync(userId, "auth-1")).ReturnsAsync(true);

        var result = await controller.RevokeSession(userId, "auth-1");

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task RevokeSession_ReturnsNotFound_WhenNotOwnedOrMissing()
    {
        var controller = CreateController(out var sessMock);
        var userId = Guid.NewGuid();
        sessMock.Setup(s => s.RevokeSessionAsync(userId, "auth-x")).ReturnsAsync(false);

        var result = await controller.RevokeSession(userId, "auth-x");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task RevokeAllSessions_ReturnsOk_WithCount()
    {
        var controller = CreateController(out var sessMock);
        var userId = Guid.NewGuid();
        sessMock.Setup(s => s.RevokeAllSessionsAsync(userId)).ReturnsAsync(3);

        var result = await controller.RevokeAllSessions(userId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var anon = ok.Value!;
        var revokedProp = anon.GetType().GetProperty("revoked");
        Assert.NotNull(revokedProp);
        Assert.Equal(3, (int)revokedProp!.GetValue(anon)!);
    }
}
