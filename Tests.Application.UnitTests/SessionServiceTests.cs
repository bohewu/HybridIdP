using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.DTOs;
using Infrastructure.Services;
using Moq;
using OpenIddict.Abstractions;
using Xunit;
using Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Tests.Application.UnitTests;

public class SessionServiceTests
{
    private readonly Mock<IOpenIddictAuthorizationManager> _authz;
    private readonly Mock<IOpenIddictApplicationManager> _apps;
    private readonly Mock<IOpenIddictTokenManager> _tokens;
    private readonly SessionService _service;
    private readonly ApplicationDbContext _dbContext;

    public SessionServiceTests()
    {
        _authz = new Mock<IOpenIddictAuthorizationManager>();
        _apps = new Mock<IOpenIddictApplicationManager>();
        _tokens = new Mock<IOpenIddictTokenManager>();

        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);

        _service = new SessionService(_authz.Object, _apps.Object, _tokens.Object, _dbContext);
    }

    private sealed class AsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        private readonly IEnumerable<T> _items;
        public AsyncEnumerable(IEnumerable<T> items) => _items = items;
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new AsyncEnumerator(_items.GetEnumerator());
        private sealed class AsyncEnumerator : IAsyncEnumerator<T>
        {
            private readonly IEnumerator<T> _enumerator;
            public AsyncEnumerator(IEnumerator<T> enumerator) => _enumerator = enumerator;
            public T Current => _enumerator.Current;
            public ValueTask DisposeAsync() { _enumerator.Dispose(); return ValueTask.CompletedTask; }
            public ValueTask<bool> MoveNextAsync() => new(_enumerator.MoveNext());
        }
    }

    [Fact]
    public async Task ListSessionsAsync_ReturnsMappedAuthorizations()
    {
        var userId = Guid.NewGuid();
        var a1 = new object();
        var a2 = new object();

        _authz.Setup(m => m.FindAsync(
            It.Is<string>(s => s == userId.ToString()),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<System.Collections.Immutable.ImmutableArray<string>>(),
            It.IsAny<CancellationToken>()))
            .Returns(new AsyncEnumerable<object>(new[] { a1, a2 }));

        _authz.Setup(m => m.GetIdAsync(a1, It.IsAny<CancellationToken>())).ReturnsAsync("auth-1");
        _authz.Setup(m => m.GetIdAsync(a2, It.IsAny<CancellationToken>())).ReturnsAsync("auth-2");

        var list = (await _service.ListSessionsAsync(userId)).ToList();
        Assert.Equal(2, list.Count);
        Assert.Contains(list, s => s.AuthorizationId == "auth-1");
        Assert.Contains(list, s => s.AuthorizationId == "auth-2");
    }

    [Fact]
    public async Task RevokeSessionAsync_ReturnsFalse_WhenNotOwnedByUser()
    {
        var userId = Guid.NewGuid();
        var auth = new object();
        _authz.Setup(m => m.FindByIdAsync("auth-x", It.IsAny<CancellationToken>()))
            .ReturnsAsync(auth);
        _authz.Setup(m => m.GetSubjectAsync(auth, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString()); // different owner

        var ok = await _service.RevokeSessionAsync(userId, "auth-x");
        Assert.False(ok);
    }

    [Fact]
    public async Task RevokeAllSessionsAsync_ReturnsCount()
    {
        var userId = Guid.NewGuid();
        var a1 = new object();
        var a2 = new object();
        _authz.Setup(m => m.FindAsync(
            It.Is<string>(s => s == userId.ToString()),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<System.Collections.Immutable.ImmutableArray<string>>(),
            It.IsAny<CancellationToken>()))
            .Returns(new AsyncEnumerable<object>(new[] { a1, a2 }));
        _authz.Setup(m => m.TryRevokeAsync(a1, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _authz.Setup(m => m.TryRevokeAsync(a2, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _authz.Setup(m => m.GetIdAsync(a1, It.IsAny<CancellationToken>())).ReturnsAsync("auth-1");
        _authz.Setup(m => m.GetIdAsync(a2, It.IsAny<CancellationToken>())).ReturnsAsync("auth-2");
        _tokens.Setup(m => m.RevokeByAuthorizationIdAsync("auth-1", It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<long>(1));
        _tokens.Setup(m => m.RevokeByAuthorizationIdAsync("auth-2", It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<long>(1));

        var count = await _service.RevokeAllSessionsAsync(userId);
        Assert.Equal(2, count);
        _tokens.Verify(m => m.RevokeByAuthorizationIdAsync("auth-1", It.IsAny<CancellationToken>()), Times.Once);
        _tokens.Verify(m => m.RevokeByAuthorizationIdAsync("auth-2", It.IsAny<CancellationToken>()), Times.Once);
    }

    #region ListSessionsAsync - Additional Tests

    [Fact]
    public async Task ListSessionsAsync_ReturnsEmptyList_WhenUserHasNoSessions()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _authz.Setup(m => m.FindAsync(
            It.Is<string>(s => s == userId.ToString()),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<System.Collections.Immutable.ImmutableArray<string>>(),
            It.IsAny<CancellationToken>()))
            .Returns(new AsyncEnumerable<object>(Array.Empty<object>()));

        // Act
        var sessions = await _service.ListSessionsAsync(userId);

        // Assert
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task ListSessionsAsync_ReturnsMultipleSessions_WhenUserHasManySessions()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var authorizations = Enumerable.Range(1, 5).Select(_ => new object()).ToList();
        var authIds = new Queue<string>(new[] { "auth-1", "auth-2", "auth-3", "auth-4", "auth-5" });

        _authz.Setup(m => m.FindAsync(
            It.Is<string>(s => s == userId.ToString()),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<System.Collections.Immutable.ImmutableArray<string>>(),
            It.IsAny<CancellationToken>()))
            .Returns(new AsyncEnumerable<object>(authorizations));
        _authz.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => authIds.Dequeue());

        // Act
        var sessions = (await _service.ListSessionsAsync(userId)).ToList();

        // Assert
        Assert.Equal(5, sessions.Count);
        Assert.Contains(sessions, s => s.AuthorizationId == "auth-1");
        Assert.Contains(sessions, s => s.AuthorizationId == "auth-5");
    }

    [Fact]
    public async Task ListSessionsAsync_HandlesNullAuthorizationId_Gracefully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var auth = new object();
        _authz.Setup(m => m.FindAsync(
            It.Is<string>(s => s == userId.ToString()),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<System.Collections.Immutable.ImmutableArray<string>>(),
            It.IsAny<CancellationToken>()))
            .Returns(new AsyncEnumerable<object>(new[] { auth }));
        _authz.Setup(m => m.GetIdAsync(auth, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var sessions = (await _service.ListSessionsAsync(userId)).ToList();

        // Assert
        Assert.Single(sessions);
        Assert.Equal(string.Empty, sessions[0].AuthorizationId);
    }

    [Fact]
    public async Task ListSessionsAsync_IncludesApplicationInfoAndExpiresAt_WhenTokensPresent()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var auth = new object();
        var app = new object();
        var token = new object();

        _authz.Setup(m => m.FindAsync(
            It.Is<string>(s => s == userId.ToString()),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<System.Collections.Immutable.ImmutableArray<string>>(),
            It.IsAny<CancellationToken>()))
            .Returns(new AsyncEnumerable<object>(new[] { auth }));
        _authz.Setup(m => m.GetIdAsync(auth, It.IsAny<CancellationToken>())).ReturnsAsync("auth-1");
        _authz.Setup(m => m.GetApplicationIdAsync(auth, It.IsAny<CancellationToken>())).ReturnsAsync("app-1");
        _authz.Setup(m => m.GetCreationDateAsync(auth, It.IsAny<CancellationToken>())).ReturnsAsync(DateTimeOffset.UtcNow);
        _authz.Setup(m => m.GetStatusAsync(auth, It.IsAny<CancellationToken>())).ReturnsAsync(OpenIddictConstants.Statuses.Valid);

        _apps.Setup(m => m.FindByIdAsync("app-1", It.IsAny<CancellationToken>())).ReturnsAsync(app);
        _apps.Setup(m => m.GetClientIdAsync(app, It.IsAny<CancellationToken>())).ReturnsAsync("testclient-app");
        _apps.Setup(m => m.GetDisplayNameAsync(app, It.IsAny<CancellationToken>())).ReturnsAsync("Test App");

        _tokens.Setup(m => m.FindAsync(
            It.Is<string>(s => s == userId.ToString()),
            It.Is<string?>(c => c == "testclient-app"),
            It.IsAny<string?>(),
            It.IsAny<string?>()))
            .Returns(new AsyncEnumerable<object>(new[] { token }));
        _tokens.Setup(m => m.GetAuthorizationIdAsync(token, It.IsAny<CancellationToken>())).ReturnsAsync("auth-1");
        _tokens.Setup(m => m.GetStatusAsync(token, It.IsAny<CancellationToken>())).ReturnsAsync(OpenIddictConstants.Statuses.Valid);
        _tokens.Setup(m => m.GetExpirationDateAsync(token, It.IsAny<CancellationToken>())).ReturnsAsync(DateTimeOffset.UtcNow.AddHours(1));

        // Act
        var list = (await _service.ListSessionsAsync(userId)).ToList();

        // Assert
        Assert.Single(list);
        var s = list.First();
        Assert.Equal("auth-1", s.AuthorizationId);
        Assert.Equal("testclient-app", s.ClientId);
        Assert.Equal("Test App", s.ClientDisplayName);
        Assert.NotNull(s.CreatedAt);
        Assert.NotNull(s.ExpiresAt);
    }

    #endregion

    #region RevokeSessionAsync - Additional Tests

    [Fact]
    public async Task RevokeSessionAsync_ReturnsTrue_WhenSessionRevokedSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var auth = new object();
        _authz.Setup(m => m.FindByIdAsync("auth-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(auth);
        _authz.Setup(m => m.GetSubjectAsync(auth, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId.ToString());
        _authz.Setup(m => m.TryRevokeAsync(auth, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _tokens.Setup(m => m.RevokeByAuthorizationIdAsync("auth-123", It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<long>(1));

        // Act
        var result = await _service.RevokeSessionAsync(userId, "auth-123");

        // Assert
        Assert.True(result);
        _authz.Verify(m => m.TryRevokeAsync(auth, It.IsAny<CancellationToken>()), Times.Once);
        _tokens.Verify(m => m.RevokeByAuthorizationIdAsync("auth-123", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeSessionAsync_ReturnsFalse_WhenAuthorizationNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _authz.Setup(m => m.FindByIdAsync("non-existent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Act
        var result = await _service.RevokeSessionAsync(userId, "non-existent");

        // Assert
        Assert.False(result);
        _authz.Verify(m => m.TryRevokeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RevokeSessionAsync_ReturnsFalse_WhenRevocationFails()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var auth = new object();
        _authz.Setup(m => m.FindByIdAsync("auth-456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(auth);
        _authz.Setup(m => m.GetSubjectAsync(auth, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId.ToString());
        _authz.Setup(m => m.TryRevokeAsync(auth, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _service.RevokeSessionAsync(userId, "auth-456");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RevokeSessionAsync_IsCaseInsensitive_ForSubjectComparison()
    {
        // Arrange
        var userId = Guid.Parse("12345678-1234-1234-1234-123456789ABC");
        var auth = new object();
        _authz.Setup(m => m.FindByIdAsync("auth-789", It.IsAny<CancellationToken>()))
            .ReturnsAsync(auth);
        _authz.Setup(m => m.GetSubjectAsync(auth, It.IsAny<CancellationToken>()))
            .ReturnsAsync("12345678-1234-1234-1234-123456789abc"); // lowercase
        _authz.Setup(m => m.TryRevokeAsync(auth, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.RevokeSessionAsync(userId, "auth-789");

        // Assert
        Assert.True(result);
    }

    #endregion

    #region RevokeAllSessionsAsync - Additional Tests

    [Fact]
    public async Task RevokeAllSessionsAsync_ReturnsZero_WhenUserHasNoSessions()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _authz.Setup(m => m.FindAsync(
            It.Is<string>(s => s == userId.ToString()),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<System.Collections.Immutable.ImmutableArray<string>>(),
            It.IsAny<CancellationToken>()))
            .Returns(new AsyncEnumerable<object>(Array.Empty<object>()));

        // Act
        var count = await _service.RevokeAllSessionsAsync(userId);

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task RevokeAllSessionsAsync_ReturnsCorrectCount_WhenSomeRevocationsFail()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var a1 = new object();
        var a2 = new object();
        var a3 = new object();
        _authz.Setup(m => m.FindAsync(
            It.Is<string>(s => s == userId.ToString()),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<System.Collections.Immutable.ImmutableArray<string>>(),
            It.IsAny<CancellationToken>()))
            .Returns(new AsyncEnumerable<object>(new[] { a1, a2, a3 }));
        _authz.Setup(m => m.TryRevokeAsync(a1, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _authz.Setup(m => m.TryRevokeAsync(a2, It.IsAny<CancellationToken>())).ReturnsAsync(false); // Fails
        _authz.Setup(m => m.TryRevokeAsync(a3, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        var count = await _service.RevokeAllSessionsAsync(userId);

        // Assert
        Assert.Equal(2, count); // Only 2 successful revocations
    }

    [Fact]
    public async Task RevokeAllSessionsAsync_RevokesAllSessions_WhenMultipleSessions()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var authorizations = Enumerable.Range(1, 10).Select(_ => new object()).ToList();
        _authz.Setup(m => m.FindAsync(
            It.Is<string>(s => s == userId.ToString()),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<System.Collections.Immutable.ImmutableArray<string>>(),
            It.IsAny<CancellationToken>()))
            .Returns(new AsyncEnumerable<object>(authorizations));
        _authz.Setup(m => m.TryRevokeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var count = await _service.RevokeAllSessionsAsync(userId);

        // Assert
        Assert.Equal(10, count);
        _authz.Verify(m => m.TryRevokeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Exactly(10));
    }

    [Fact]
    public async Task RevokeAllSessionsAsync_FallbackSearch_WhenNoValidSessionsReturned()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var b1 = new object();
        var b2 = new object();

        // First call (status=valid) returns empty
        _authz.Setup(m => m.FindAsync(
            It.Is<string>(s => s == userId.ToString()),
            It.IsAny<string?>(),
            It.Is<string?>(st => st == OpenIddictConstants.Statuses.Valid),
            It.IsAny<string?>(),
            It.IsAny<System.Collections.Immutable.ImmutableArray<string>>(),
            It.IsAny<CancellationToken>()))
            .Returns(new AsyncEnumerable<object>(Array.Empty<object>()));

        // Second broader search (status=null) returns two items
        _authz.Setup(m => m.FindAsync(
            It.Is<string>(s => s == userId.ToString()),
            It.IsAny<string?>(),
            It.Is<string?>(st => st == null),
            It.IsAny<string?>(),
            It.IsAny<System.Collections.Immutable.ImmutableArray<string>>(),
            It.IsAny<CancellationToken>()))
            .Returns(new AsyncEnumerable<object>(new[] { b1, b2 }));

        _authz.Setup(m => m.TryRevokeAsync(b1, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _authz.Setup(m => m.TryRevokeAsync(b2, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _authz.Setup(m => m.GetIdAsync(b1, It.IsAny<CancellationToken>())).ReturnsAsync("b1");
        _authz.Setup(m => m.GetIdAsync(b2, It.IsAny<CancellationToken>())).ReturnsAsync("b2");
        _tokens.Setup(m => m.RevokeByAuthorizationIdAsync("b1", It.IsAny<CancellationToken>())).Returns(new ValueTask<long>(1));
        _tokens.Setup(m => m.RevokeByAuthorizationIdAsync("b2", It.IsAny<CancellationToken>())).Returns(new ValueTask<long>(1));

        // Act
        var count = await _service.RevokeAllSessionsAsync(userId);

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task ListSessionsAsync_PopulatesClientInfoAndDates()
    {
        var userId = Guid.NewGuid();
        var auth = new object();
        var app = new object();

        _authz.Setup(m => m.FindAsync(
            It.Is<string>(s => s == userId.ToString()),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<System.Collections.Immutable.ImmutableArray<string>>(),
            It.IsAny<CancellationToken>()))
            .Returns(new AsyncEnumerable<object>(new[] { auth }));

        _authz.Setup(m => m.GetIdAsync(auth, It.IsAny<CancellationToken>())).ReturnsAsync("auth-42");
        _authz.Setup(m => m.GetApplicationIdAsync(auth, It.IsAny<CancellationToken>())).ReturnsAsync("app-42");
        // Not all OpenIddict versions expose creation/expiration via manager; skip in unit tests
        _authz.Setup(m => m.GetStatusAsync(auth, It.IsAny<CancellationToken>())).ReturnsAsync(OpenIddict.Abstractions.OpenIddictConstants.Statuses.Valid);

        _apps.Setup(m => m.FindByIdAsync("app-42", It.IsAny<CancellationToken>())).ReturnsAsync(app);
        _apps.Setup(m => m.GetClientIdAsync(app, It.IsAny<CancellationToken>())).ReturnsAsync("test-client");
        _apps.Setup(m => m.GetDisplayNameAsync(app, It.IsAny<CancellationToken>())).ReturnsAsync("Test Client");

        var list = (await _service.ListSessionsAsync(userId)).ToList();

        Assert.Single(list);
        var s = list[0];
        Assert.Equal("auth-42", s.AuthorizationId);
        Assert.Equal("test-client", s.ClientId);
        Assert.Equal("Test Client", s.ClientDisplayName);
        Assert.Null(s.CreatedAt);
        Assert.Null(s.ExpiresAt);
        Assert.Equal("valid", s.Status);
    }

    // Token revocation for linked tokens is out-of-scope for current OpenIddict surface in this repo
    // and will be implemented in a later step if the token manager exposes a compatible API.

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task RevokeSessionAsync_ThrowsException_WhenAuthorizationManagerThrows()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _authz.Setup(m => m.FindByIdAsync("auth-error", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RevokeSessionAsync(userId, "auth-error"));
    }

    [Fact]
    public async Task ListSessionsAsync_ThrowsException_WhenAuthorizationManagerThrows()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _authz.Setup(m => m.FindAsync(
            It.Is<string>(s => s == userId.ToString()),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<System.Collections.Immutable.ImmutableArray<string>>(),
            It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("Database connection failed"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ListSessionsAsync(userId));
    }

    #endregion
}
