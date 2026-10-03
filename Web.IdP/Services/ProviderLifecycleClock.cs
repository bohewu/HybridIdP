namespace Web.IdP.Services;

/// <summary>
/// Uses the host's trusted TimeProvider. Unreadable/non-UTC time or an observed
/// rollback latches denial for this process. Host clock trust is an operational prerequisite.
/// </summary>
public sealed class ProviderLifecycleClock(TimeProvider timeProvider)
{
    private readonly object _gate = new();
    private DateTimeOffset? _lastUtc;
    private bool _unhealthy;

    public bool TryGetUtcNow(out DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            utcNow = default;
            if (_unhealthy) return false;
            try
            {
                var current = timeProvider.GetUtcNow();
                if (current.Offset != TimeSpan.Zero || current == DateTimeOffset.MinValue ||
                    current == DateTimeOffset.MaxValue || (_lastUtc.HasValue && current < _lastUtc.Value))
                {
                    _unhealthy = true;
                    return false;
                }
                _lastUtc = utcNow = current;
                return true;
            }
            catch
            {
                _unhealthy = true;
                return false;
            }
        }
    }
}
