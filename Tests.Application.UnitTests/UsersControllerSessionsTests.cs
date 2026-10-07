using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.Ports;
using Core.Application.DTOs;
using Core.Application.Options;
using Core.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Localization;
using Moq;
using Web.IdP;
using Web.IdP.Controllers.Admin;
using Web.IdP.Services;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using AspNetCoreAuthorizationService = Microsoft.AspNetCore.Authorization.IAuthorizationService;
using Core.Domain.Constants;
using Core.Domain.Events;
using Infrastructure;
using Infrastructure.Services;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace Tests.Application.UnitTests;

public class UsersControllerSessionsTests
{
    private static UsersController CreateController(
        out Mock<ISessionService> sessionServiceMock,
        ICurrentUserLifecycleEligibility? lifecycle = null, IImpersonationService? impersonation = null, IAuditService? audit = null)
    {
        var userMgmt = new Mock<IUserManagementService>();

        var store = new Mock<IUserStore<ApplicationUser>>();
        var roleStore = new Mock<IRoleStore<ApplicationRole>>();
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

        var roleManager = new RoleManager<ApplicationRole>(
            roleStore.Object,
            Array.Empty<IRoleValidator<ApplicationRole>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new Mock<ILogger<RoleManager<ApplicationRole>>>().Object);

        sessionServiceMock = new Mock<ISessionService>();
        var loginHistoryMock = new Mock<ILoginHistoryService>();
        var dbContextMock = new Mock<IApplicationDbContext>();
        var localizerMock = new Mock<IStringLocalizer<SharedResource>>();
        var impersonationMock = new Mock<IImpersonationService>();

        return new UsersController(
            lifecycle ?? Moq.Mock.Of<global::Web.IdP.Services.ICurrentUserLifecycleEligibility>(policy => policy.IsEligibleAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<CancellationToken>()) == Task.FromResult(true)),
            userMgmt.Object, 
            userManager, 
            roleManager,
            sessionServiceMock.Object, 
            loginHistoryMock.Object,
            dbContextMock.Object,
            localizerMock.Object,
            impersonation ?? impersonationMock.Object,
            new Mock<AspNetCoreAuthorizationService>().Object,
            Options.Create(new PrivilegedRoleProtectionOptions()),
            new Mock<ILogger<UsersController>>().Object,
            Mock.Of<IRecoveryAssistanceService>(),
            audit ?? Mock.Of<IAuditService>());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ImpersonationCookies_ShouldCheckReceivingAccountAfterPrincipalPreparation(bool restore, bool allowed)
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var receivingId = restore ? actorId : targetId;
        var issued = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, receivingId.ToString())], "test"));
        var impersonation = new Mock<IImpersonationService>();
        impersonation.Setup(service => service.StartImpersonationAsync(actorId, targetId)).ReturnsAsync((true, issued, (string?)null));
        impersonation.Setup(service => service.RevertImpersonationAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>())).ReturnsAsync((true, issued, (string?)null));
        var lifecycle = new Mock<ICurrentUserLifecycleEligibility>();
        lifecycle.Setup(policy => policy.IsEligibleAsync(receivingId, It.IsAny<CancellationToken>())).ReturnsAsync(allowed);
        var authentication = new Mock<Microsoft.AspNetCore.Authentication.IAuthenticationService>();
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider(),
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, actorId.ToString())], "test"))
        };
        IActionResult result;
        if (restore)
        {
            var controller = new ImpersonationController(lifecycle.Object, impersonation.Object, Mock.Of<ILogger<ImpersonationController>>(), Mock.Of<IAuditService>())
                { ControllerContext = new ControllerContext { HttpContext = context } };
            result = await controller.Stop();
        }
        else
        {
            var controller = CreateController(out _, lifecycle.Object, impersonation.Object);
            controller.ControllerContext = new ControllerContext { HttpContext = context };
            result = await controller.StartImpersonation(targetId);
        }
        if (allowed) Assert.IsType<OkObjectResult>(result);
        else Assert.IsType<BadRequestObjectResult>(result);
        lifecycle.Verify(policy => policy.IsEligibleAsync(receivingId, It.IsAny<CancellationToken>()), Times.Once);
        authentication.Verify(service => service.SignInAsync(context, IdentityConstants.ApplicationScheme, issued,
            It.IsAny<Microsoft.AspNetCore.Authentication.AuthenticationProperties>()), allowed ? Times.Once() : Times.Never());
        authentication.Verify(service => service.SignOutAsync(It.IsAny<HttpContext>(), It.IsAny<string>(),
            It.IsAny<Microsoft.AspNetCore.Authentication.AuthenticationProperties>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Impersonation_ShouldPersistStartActionAndStop_ThroughRestoredCookie(bool claimOnly)
    {
        var actor = new ApplicationUser { Id = Guid.NewGuid(), UserName = "private-admin" };
        var target = new ApplicationUser { Id = Guid.NewGuid(), UserName = "private-target" };
        var userManager = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        userManager.Setup(manager => manager.FindByIdAsync(actor.Id.ToString())).ReturnsAsync(actor);
        userManager.Setup(manager => manager.FindByIdAsync(target.Id.ToString())).ReturnsAsync(target);
        userManager.Setup(manager => manager.IsInRoleAsync(target, AuthConstants.Roles.Admin)).ReturnsAsync(false);
        var factory = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
        factory.Setup(claims => claims.CreateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync((ApplicationUser user) =>
            new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.UserName!)], IdentityConstants.ApplicationScheme)));
        var lifecycle = Mock.Of<ICurrentUserLifecycleEligibility>(policy => policy.IsEligibleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(true));
        var impersonation = new ImpersonationService(lifecycle, userManager.Object, factory.Object);
        var authentication = new Mock<Microsoft.AspNetCore.Authentication.IAuthenticationService>();
        authentication.Setup(service => service.SignInAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme,
                It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()))
            .Callback<HttpContext, string, ClaimsPrincipal, AuthenticationProperties>((http, scheme, principal, properties) =>
            {
                var ticket = new AuthenticationTicket(principal, properties, scheme);
                http.User = TicketSerializer.Default.Deserialize(TicketSerializer.Default.Serialize(ticket))!.Principal;
            }).Returns(Task.CompletedTask);
        using var services = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            User = await factory.Object.CreateAsync(actor)
        };
        var accessor = new HttpContextAccessor { HttpContext = context };
        var administrativeBoundary = new Mock<IAdministrativeAuthorizationBoundary>();
        administrativeBoundary.Setup(boundary => boundary.ResolveAsync()).ReturnsAsync(() =>
            new AdministrativeAuthority(context.User, false, new HashSet<string>()));
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var audit = new AuditService(db, db, Mock.Of<IDomainEventPublisher>(), Mock.Of<ISettingsService>(),
            Options.Create(new AuditOptions { PiiMaskingLevel = PiiMaskingLevel.Strict }), accessor, administrativeBoundary.Object);
        var startController = CreateController(out _, lifecycle, impersonation, audit);
        startController.ControllerContext = new ControllerContext { HttpContext = context };

        Assert.IsType<OkObjectResult>(await startController.StartImpersonation(target.Id));
        Assert.Equal(target.Id.ToString(), context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal(actor.Id.ToString(), ((ClaimsIdentity)context.User.Identity!).Actor!.FindFirst("sub")?.Value);
        if (claimOnly) ((ClaimsIdentity)context.User.Identity!).Actor = null;
        var affectedUserId = Guid.NewGuid().ToString();
        await audit.HandleAsync(new UserUpdatedEvent(affectedUserId, "private-affected", "profile"));
        var stopController = new ImpersonationController(lifecycle, impersonation, Mock.Of<ILogger<ImpersonationController>>(), audit)
            { ControllerContext = new ControllerContext { HttpContext = context } };
        Assert.IsType<OkObjectResult>(await stopController.Stop());
        Assert.Equal(actor.Id.ToString(), context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Null(((ClaimsIdentity)context.User.Identity!).Actor);
        Assert.Null(context.User.FindFirst(AuthConstants.Claims.ImpersonatorId));
        await audit.LogEventAsync("OrdinaryAfterRestoration", actor.Id.ToString(), "original-details", null, null);

        db.ChangeTracker.Clear();
        var records = await db.AuditEvents.ToListAsync();
        Assert.Equal(4, records.Count);
        foreach (var eventType in new[] { "ImpersonationStarted", "UserUpdated", "ImpersonationStopped" })
        {
            var record = Assert.Single(records, entry => entry.EventType == eventType);
            Assert.Equal(actor.Id.ToString(), record.UserId);
            using var details = JsonDocument.Parse(record.Details!);
            if (eventType == "UserUpdated")
                Assert.Equal(affectedUserId, details.RootElement.GetProperty("target").GetProperty("id").GetString());
            var attribution = details.RootElement.GetProperty("impersonation");
            Assert.Equal(actor.Id.ToString(), attribution.GetProperty("actorUserId").GetString());
            Assert.Equal(target.Id.ToString(), attribution.GetProperty("subjectUserId").GetString());
            Assert.DoesNotContain("private-", record.Details);
        }
        Assert.Equal("original-details", Assert.Single(records, record => record.EventType == "OrdinaryAfterRestoration").Details);
        authentication.Verify(service => service.SignInAsync(context, IdentityConstants.ApplicationScheme,
            It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Impersonation_ShouldNotIssueCookie_WhenTransitionAuditFails(bool restore)
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var impersonated = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, targetId.ToString()),
            new Claim(AuthConstants.Claims.ImpersonatorId, actorId.ToString())], "cookie"));
        var actor = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actorId.ToString())], "cookie"));
        var impersonation = new Mock<IImpersonationService>();
        impersonation.Setup(service => service.StartImpersonationAsync(actorId, targetId)).ReturnsAsync((true, impersonated, (string?)null));
        impersonation.Setup(service => service.RevertImpersonationAsync(impersonated)).ReturnsAsync((true, actor, (string?)null));
        var audit = new Mock<IAuditService>();
        audit.Setup(service => service.LogImpersonationEventAsync(It.IsAny<string>(), It.IsAny<ClaimsPrincipal>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("audit unavailable"));
        var authentication = new Mock<Microsoft.AspNetCore.Authentication.IAuthenticationService>();
        using var services = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider();
        var context = new DefaultHttpContext { User = restore ? impersonated : actor, RequestServices = services };
        var lifecycle = Mock.Of<ICurrentUserLifecycleEligibility>(policy => policy.IsEligibleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(true));
        IActionResult result;
        if (restore)
        {
            var controller = new ImpersonationController(lifecycle, impersonation.Object, Mock.Of<ILogger<ImpersonationController>>(), audit.Object)
                { ControllerContext = new ControllerContext { HttpContext = context } };
            result = await controller.Stop();
        }
        else
        {
            var controller = CreateController(out _, lifecycle, impersonation.Object, audit.Object);
            controller.ControllerContext = new ControllerContext { HttpContext = context };
            result = await controller.StartImpersonation(targetId);
        }
        Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
        authentication.Verify(service => service.SignInAsync(It.IsAny<HttpContext>(), It.IsAny<string>(),
            It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()), Times.Never);
    }

    [Fact]
    public async Task StartImpersonation_ShouldPreserveOriginalActor_WhenSwitchingAnImpersonatedCookie()
    {
        var actorId = Guid.NewGuid();
        var previousSubjectId = Guid.NewGuid();
        var newSubjectId = Guid.NewGuid();
        var incoming = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, previousSubjectId.ToString()),
            new Claim(AuthConstants.Claims.ImpersonatorId, actorId.ToString())], "cookie"));
        var prepared = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, newSubjectId.ToString()),
            new Claim(AuthConstants.Claims.ImpersonatorId, actorId.ToString())], "cookie"));
        var impersonation = new Mock<IImpersonationService>();
        impersonation.Setup(service => service.StartImpersonationAsync(actorId, newSubjectId)).ReturnsAsync((true, prepared, (string?)null));
        var authentication = new Mock<Microsoft.AspNetCore.Authentication.IAuthenticationService>();
        using var services = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider();
        var controller = CreateController(out _, impersonation: impersonation.Object);
        controller.ControllerContext = new ControllerContext
            { HttpContext = new DefaultHttpContext { User = incoming, RequestServices = services } };

        Assert.IsType<OkObjectResult>(await controller.StartImpersonation(newSubjectId));

        impersonation.Verify(service => service.StartImpersonationAsync(previousSubjectId, newSubjectId), Times.Never);
        impersonation.Verify(service => service.StartImpersonationAsync(actorId, newSubjectId), Times.Once);
    }

    [Fact]
    public async Task StartImpersonation_ShouldRejectMissingSubject_AndRetainPermissionRequirement()
    {
        var audit = new Mock<IAuditService>();
        var controller = CreateController(out _, audit: audit.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        Assert.IsType<UnauthorizedResult>(await controller.StartImpersonation(Guid.NewGuid()));

        var method = typeof(UsersController).GetMethod(nameof(UsersController.StartImpersonation))!;
        Assert.Equal(Permissions.Users.Impersonate, Assert.Single(method.GetCustomAttributes(typeof(HasPermissionAttribute), true).Cast<HasPermissionAttribute>()).Policy);
        audit.Verify(service => service.LogImpersonationEventAsync(It.IsAny<string>(), It.IsAny<ClaimsPrincipal>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImpersonationPermission_ShouldRequireApplicablePermission(bool allowed)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("permission", allowed ? Permissions.Users.Impersonate : Permissions.Users.Read)], IdentityConstants.ApplicationScheme));
        var boundary = new Mock<IAdministrativeAuthorizationBoundary>();
        boundary.Setup(service => service.ResolveAsync()).ReturnsAsync(new AdministrativeAuthority(principal, false, new HashSet<string>()));
        var roles = new Mock<RoleManager<ApplicationRole>>(Mock.Of<IRoleStore<ApplicationRole>>(), null, null, null, null);
        var requirement = new PermissionRequirement(Permissions.Users.Impersonate);
        var context = new AuthorizationHandlerContext([requirement], principal, null);

        await new PermissionAuthorizationHandler(roles.Object, boundary.Object).HandleAsync(context);

        Assert.Equal(allowed, context.HasSucceeded);
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
        sessMock.Setup(s => s.ListSessionsAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(sessions);
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
        sessMock.Setup(s => s.ListSessionsAsync(userId, It.IsAny<CancellationToken>()))
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
        sessMock.Setup(s => s.ListSessionsAsync(userId, It.IsAny<CancellationToken>()))
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
        sessMock.Setup(s => s.ListSessionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SessionDto>());
        await controller.ListSessions(userId, 1, 10);
        sessMock.Verify(s => s.ListSessionsAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeSession_ReturnsNoContent_WhenSuccess()
    {
        var controller = CreateController(out var sessMock);
        var userId = Guid.NewGuid();
        sessMock.Setup(s => s.RevokeSessionAsync(userId, "auth-1", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        // Simulate the caller being the same user (owner) so the controller allows the operation
        var claims = new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()) };
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "TestAuth")) } };
        var result = await controller.RevokeSession(userId, "auth-1");
        Assert.IsType<NoContentResult>(result);
    }

    /* 
    [Fact]
    public async Task RevokeSession_ReturnsForbidden_WhenCalledByDifferentNonAdmin()
    {
        // This test is invalid because the controller relies on [HasPermission] attribute for security,
        // which is not executed in unit tests. The method body does not contain imperative permission checks.
        // Authorization is covered by System Tests (Integration Tests).
    }
    */

    [Fact]
    public async Task RevokeSession_ReturnsNotFound_WhenNotOwnedOrMissing()
    {
        var controller = CreateController(out var sessMock);
        var userId = Guid.NewGuid();
        sessMock.Setup(s => s.RevokeSessionAsync(userId, "auth-x", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        // Simulate the caller being the same user (owner) so the controller allows the operation to run
        var claims = new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()) };
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "TestAuth")) } };
        var result = await controller.RevokeSession(userId, "auth-x");
        Assert.IsType<NotFoundObjectResult>(result);
    }

    /*
    [Fact]
    public async Task RevokeAllSessions_ReturnsForbidden_WhenCalledByDifferentNonAdmin()
    {
        // Invalid test for attribute-based security.
    }
    */

    [Fact]
    public async Task ListSessions_PaginatesCorrectly()
    {
        var controller = CreateController(out var sessMock);
        var userId = Guid.NewGuid();
        var sessions = Enumerable.Range(1, 5)
            .Select(i => new SessionDto($"auth-{i}", null, null, null, null, null))
            .ToArray();
        sessMock.Setup(s => s.ListSessionsAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(sessions);
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
