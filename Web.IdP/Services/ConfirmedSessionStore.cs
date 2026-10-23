using Microsoft.AspNetCore.Session;
using Microsoft.Extensions.Caching.Distributed;

namespace Web.IdP.Services;

// Session contains short-lived ceremony/operation records. Confirm IO at the
// access boundary, before a helper can return proof or a caller can issue cookies.
public sealed class ConfirmedSessionStore(IDistributedCache cache, ILoggerFactory loggerFactory) : ISessionStore
{
    public static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(5);

    public ISession Create(string sessionKey, TimeSpan idleTimeout, TimeSpan ioTimeout,
        Func<bool> tryEstablishSession, bool isNewSessionKey) =>
        new ConfirmedSession(new DistributedSession(cache, sessionKey, idleTimeout,
            ioTimeout, tryEstablishSession, loggerFactory, isNewSessionKey), ioTimeout);

    private sealed class ConfirmedSession(ISession inner, TimeSpan timeout) : ISession
    {
        private bool _loaded;
        private bool _failed;
        public bool IsAvailable { get { EnsureLoaded(); return inner.IsAvailable; } }
        public string Id { get { EnsureLoaded(); return inner.Id; } }
        public IEnumerable<string> Keys { get { EnsureLoaded(); return inner.Keys; } }

        public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? value)
        {
            EnsureLoaded();
            return inner.TryGetValue(key, out value);
        }

        public void Set(string key, byte[] value)
        {
            EnsureLoaded();
            inner.Set(key, value);
            ConfirmWrite();
        }

        public void Remove(string key)
        {
            EnsureLoaded();
            if (!inner.TryGetValue(key, out _)) return;
            inner.Remove(key);
            ConfirmWrite();
        }

        public void Clear()
        {
            EnsureLoaded();
            if (!inner.Keys.Any()) return;
            inner.Clear();
            ConfirmWrite();
        }

        public async Task LoadAsync(CancellationToken cancellationToken = default)
        {
            if (_failed) throw new SessionStateUnavailableException();
            if (_loaded) return;
            await ConfirmAsync(inner.LoadAsync, cancellationToken);
            _loaded = true;
        }

        // Mutations are already committed. In particular, SessionMiddleware must
        // never retry an unconfirmed mutation after the failure response is sent.
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        private void EnsureLoaded() => LoadAsync().GetAwaiter().GetResult();
        private void ConfirmWrite() => ConfirmAsync(inner.CommitAsync, CancellationToken.None).GetAwaiter().GetResult();

        private async Task ConfirmAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            try
            {
                // Some cache providers do not observe cancellation while awaiting
                // network IO. Bound the caller's wait as well as passing the token.
                await operation(deadline.Token).WaitAsync(deadline.Token);
            }
            catch (Exception exception)
            {
                _failed = true;
                if (cancellationToken.IsCancellationRequested) throw;
                throw new SessionStateUnavailableException(exception);
            }
        }
    }
}

public sealed class SessionStateUnavailableException(Exception? inner = null)
    : Exception("Session state could not be confirmed.", inner);
