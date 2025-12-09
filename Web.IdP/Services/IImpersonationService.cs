using System.Security.Claims;

namespace Web.IdP.Services;

public interface IImpersonationService
{
    /// <summary>
    /// Validates and creates a principal for impersonation.
    /// </summary>
    /// <param name="currentUserId">The ID of the admin user initiating impersonation.</param>
    /// <param name="targetUserId">The ID of the user to impersonate.</param>
    /// <returns>Success status, the new principal (if success), and error message (if failure).</returns>
    Task<(bool Success, ClaimsPrincipal? Principal, string? Error)> StartImpersonationAsync(Guid currentUserId, Guid targetUserId);

    /// <summary>
    /// Validates and restores the original admin principal from an impersonated principal.
    /// </summary>
    /// <param name="currentPrincipal">The current impersonation principal.</param>
    /// <returns>Success status, the restored admin principal (if success), and error message (if failure).</returns>
    Task<(bool Success, ClaimsPrincipal? Principal, string? Error)> RevertImpersonationAsync(ClaimsPrincipal currentPrincipal);
}
