using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Web.IdP.Services;

public interface IDeviceFlowService
{
    Task<DeviceVerificationViewModel> PrepareVerificationViewModelAsync(AuthenticateResult authenticateResult);
    Task<IActionResult> ProcessVerificationAsync(ClaimsPrincipal user, AuthenticateResult authenticateResult);
}
