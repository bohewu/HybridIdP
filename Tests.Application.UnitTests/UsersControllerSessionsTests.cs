using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Web.IdP.Api;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

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
        var loginHistoryMock = new Mock<ILoginHistoryService>();

        return new UsersController(userMgmt.Object, userManager, sessionServiceMock.Object, loginHistoryMock.Object);
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
        var result = await controller.ListSessions(userId, 1, 10);
        var ok = Assert.IsType<OkObjectResult>(result);
        var anon = ok.Value!;
        var itemsProp = anon.GetType().GetProperty("items");
        Assert.NotNull(itemsProp);
        var items = Assert.IsAssignableFrom<IEnumerable<SessionDto>>(itemsProp!.GetValue(anon)!);
        Assert.Equal(2, items.Count());
    }

    [Fact]
    public async Task ListSessions_ReturnsOk_WithEmptyList_WhenUserHasNoSessions()
    {
        var controller = CreateController(out var sessMock);
        var userId = Guid.NewGuid();
        sessMock.Setup(s => s.ListSessionsAsync(userId))
            .ReturnsAsync(Array.Empty<SessionDto>());
        var result = await controller.ListSessions(userId, 1, 5);
        var ok = Assert.IsType<OkObjectResult>(result);
        var anon = ok.Value!;
        var itemsProp = anon.GetType().GetProperty("items");
        Assert.NotNull(itemsProp);
        var items = Assert.IsAssignableFrom<IEnumerable<SessionDto>>(itemsProp!.GetValue(anon)!);
        Assert.Empty(items);
    }

    [Fact]
    public async Task ListSessions_Returns500_WhenServiceThrows()
    {
        var controller = CreateController(out var sessMock);
        var userId = Guid.NewGuid();
        sessMock.Setup(s => s.ListSessionsAsync(userId))
            .ThrowsAsync(new InvalidOperationException("Database error"));
        var result = await controller.ListSessions(userId, 1, 10);
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public async Task ListSessions_VerifiesCorrectUserId_WhenCalled()
    {
        var controller = CreateController(out var sessMock);
        var userId = Guid.NewGuid();
        sessMock.Setup(s => s.ListSessionsAsync(userId))
            .ReturnsAsync(Array.Empty<SessionDto>());
        await controller.ListSessions(userId, 1, 10);
        sessMock.Verify(s => s.ListSessionsAsync(userId), Times.Once);
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
    public async Task ListSessions_PaginatesCorrectly()
    {
        var controller = CreateController(out var sessMock);
        var userId = Guid.NewGuid();
        var sessions = Enumerable.Range(1, 5)
            .Select(i => new SessionDto($"auth-{i}", null, null, null, null, null))
            .ToArray();
        sessMock.Setup(s => s.ListSessionsAsync(userId)).ReturnsAsync(sessions);
        var result = await controller.ListSessions(userId, 2, 2);
        var ok = Assert.IsType<OkObjectResult>(result);
        var anon = ok.Value!;
        var itemsProp = anon.GetType().GetProperty("items");
        var pageProp = anon.GetType().GetProperty("page");
        var pagesProp = anon.GetType().GetProperty("pages");
        var totalProp = anon.GetType().GetProperty("total");
        Assert.NotNull(itemsProp);
        var items = Assert.IsAssignableFrom<IEnumerable<SessionDto>>(itemsProp!.GetValue(anon)!);
        Assert.Equal(2, items.Count());
        Assert.Contains(items, s => s.AuthorizationId == "auth-3");
        Assert.Contains(items, s => s.AuthorizationId == "auth-4");
        Assert.Equal(2, (int)pageProp!.GetValue(anon)!);
        Assert.Equal(3, (int)pagesProp!.GetValue(anon)!);
        Assert.Equal(5, (int)totalProp!.GetValue(anon)!);
    }
}
