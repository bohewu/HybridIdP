namespace Core.Application.DTOs;

/// <summary>
/// Represents the result of a login attempt.
/// </summary>
public class LoginResult
{
    public LoginStatus Status { get; }
    public Domain.ApplicationUser? User { get; }
    public string? Message { get; }

    private LoginResult(LoginStatus status, Domain.ApplicationUser? user = null, string? message = null)
    {
        Status = status;
        User = user;
        Message = message;
    }

    public bool IsSuccess => Status == LoginStatus.Success || Status == LoginStatus.LegacySuccess;

    public static LoginResult Success(Domain.ApplicationUser user) => new(LoginStatus.Success, user);
    public static LoginResult LegacySuccess(Domain.ApplicationUser user) => new(LoginStatus.LegacySuccess, user);
    public static LoginResult InvalidCredentials() => new(LoginStatus.InvalidCredentials);
    public static LoginResult LockedOut() => new(LoginStatus.LockedOut);
    public static LoginResult PersonInactive(string message = "Person is not active") => new(LoginStatus.PersonInactive, null, message);
}

/// <summary>
/// Defines the possible outcomes of a login attempt.
/// </summary>
public enum LoginStatus
{
    /// <summary>
    /// The user was successfully authenticated with a local account.
    /// </summary>
    Success,
    /// <summary>
    /// The provided credentials are invalid.
    /// </summary>
    InvalidCredentials,
    /// <summary>
    /// The account is currently locked out.
    /// </summary>
    LockedOut,
    /// <summary>
    /// The user was successfully authenticated via the legacy system and provisioned.
    /// </summary>
    LegacySuccess,
    /// <summary>
    /// The user's associated Person is not active (terminated, suspended, pending, or outside valid date range).
    /// Phase 18: Personnel Lifecycle Management
    /// </summary>
    PersonInactive
}

