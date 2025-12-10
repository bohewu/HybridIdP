namespace Core.Application;

public interface ITurnstileStateService
{
    bool IsAvailable { get; }
    void SetAvailable(bool isAvailable);
}
