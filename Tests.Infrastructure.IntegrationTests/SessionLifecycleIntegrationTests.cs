using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenIddict.Abstractions;
using Xunit;

namespace Tests.Infrastructure.IntegrationTests;

/// <summary>
/// Integration-level verification of session lifecycle persistence mutations and audit events.
/// Uses real EF Core in-memory context, exercising RefreshAsync and RevokeChainAsync.
/// </summary>
public class SessionLifecycleIntegrationTests
{
    private readonly ApplicationDbContext _db;
    private readonly SessionService _service;
    private readonly Mock<IOpenIddictAuthorizationManager> _authz = new();
    private readonly Mock<IOpenIddictApplicationManager> _apps = new();
    private readonly Mock<IOpenIddictTokenManager> _tokens = new();

    public SessionLifecycleIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _service = new SessionService(_authz.Object, _apps.Object, _tokens.Object, _db);
    }

    [Fact]
    public async Task RefreshAsync_PersistsRotationAndAuditEvents()
    {
        var userId = Guid.NewGuid();
        var authId = "auth-int-rotate-1";
        _db.UserSessions.Add(new UserSession
        {
            UserId = userId,
            AuthorizationId = authId,
            CurrentRefreshTokenHash = "hash_old",
            SlidingExpiresUtc = DateTime.UtcNow.AddMinutes(10),
            AbsoluteExpiresUtc = DateTime.UtcNow.AddHours(1)
        });
        await _db.SaveChangesAsync(CancellationToken.None);

        var result = await _service.RefreshAsync(userId, authId, "raw-new-token", "127.0.0.1", "UA");

        // Verify session updated
        var session = await _db.UserSessions.FirstAsync(s => s.AuthorizationId == authId);
        Assert.NotNull(session.CurrentRefreshTokenHash);
        Assert.Equal("hash_old", session.PreviousRefreshTokenHash);
        Assert.True(session.SlidingExpiresUtc > DateTime.UtcNow.AddMinutes(20 - 10)); // extended beyond original 10 min window

        // Audit events recorded (at least rotation + maybe sliding extension)
        var audit = _db.AuditEvents.Where(a => a.EventType.Contains("RefreshTokenRotated") || a.EventType.Contains("SlidingExpirationExtended"))
            .ToList();
        Assert.NotEmpty(audit);
    }

    [Fact]
    public async Task RevokeChainAsync_PersistsRevocationAndAuditEvent()
    {
        var userId = Guid.NewGuid();
        var authId = "auth-int-revoke-1";
        _db.UserSessions.Add(new UserSession
        {
            UserId = userId,
            AuthorizationId = authId,
            CurrentRefreshTokenHash = "hash_current",
            SlidingExpiresUtc = DateTime.UtcNow.AddMinutes(30)
        });
        await _db.SaveChangesAsync(CancellationToken.None);

        // Mock token revocation count
        _authz.Setup(a => a.FindByIdAsync(authId, It.IsAny<CancellationToken>())).ReturnsAsync(new object());
        _tokens.Setup(t => t.RevokeByAuthorizationIdAsync(authId, It.IsAny<CancellationToken>())).Returns(new ValueTask<long>(2));

        var result = await _service.RevokeChainAsync(userId, authId, "logout");
        Assert.Equal(authId, result.AuthorizationId);
        Assert.True(result.TokensRevoked >= 1);
        Assert.False(result.AlreadyRevoked);

        var session = await _db.UserSessions.FirstAsync(s => s.AuthorizationId == authId);
        Assert.NotNull(session.RevokedUtc);
        Assert.Equal("logout", session.RevocationReason);

        var auditRevoked = _db.AuditEvents.Any(a => a.EventType == Core.Domain.Constants.AuditEventTypes.SessionRevoked && a.Details!.Contains(authId));
        Assert.True(auditRevoked);
    }
}