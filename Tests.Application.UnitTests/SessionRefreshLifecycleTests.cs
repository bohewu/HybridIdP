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