using System.Security.Claims;
using Core.Domain;

namespace Web.IdP.Services;

public interface IClaimsEnrichmentService
{
    Task AddPermissionClaimsAsync(ClaimsIdentity identity, ApplicationUser user, string? clientId = null);
    Task AddScopeMappedClaimsAsync(ClaimsIdentity identity, ApplicationUser user, IEnumerable<string> grantedScopes);
}
