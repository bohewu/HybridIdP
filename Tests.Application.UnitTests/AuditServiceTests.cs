using Core.Application;
using Core.Application.DTOs;
using Core.Application.Options;
using Core.Domain.Entities;
using Core.Domain.Events;
using Core.Domain.Constants; // Added
using Infrastructure;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.Text.Json;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Tests.Application.UnitTests;

public class AuditServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IDomainEventPublisher> _eventPublisherMock;
    private readonly Mock<ISettingsService> _settingsServiceMock;
    private readonly AuditService _auditService;
    private readonly HttpContextAccessor _httpContextAccessor = new();

    public AuditServiceTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _eventPublisherMock = new Mock<IDomainEventPublisher>();
        _settingsServiceMock = new Mock<ISettingsService>();
        _settingsServiceMock.Setup(s => s.GetValueAsync<int>(SettingKeys.Audit.RetentionDays, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var auditOptions = Options.Create(new AuditOptions { PiiMaskingLevel = PiiMaskingLevel.Partial });
        _auditService = new AuditService(_dbContext, _dbContext, _eventPublisherMock.Object, _settingsServiceMock.Object, auditOptions, _httpContextAccessor);
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
    }

    #region LogEventAsync Tests

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task LogEventAsync_ShouldPersistActorAndSubject_WithoutChangingAffectedUser(bool claimOnly, bool subOnly)
    {
        var actorId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var affectedUserId = Guid.NewGuid().ToString();
        var identity = new ClaimsIdentity([new Claim(subOnly ? "sub" : ClaimTypes.NameIdentifier, subjectId.ToString())], "cookie");
        if (claimOnly) identity.AddClaim(new Claim(AuthConstants.Claims.ImpersonatorId, actorId.ToString()));
        else identity.Actor = new ClaimsIdentity([new Claim(subOnly ? "sub" : ClaimTypes.NameIdentifier, actorId.ToString())], "Impersonation");
        _httpContextAccessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        await _auditService.LogEventAsync("UserUpdated", affectedUserId, "{\"action\":\"update\",\"impersonation\":{\"actorUserId\":\"untrusted\"}}", null, null);

        _dbContext.ChangeTracker.Clear();
        var stored = await _dbContext.AuditEvents.SingleAsync();
        Assert.Equal(affectedUserId, stored.UserId);
        using var details = JsonDocument.Parse(stored.Details!);
        Assert.Equal("update", details.RootElement.GetProperty("action").GetString());
        var attribution = details.RootElement.GetProperty("impersonation");
        Assert.Equal(actorId.ToString(), attribution.GetProperty("actorUserId").GetString());
        Assert.Equal(subjectId.ToString(), attribution.GetProperty("subjectUserId").GetString());
        var (items, _) = await _auditService.GetEventsAsync(new AuditEventFilterDto());
        Assert.Equal(stored.Details, Assert.Single(items).Details);
        var export = await _auditService.ExportEventAsync(stored.Id);
        Assert.Equal(stored.Details, export!.Details);
    }

    [Theory]
    [InlineData(PiiMaskingLevel.Partial, "ali***")]
    [InlineData(PiiMaskingLevel.Strict, "***")]
    public async Task HandleAsync_ShouldKeepNameMasking_WhenImpersonating(PiiMaskingLevel level, string maskedName)
    {
        var actorId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, subjectId.ToString()),
            new Claim(AuthConstants.Claims.ImpersonatorId, actorId.ToString()),
            new Claim(ClaimTypes.Name, "private-target-name")], "cookie")
        { Actor = new ClaimsIdentity([new Claim("sub", actorId.ToString()), new Claim(ClaimTypes.Name, "private-actor-name")], "Impersonation") };
        _httpContextAccessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        var audit = new AuditService(_dbContext, _dbContext, _eventPublisherMock.Object, _settingsServiceMock.Object,
            Options.Create(new AuditOptions { PiiMaskingLevel = level }), _httpContextAccessor);

        await audit.HandleAsync(new UserUpdatedEvent(subjectId.ToString(), "alice-private", "profile"));

        _dbContext.ChangeTracker.Clear();
        var stored = await _dbContext.AuditEvents.SingleAsync();
        using var details = JsonDocument.Parse(stored.Details!);
        Assert.Equal($"User '{maskedName}' was updated: profile", details.RootElement.GetProperty("details").GetString());
        Assert.DoesNotContain("alice-private", stored.Details);
        Assert.DoesNotContain("private-actor-name", stored.Details);
        Assert.DoesNotContain("private-target-name", stored.Details);
        Assert.Equal(actorId.ToString(), details.RootElement.GetProperty("impersonation").GetProperty("actorUserId").GetString());
    }

    [Fact]
    public async Task LogImpersonationEventAsync_ShouldUsePreparedSubject_WhenIncomingCookieImpersonatesAnotherSubject()
    {
        var actorId = Guid.NewGuid();
        var newSubjectId = Guid.NewGuid();
        var incoming = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(AuthConstants.Claims.ImpersonatorId, actorId.ToString())], "cookie");
        _httpContextAccessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(incoming) };
        var prepared = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, newSubjectId.ToString()),
            new Claim(AuthConstants.Claims.ImpersonatorId, actorId.ToString())], "cookie"));

        await _auditService.LogImpersonationEventAsync("ImpersonationStarted", prepared, null, null);

        var stored = await _dbContext.AuditEvents.SingleAsync();
        using var details = JsonDocument.Parse(stored.Details!);
        Assert.Equal(actorId.ToString(), stored.UserId);
        Assert.Equal(newSubjectId.ToString(), details.RootElement.GetProperty("impersonation").GetProperty("subjectUserId").GetString());
    }

    [Fact]
    public async Task LogEventAsync_ShouldRejectConflictingActor_WithoutWritingMisattributedRecord()
    {
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(AuthConstants.Claims.ImpersonatorId, Guid.NewGuid().ToString())], "cookie")
        { Actor = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "Impersonation") };
        _httpContextAccessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _auditService.LogEventAsync("UserUpdated", "affected-user", null, null, null));

        Assert.Empty(await _dbContext.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task LogEventAsync_ShouldReadApplicationCookie_WhenBearerIdentityComesFirst()
    {
        var actorId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var cookie = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, subjectId.ToString()),
            new Claim(AuthConstants.Claims.ImpersonatorId, actorId.ToString())], Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme);
        var bearer = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "Bearer");
        _httpContextAccessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal([bearer, cookie]) };

        await _auditService.LogEventAsync("UserUpdated", "affected-user", null, null, null);

        var stored = await _dbContext.AuditEvents.SingleAsync();
        using var details = JsonDocument.Parse(stored.Details!);
        var attribution = details.RootElement.GetProperty("impersonation");
        Assert.Equal(actorId.ToString(), attribution.GetProperty("actorUserId").GetString());
        Assert.Equal(subjectId.ToString(), attribution.GetProperty("subjectUserId").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("system")]
    [InlineData("bootstrap-user")]
    public async Task LogEventAsync_ShouldPreserveOrdinaryAndSystemDetails_WhenNoImpersonation(string? userId)
    {
        _httpContextAccessor.HttpContext = new DefaultHttpContext
        { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "cookie")) };
        const string details = "{\"operation\":\"bootstrap\"}";

        await _auditService.LogEventAsync("ExistingEvent", userId, details, null, null);

        var stored = await _dbContext.AuditEvents.SingleAsync();
        Assert.Equal(userId, stored.UserId);
        Assert.Equal(details, stored.Details);
    }

    [Fact]
    public async Task LogEventAsync_ShouldCreateAuditEvent_WithAllFields()
    {
        // Arrange
        var eventType = "UserLogin";
        var userId = "user123";
        var details = "{\"action\":\"login\"}";
        var ipAddress = "192.168.1.1";
        var userAgent = "Mozilla/5.0";

        // Act
        await _auditService.LogEventAsync(eventType, userId, details, ipAddress, userAgent);

        // Assert
        var auditEvent = await _dbContext.AuditEvents.FirstOrDefaultAsync();
        Assert.NotNull(auditEvent);
        Assert.Equal(eventType, auditEvent.EventType);
        Assert.Equal(userId, auditEvent.UserId);
        Assert.Equal(details, auditEvent.Details);
        Assert.Equal(ipAddress, auditEvent.IPAddress);
        Assert.Equal(userAgent, auditEvent.UserAgent);
        Assert.True(auditEvent.Timestamp <= DateTime.UtcNow);

        // Verify domain event was published
        _eventPublisherMock.Verify(p => p.PublishAsync(It.Is<AuditEventLoggedEvent>(
            e => e.EventType == eventType && e.UserId == userId && e.AuditEventId == auditEvent.Id)), Times.Once);
    }

    [Fact]
    public async Task LogEventAsync_ShouldCreateAuditEvent_WithNullUserId()
    {
        // Arrange
        var eventType = "SystemEvent";

        // Act
        await _auditService.LogEventAsync(eventType, null, null, null, null);

        // Assert
        var auditEvent = await _dbContext.AuditEvents.FirstOrDefaultAsync();
        Assert.NotNull(auditEvent);
        Assert.Equal(eventType, auditEvent.EventType);
        Assert.Null(auditEvent.UserId);
        Assert.Null(auditEvent.Details);
        Assert.Null(auditEvent.IPAddress);
        Assert.Null(auditEvent.UserAgent);

        // Verify domain event was published
        _eventPublisherMock.Verify(p => p.PublishAsync(It.Is<AuditEventLoggedEvent>(
            e => e.EventType == eventType && e.UserId == null && e.AuditEventId == auditEvent.Id)), Times.Once);
    }

    #endregion

    #region GetEventsAsync Tests

    [Fact]
    public async Task GetEventsAsync_ShouldReturnAllEvents_WhenNoFilters()
    {
        // Arrange
        var events = new List<AuditEvent>
        {
            new AuditEvent { EventType = "Login", UserId = "user1", Timestamp = DateTime.UtcNow.AddHours(-1) },
            new AuditEvent { EventType = "Logout", UserId = "user2", Timestamp = DateTime.UtcNow }
        };
        _dbContext.AuditEvents.AddRange(events);
        await _dbContext.SaveChangesAsync();

        var filter = new AuditEventFilterDto();

        // Act
        var (items, totalCount) = await _auditService.GetEventsAsync(filter);

        // Assert
        Assert.Equal(2, totalCount);
        Assert.Equal(2, items.Count());
    }

    [Fact]
    public async Task GetEventsAsync_ShouldFilterByEventType()
    {
        // Arrange
        var events = new List<AuditEvent>
        {
            new AuditEvent { EventType = "Login", UserId = "user1" },
            new AuditEvent { EventType = "Logout", UserId = "user2" }
        };
        _dbContext.AuditEvents.AddRange(events);
        await _dbContext.SaveChangesAsync();

        var filter = new AuditEventFilterDto { EventType = "Login" };

        // Act
        var (items, totalCount) = await _auditService.GetEventsAsync(filter);

        // Assert
        Assert.Equal(1, totalCount);
        Assert.Single(items);
        Assert.Equal("Login", items.First().EventType);
    }

    [Fact]
    public async Task GetEventsAsync_ShouldFilterByUserId()
    {
        // Arrange
        var events = new List<AuditEvent>
        {
            new AuditEvent { EventType = "Login", UserId = "user1" },
            new AuditEvent { EventType = "Login", UserId = "user2" }
        };
        _dbContext.AuditEvents.AddRange(events);
        await _dbContext.SaveChangesAsync();

        var filter = new AuditEventFilterDto { UserId = "user1" };

        // Act
        var (items, totalCount) = await _auditService.GetEventsAsync(filter);

        // Assert
        Assert.Equal(1, totalCount);
        Assert.Single(items);
        Assert.Equal("user1", items.First().UserId);
    }

    [Fact]
    public async Task GetEventsAsync_ShouldFilterByDateRange()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var events = new List<AuditEvent>
        {
            new AuditEvent { EventType = "Login", Timestamp = now.AddDays(-2) },
            new AuditEvent { EventType = "Login", Timestamp = now.AddDays(-1) },
            new AuditEvent { EventType = "Login", Timestamp = now }
        };
        _dbContext.AuditEvents.AddRange(events);
        await _dbContext.SaveChangesAsync();

        var filter = new AuditEventFilterDto { StartDate = now.AddDays(-1.5), EndDate = now.AddDays(-0.5) };

        // Act
        var (items, totalCount) = await _auditService.GetEventsAsync(filter);

        // Assert
        Assert.Equal(1, totalCount);
        Assert.Single(items);
    }

    [Fact]
    public async Task GetEventsAsync_ShouldSupportPagination()
    {
        // Arrange
        var events = new List<AuditEvent>();
        for (int i = 0; i < 10; i++)
        {
            events.Add(new AuditEvent { EventType = "Login", UserId = $"user{i}" });
        }
        _dbContext.AuditEvents.AddRange(events);
        await _dbContext.SaveChangesAsync();

        var filter = new AuditEventFilterDto { PageNumber = 2, PageSize = 3 };

        // Act
        var (items, totalCount) = await _auditService.GetEventsAsync(filter);

        // Assert
        Assert.Equal(10, totalCount);
        Assert.Equal(3, items.Count());
    }

    #endregion

    #region ExportEventAsync Tests

    [Fact]
    public async Task ExportEventAsync_ShouldReturnExportDto_WhenEventExists()
    {
        // Arrange
        var auditEvent = new AuditEvent
        {
            EventType = "Login",
            UserId = "user1",
            Details = "{\"action\":\"login\"}",
            IPAddress = "192.168.1.1",
            UserAgent = "Mozilla/5.0"
        };
        _dbContext.AuditEvents.Add(auditEvent);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _auditService.ExportEventAsync(auditEvent.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(auditEvent.Id, result.Id);
        Assert.Equal(auditEvent.EventType, result.EventType);
        Assert.Equal(auditEvent.UserId, result.UserId);
        Assert.Equal(auditEvent.Details, result.Details);
        Assert.Equal(auditEvent.IPAddress, result.IPAddress);
        Assert.Equal(auditEvent.UserAgent, result.UserAgent);
        // Username will be null in in-memory test since no Users table
        Assert.Null(result.Username);
    }

    [Fact]
    public async Task ExportEventAsync_ShouldReturnExportDto_ForSystemEvent()
    {
        // Arrange
        var auditEvent = new AuditEvent
        {
            EventType = "SystemRestart",
            UserId = null,
            Details = "{\"action\":\"restart\"}",
            IPAddress = "127.0.0.1",
            UserAgent = null
        };
        _dbContext.AuditEvents.Add(auditEvent);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _auditService.ExportEventAsync(auditEvent.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(auditEvent.Id, result.Id);
        Assert.Equal(auditEvent.EventType, result.EventType);
        Assert.Null(auditEvent.UserId);
        Assert.Null(result.Username);
    }

    [Fact]
    public async Task ExportEventAsync_ShouldReturnNull_WhenEventDoesNotExist()
    {
        // Act
        var result = await _auditService.ExportEventAsync(999);

        // Assert
        Assert.Null(result);
    }

    #endregion
}
