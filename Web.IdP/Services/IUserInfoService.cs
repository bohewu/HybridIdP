using System.Security.Claims;

namespace Web.IdP.Services;

public interface IUserInfoService
{
    Task<Dictionary<string, object>> GetUserInfoAsync(ClaimsPrincipal principal);
}
