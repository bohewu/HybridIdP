using Core.Application;
using Core.Application.DTOs;
using Core.Application.Options;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Events;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Tests.Infrastructure.IntegrationTests;

public sealed class AuditQueryTests : IDisposable
{
    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly AuditService _service;

    public AuditQueryTests() => _service = new AuditService(_db, _db,
        Mock.Of<IDomainEventPublisher>(), Mock.Of<ISettingsService>(), Options.Create(new AuditOptions()));

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetEventsAsync_ShouldReturnDisjointPages_WhenTimestampsMatch()
    {
        var timestamp = DateTime.UtcNow;
        _db.AuditEvents.AddRange(Enumerable.Range(1, 5).Select(id =>
            new AuditEvent { Id = id, EventType = "LoginAttempt", Timestamp = timestamp }));
        await _db.SaveChangesAsync();

        var first = await _service.GetEventsAsync(new AuditEventFilterDto { PageSize = 2 });
        var second = await _service.GetEventsAsync(new AuditEventFilterDto { PageSize = 2, PageNumber = 2 });

        Assert.Equal(5, second.totalCount);
        Assert.Equal(new[] { 5, 4 }, first.items.Select(item => item.Id));
        Assert.Equal(new[] { 3, 2 }, second.items.Select(item => item.Id));
    }

    [Fact]
    public async Task GetEventTypesAsync_ShouldReturnExactDistinctPersistedNames()
    {
        _db.AuditEvents.AddRange(new[] { "LoginAttempt", "LoginAttempt", "UserCreated", "EmailMfaDisabled" }
            .Select(type => new AuditEvent { EventType = type }));
        await _db.SaveChangesAsync();

        Assert.Equal(new[] { "EmailMfaDisabled", "LoginAttempt", "UserCreated" },
            await _service.GetEventTypesAsync());
    }

    [Fact]
    public async Task GetEventsAsync_ShouldApplyTypeSearchAndIpTogether()
    {
        _db.AuditEvents.AddRange(
            new AuditEvent { EventType = "LoginAttempt", Details = "successful", IPAddress = "127.0.0.1" },
            new AuditEvent { EventType = "LoginAttempt", Details = "failed", IPAddress = "127.0.0.1" },
            new AuditEvent { EventType = "EmailMfaDisabled", Details = "successful", IPAddress = "127.0.0.1" });
        await _db.SaveChangesAsync();

        var result = await _service.GetEventsAsync(new AuditEventFilterDto
            { EventType = "LoginAttempt", Search = "successful", IPAddress = "127.0.0.1" });

        Assert.Equal(1, result.totalCount);
        Assert.Equal("successful", Assert.Single(result.items).Details);
    }

    [Fact]
    public async Task GetEventsAsync_ShouldResolveEmailFilterAndApplyAscendingSort()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "s340000", Email = "student@example.test" };
        _db.Users.Add(user);
        _db.AuditEvents.AddRange(
            new AuditEvent { Id = 1, EventType = "LoginAttempt", UserId = user.Id.ToString(), Timestamp = DateTime.UtcNow.AddMinutes(-1) },
            new AuditEvent { Id = 2, EventType = "LoginAttempt", UserId = user.Id.ToString(), Timestamp = DateTime.UtcNow },
            new AuditEvent { Id = 3, EventType = "LoginAttempt", UserId = Guid.NewGuid().ToString() });
        await _db.SaveChangesAsync();

        var result = await _service.GetEventsAsync(new AuditEventFilterDto
            { UserId = user.Email, Sort = "timestamp:asc" });

        Assert.Equal(2, result.totalCount);
        Assert.Equal(new[] { 1, 2 }, result.items.Select(item => item.Id));
    }
}
