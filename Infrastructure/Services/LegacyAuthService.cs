using Core.Application;
using Core.Application.DTOs;

namespace Infrastructure.Services;

/// <summary>
/// Development stub for legacy authentication. Accepts any username when password matches a fixed dev secret.
/// Replace with real HTTP API integration in later phases.
/// </summary>
public class LegacyAuthService : ILegacyAuthService
{
    private const string DevPassword = "LegacyDev@123"; // dev-only secret

    public Task<LegacyUserDto> ValidateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return Task.FromResult(new LegacyUserDto { IsAuthenticated = false });
        }

        // Dev rule: authenticate if password matches the dev secret
        if (password == DevPassword)
        {
            var dto = new LegacyUserDto
            {
                IsAuthenticated = true,
                ExternalId = $"legacy:{username.ToLowerInvariant()}",
                FullName = $"Legacy {username}",
                Department = "IT",
                Email = username,
                Phone = null
            };
            return Task.FromResult(dto);
        }

        return Task.FromResult(new LegacyUserDto { IsAuthenticated = false });
    }
}
