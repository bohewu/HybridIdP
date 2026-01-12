using System.Security.Claims;
using Core.Domain.Constants;

namespace Web.IdP.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Retrieves the Person ID from the current user's claims.
    /// </summary>
    /// <param name="user">The ClaimsPrincipal instance.</param>
    /// <returns>The Person ID if found and valid; otherwise, null.</returns>
    public static Guid? GetCurrentPersonId(this ClaimsPrincipal user)
    {
        var personIdClaim = user.FindFirst(AuthConstants.Claims.PersonId)?.Value;
        return Guid.TryParse(personIdClaim, out var personId) ? personId : null;
    }

    /// <summary>
    /// Checks if the current user is in the Admin role.
    /// </summary>
    /// <param name="user">The ClaimsPrincipal instance.</param>
    /// <returns>True if the user is an Admin; otherwise, false.</returns>
    public static bool IsAdmin(this ClaimsPrincipal user)
    {
        return user.IsInRole(AuthConstants.Roles.Admin);
    }
}
