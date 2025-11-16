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

namespace Tests.Application.UnitTests;

public class SessionServiceTests
{
    private readonly Mock<IOpenIddictAuthorizationManager> _authz;
    private readonly Mock<IOpenIddictApplicationManager> _apps;
    private readonly Mock<IOpenIddictTokenManager> _tokens;
    private readonly SessionService _service;

    public SessionServiceTests()
    {
        _authz = new Mock<IOpenIddictAuthorizationManager>();
        _apps = new Mock<IOpenIddictApplicationManager>();
        _tokens = new Mock<IOpenIddictTokenManager>();
        _service = new SessionService(_authz.Object, _apps.Object, _tokens.Object);
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

        var count = await _service.RevokeAllSessionsAsync(userId);
        Assert.Equal(2, count);
    }
}
