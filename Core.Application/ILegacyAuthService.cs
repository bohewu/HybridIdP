using Core.Application.DTOs;

namespace Core.Application;

public interface ILegacyAuthService
{
    Task<LegacyUserDto> ValidateAsync(string username, string password, CancellationToken cancellationToken = default);
}
