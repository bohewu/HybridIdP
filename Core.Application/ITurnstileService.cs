namespace Core.Application;

public interface ITurnstileService
{
    Task<bool> ValidateTokenAsync(string token, string? remoteIp = null);
}
