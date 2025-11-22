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

namespace Tests.Application.UnitTests;

/// <summary>
/// Failing (RED) tests defining expected behavior for refresh rotation, reuse detection and chain revocation.
/// Implementation will be added to SessionService to satisfy these tests.
/// </summary>
public class SessionRefreshLifecycleTests
{
    private readonly Mock<IOpenIddictAuthorizationManager> _authz = new();
    private readonly Mock<IOpenIddictApplicationManager> _apps = new();
    private readonly Mock<IOpenIddictTokenManager> _tokens = new();
    private readonly ApplicationDbContext _db;
    private readonly SessionService _service;

    public SessionRefreshLifecycleTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _service = new SessionService(_authz.Object, _apps.Object, _tokens.Object, _db);
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
            AbsoluteExpiresUtc = DateTime.UtcNow.AddHours(8),
            SlidingExpiresUtc = DateTime.UtcNow.AddMinutes(30),
            SlidingExtensionCount = 0
        });
        await _db.SaveChangesAsync(CancellationToken.None);

        // Act (will FAIL until implemented)
        var result = await _service.RefreshAsync(userId, authId, "raw-new-token", "127.0.0.1", "UA");

        // Assert desired post-conditions
        Assert.Equal(authId, result.AuthorizationId);
        Assert.True(result.RefreshTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(25)); // sliding extended
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
            AbsoluteExpiresUtc = DateTime.UtcNow.AddHours(2),
            SlidingExpiresUtc = DateTime.UtcNow.AddMinutes(20),
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
            AbsoluteExpiresUtc = DateTime.UtcNow.AddHours(2),
            SlidingExpiresUtc = DateTime.UtcNow.AddMinutes(20)
        });
        await _db.SaveChangesAsync(CancellationToken.None);

        var before = _db.AuditEvents.Count();
        var result = await _service.RefreshAsync(userId, authId, "raw-previous-token", "127.0.0.1", "UA");
        var after = _db.AuditEvents.Count();

        Assert.Equal(authId, result.AuthorizationId);
        Assert.True(result.ReuseDetected);
        // Session should now be revoked in DB
        var session = await _db.UserSessions.FindAsync(userId, authId);
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
        var absolute = DateTime.UtcNow.AddMinutes(10);
        var sliding = DateTime.UtcNow.AddMinutes(2);
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
        // Should be capped at absolute expiry
        Assert.Equal(new DateTimeOffset(absolute), result.RefreshTokenExpiresAt);
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
            RevokedUtc = DateTime.UtcNow
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
        var sliding = DateTime.UtcNow.AddMinutes(2);
        _db.UserSessions.Add(new UserSession
        {
            UserId = userId,
            AuthorizationId = authId,
            CurrentRefreshTokenHash = "hash_old",
            AbsoluteExpiresUtc = DateTime.UtcNow.AddHours(1),
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
            AbsoluteExpiresUtc = DateTime.UtcNow.AddHours(1)
        });
        await _db.SaveChangesAsync(CancellationToken.None);

        var result = await _service.RevokeChainAsync(userId, authId, "User requested logout");

        Assert.Equal(authId, result.AuthorizationId);
        Assert.True(result.TokensRevoked >= 1);
        Assert.False(result.AlreadyRevoked);
    }
}