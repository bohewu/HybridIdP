using System;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Services;
using Moq;
using OpenIddict.Abstractions;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Application.UnitTests;

/// <summary>
/// Failing (RED) tests defining expected behavior for refresh rotation, reuse detection and chain revocation.
/// Implementation will be added to SessionService to satisfy these tests.
/// Now uses FakeTimeProvider for deterministic time control.
/// </summary>
public class SessionRefreshLifecycleTests
{
    private readonly Mock<IOpenIddictAuthorizationManager> _authz = new();
    private readonly Mock<IOpenIddictApplicationManager> _apps = new();
    private readonly Mock<IOpenIddictTokenManager> _tokens = new();
    private readonly ApplicationDbContext _db;
    private readonly SessionService _service;
    private readonly FakeTimeProvider _timeProvider;
    private readonly DateTimeOffset _fixedTime;

    public SessionRefreshLifecycleTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        
        // Use FakeTimeProvider for deterministic time
        _fixedTime = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);
        _timeProvider = new FakeTimeProvider(_fixedTime);
        _service = new SessionService(_authz.Object, _apps.Object, _tokens.Object, _db, _timeProvider);
    }

    [Fact]
    public async Task RefreshAsync_RotatesTokenAndExtendsSlidingWindow()
    {
        var userId = Guid.NewGuid();
        var authId = "auth-rotate-1";
        _db.UserSessions.Add(new UserSession
        {
            UserId = userId,
            AuthorizationId = authId,
            CurrentRefreshTokenHash = "hash_old",
            AbsoluteExpiresUtc = _fixedTime.DateTime.AddHours(8),
            SlidingExpiresUtc = _fixedTime.DateTime.AddMinutes(2), // Near expiry, will be extended
            SlidingExtensionCount = 0
        });
        await _db.SaveChangesAsync(CancellationToken.None);

        // Act (will FAIL until implemented)
        var result = await _service.RefreshAsync(userId, authId, "raw-new-token", "127.0.0.1", "UA");

        // Assert desired post-conditions
        Assert.Equal(authId, result.AuthorizationId);
        Assert.NotNull(result.RefreshTokenExpiresAt);
        // Sliding window is 30 min, so after extension it should be _fixedTime + 30min
        // Use UtcTicks for timezone-safe comparison
        var expectedExpiryUtc = _fixedTime.UtcDateTime.AddMinutes(30);
        Assert.Equal(expectedExpiryUtc.Ticks, result.RefreshTokenExpiresAt!.Value.UtcTicks);
        Assert.False(result.ReuseDetected);
        Assert.True(result.SlidingExtended);
    }

    [Fact]
    public async Task RefreshAsync_DetectsReuse_WhenPresentedMatchesPrevious()
    {
        var userId = Guid.NewGuid();
        var authId = "auth-reuse-1";
        _db.UserSessions.Add(new UserSession
        {
            UserId = userId,
            AuthorizationId = authId,
            CurrentRefreshTokenHash = "hash_current",
            PreviousRefreshTokenHash = "hash_previous",
            AbsoluteExpiresUtc = _fixedTime.DateTime.AddHours(2),
            SlidingExpiresUtc = _fixedTime.DateTime.AddMinutes(20),
            SlidingExtensionCount = 1
        });
        await _db.SaveChangesAsync(CancellationToken.None);

        // Act (will FAIL until implemented)
        var result = await _service.RefreshAsync(userId, authId, "raw-previous-token", null, null);

        // Assert desired detection flag
        Assert.Equal(authId, result.AuthorizationId);
        Assert.True(result.ReuseDetected);
    }

    [Fact]
    public async Task RefreshAsync_DetectsReuseAndRevokesSessionAndEmitsAudit()
    {
        var userId = Guid.NewGuid();
        var authId = "auth-reuse-audit-1";
        _db.UserSessions.Add(new UserSession
        {
            UserId = userId,
            AuthorizationId = authId,
            CurrentRefreshTokenHash = "hash_current",
            PreviousRefreshTokenHash = "hash_previous",
            AbsoluteExpiresUtc = _fixedTime.DateTime.AddHours(2),
            SlidingExpiresUtc = _fixedTime.DateTime.AddMinutes(20)
        });
        await _db.SaveChangesAsync(CancellationToken.None);

        var before = _db.AuditEvents.Count();
        var result = await _service.RefreshAsync(userId, authId, "raw-previous-token", "127.0.0.1", "UA");
        var after = _db.AuditEvents.Count();

        Assert.Equal(authId, result.AuthorizationId);
        Assert.True(result.ReuseDetected);
        // Session should now be revoked in DB - query by UserId+AuthorizationId since PK is single Id
        var session = await _db.UserSessions.SingleOrDefaultAsync(s => s.UserId == userId && s.AuthorizationId == authId);
        Assert.NotNull(session);
        Assert.NotNull(session.RevokedUtc);
        // An audit event for reuse should be created
        Assert.True(after >= before + 1);
        Assert.Contains(_db.AuditEvents.Select(a => a.EventType), t => t.Contains("RefreshTokenReuseDetected"));
    }

    [Fact]
    public async Task RefreshAsync_DoesNotExtendBeyondAbsoluteExpiry()
    {
        var userId = Guid.NewGuid();
        var authId = "auth-abs-1";
        var absolute = _fixedTime.DateTime.AddMinutes(10);
        var sliding = _fixedTime.DateTime.AddMinutes(2);
        _db.UserSessions.Add(new UserSession
        {
            UserId = userId,
            AuthorizationId = authId,
            CurrentRefreshTokenHash = "hash_old",
            AbsoluteExpiresUtc = absolute,
            SlidingExpiresUtc = sliding,
            SlidingExtensionCount = 0
        });
        await _db.SaveChangesAsync(CancellationToken.None);

        var result = await _service.RefreshAsync(userId, authId, "raw-new-token", null, null);

        Assert.Equal(authId, result.AuthorizationId);
        Assert.True(result.SlidingExtended);
        Assert.NotNull(result.RefreshTokenExpiresAt);
        // Should be capped at absolute expiry (use UTC for comparison)
        var expectedAbsolute = new DateTimeOffset(DateTime.SpecifyKind(absolute, DateTimeKind.Utc));
        Assert.Equal(expectedAbsolute, result.RefreshTokenExpiresAt);
    }

    [Fact]
    public async Task RefreshAsync_OnRevokedSession_ReturnsNullAndNoRotation()
    {
        var userId = Guid.NewGuid();
        var authId = "auth-revoked-1";
        _db.UserSessions.Add(new UserSession
        {
            UserId = userId,
            AuthorizationId = authId,
            CurrentRefreshTokenHash = "hash_current",
            RevokedUtc = _fixedTime.DateTime
        });
        await _db.SaveChangesAsync(CancellationToken.None);

        var result = await _service.RefreshAsync(userId, authId, "raw-any", null, null);

        Assert.Equal(authId, result.AuthorizationId);
        Assert.False(result.ReuseDetected);
        Assert.False(result.SlidingExtended);
        Assert.Null(result.AccessTokenExpiresAt);
        Assert.Null(result.RefreshTokenExpiresAt);
    }

    [Fact]
    public async Task RefreshAsync_EmitsAuditEvents_ForRotationAndSlidingExtension()
    {
        var userId = Guid.NewGuid();
        var authId = "auth-audit-1";
        var sliding = _fixedTime.DateTime.AddMinutes(2);
        _db.UserSessions.Add(new UserSession
        {
            UserId = userId,
            AuthorizationId = authId,
            CurrentRefreshTokenHash = "hash_old",
            AbsoluteExpiresUtc = _fixedTime.DateTime.AddHours(1),
            SlidingExpiresUtc = sliding,
            SlidingExtensionCount = 0
        });
        await _db.SaveChangesAsync(CancellationToken.None);

        var before = _db.AuditEvents.Count();
        var result = await _service.RefreshAsync(userId, authId, "raw-new-token", "127.0.0.1", "UA");
        var after = _db.AuditEvents.Count();

        Assert.Equal(authId, result.AuthorizationId);
        Assert.True(after >= before + 1); // At least rotation event
        Assert.Contains(_db.AuditEvents.Select(a => a.EventType), t => t.Contains("RefreshTokenRotated") || t.Contains("SlidingExpirationExtended"));
    }

    [Fact]
    public async Task RevokeChainAsync_RevokesSessionAndReturnsCounts()
    {
        var userId = Guid.NewGuid();
        var authId = "auth-revoke-chain-1";
        _db.UserSessions.Add(new UserSession
        {
            UserId = userId,
            AuthorizationId = authId,
            CurrentRefreshTokenHash = "hash_current",
            AbsoluteExpiresUtc = _fixedTime.DateTime.AddHours(1)
        });
        await _db.SaveChangesAsync(CancellationToken.None);

        var result = await _service.RevokeChainAsync(userId, authId, "User requested logout");

        Assert.Equal(authId, result.AuthorizationId);
        Assert.True(result.TokensRevoked >= 1);
        Assert.False(result.AlreadyRevoked);
    }
}
