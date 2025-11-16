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
    public async Task RevokeSession_SignsOutCurrentUser_WhenRevokedIsCurrentSession()
    {
        // Arrange
        var controller = CreateController(out var sessMock);
        var userId = Guid.NewGuid();
        var authId = "auth-current";
        sessMock.Setup(s => s.RevokeSessionAsync(userId, authId)).ReturnsAsync(true);

        // Create HttpContext with user claims including the matching authorization id
        var httpContext = new DefaultHttpContext();
        var claims = new List<System.Security.Claims.Claim>
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()),
            // Simulate OpenIddict authorization id claim - we accept any claim that contains 'auth'
            new System.Security.Claims.Claim("authorization", authId)
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "Test");
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(identity);

        // Mock the authentication service so SignOutAsync can be verified
        var authServiceMock = new Mock<Microsoft.AspNetCore.Authentication.IAuthenticationService>();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(authServiceMock.Object);
        httpContext.RequestServices = services.BuildServiceProvider();

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        // Act
        var result = await controller.RevokeSession(userId, authId);

        // Assert
        Assert.IsType<NoContentResult>(result);
        // Verify SignOutAsync was called for the application scheme
        authServiceMock.Verify(a => a.SignOutAsync(httpContext, Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme, It.IsAny<Microsoft.AspNetCore.Authentication.AuthenticationProperties>()), Times.Once);
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

    #region Additional Controller Tests

    [Fact]
    public async Task ListSessions_ReturnsOk_WithEmptyList_WhenUserHasNoSessions()
    {
        // Arrange
        var controller = CreateController(out var sessMock);
        var userId = Guid.NewGuid();
        sessMock.Setup(s => s.ListSessionsAsync(userId))
            .ReturnsAsync(Array.Empty<SessionDto>());

        // Act
        var result = await controller.ListSessions(userId);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsAssignableFrom<IEnumerable<SessionDto>>(ok.Value);
        Assert.Empty(payload);
    }

    [Fact]
    public async Task ListSessions_Returns500_WhenServiceThrows()
    {
        // Arrange
        var controller = CreateController(out var sessMock);
        var userId = Guid.NewGuid();
        sessMock.Setup(s => s.ListSessionsAsync(userId))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        // Act
        var result = await controller.ListSessions(userId);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public async Task RevokeSession_ReturnsNotFound_WhenAuthorizationIdIsNull()
    {
        // Arrange
        var controller = CreateController(out var sessMock);
        var userId = Guid.NewGuid();
        sessMock.Setup(s => s.RevokeSessionAsync(userId, It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act
        var result = await controller.RevokeSession(userId, string.Empty);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task RevokeSession_Returns500_WhenServiceThrows()
    {
        // Arrange
        var controller = CreateController(out var sessMock);
        var userId = Guid.NewGuid();
        sessMock.Setup(s => s.RevokeSessionAsync(userId, "auth-error"))
            .ThrowsAsync(new InvalidOperationException("Revocation failed"));

        // Act
        var result = await controller.RevokeSession(userId, "auth-error");

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public async Task RevokeAllSessions_ReturnsZeroCount_WhenNoSessionsExist()
    {
        // Arrange
        var controller = CreateController(out var sessMock);
        var userId = Guid.NewGuid();
        sessMock.Setup(s => s.RevokeAllSessionsAsync(userId)).ReturnsAsync(0);

        // Act
        var result = await controller.RevokeAllSessions(userId);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        var anon = ok.Value!;
        var revokedProp = anon.GetType().GetProperty("revoked");
        Assert.NotNull(revokedProp);
        Assert.Equal(0, (int)revokedProp!.GetValue(anon)!);
    }

    [Fact]
    public async Task RevokeAllSessions_Returns500_WhenServiceThrows()
    {
        // Arrange
        var controller = CreateController(out var sessMock);
        var userId = Guid.NewGuid();
        sessMock.Setup(s => s.RevokeAllSessionsAsync(userId))
            .ThrowsAsync(new InvalidOperationException("Batch revocation failed"));

        // Act
        var result = await controller.RevokeAllSessions(userId);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public async Task RevokeSession_VerifiesCorrectUserId_WhenCalled()
    {
        // Arrange
        var controller = CreateController(out var sessMock);
        var userId = Guid.NewGuid();
        var authId = "auth-123";
        sessMock.Setup(s => s.RevokeSessionAsync(userId, authId)).ReturnsAsync(true);

        // Act
        await controller.RevokeSession(userId, authId);

        // Assert
        sessMock.Verify(s => s.RevokeSessionAsync(userId, authId), Times.Once);
    }

    [Fact]
    public async Task ListSessions_VerifiesCorrectUserId_WhenCalled()
    {
        // Arrange
        var controller = CreateController(out var sessMock);
        var userId = Guid.NewGuid();
        sessMock.Setup(s => s.ListSessionsAsync(userId))
            .ReturnsAsync(Array.Empty<SessionDto>());

        // Act
        await controller.ListSessions(userId);

        // Assert
        sessMock.Verify(s => s.ListSessionsAsync(userId), Times.Once);
    }

    #endregion
}
