using Core.Application.DTOs;

namespace Core.Application;

/// <summary>
/// Defines the contract for a service that handles user authentication logic.
/// </summary>
public interface ILoginService
{
    /// <summary>
    /// Authenticates a user based on their login and password.
    /// </summary>
    /// <param name="login">The user's login identifier (username or email).</param>
    /// <param name="password">The user's password.</param>
    /// <returns>A <see cref="LoginResult"/> indicating the outcome of the authentication attempt.</returns>
    Task<LoginResult> AuthenticateAsync(string login, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a user can link a specific external login provider (e.g., enforces limits).
    /// </summary>
    /// <param name="user">The application user.</param>
    /// <param name="provider">The provider name (e.g. Google).</param>
    /// <returns>A tuple indicating success and an error message if failed.</returns>
    Task<(bool Succeeded, string? Error)> CanLinkExternalLoginAsync(Core.Domain.ApplicationUser user, string provider, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates whether an existing user can complete external sign-in without
    /// affecting password failure counters.
    /// </summary>
    /// <param name="user">The application user resolved from external login.</param>
    /// <returns>A <see cref="LoginResult"/> describing whether sign-in may continue.</returns>
    Task<LoginResult> ValidateExternalUserSignInAsync(Core.Domain.ApplicationUser user, CancellationToken cancellationToken = default);
}
