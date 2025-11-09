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
    Task<LoginResult> AuthenticateAsync(string login, string password);
}
