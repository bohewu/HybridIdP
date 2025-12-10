using Core.Application;

namespace Web.IdP.Services;

public class TurnstileStateService : ITurnstileStateService
{
    private volatile bool _isAvailable = true; // Default to true (optimistic)

    public bool IsAvailable => _isAvailable;

    public void SetAvailable(bool isAvailable)
    {
        _isAvailable = isAvailable;
    }
}
