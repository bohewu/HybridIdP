using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Server.AspNetCore;
using Web.IdP.Services;

namespace Web.IdP.Controllers.Connect;

public class DeviceController : Controller
{
    private readonly IDeviceFlowService _deviceFlowService;

    public DeviceController(IDeviceFlowService deviceFlowService)
    {
        _deviceFlowService = deviceFlowService;
    }

    [HttpGet("~/connect/verify")]
    [Authorize, IgnoreAntiforgeryToken] // Keep attributes from original
    public async Task<IActionResult> Verify()
    {
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var vm = await _deviceFlowService.PrepareVerificationViewModelAsync(result);
        return View(vm);
    }

    [HttpPost("~/connect/verify")]
    [Authorize, IgnoreAntiforgeryToken]
    public async Task<IActionResult> Verify(string? user_code) 
    {
        // Enforce user_code binding if not present in route/query but submitted via form
        // However, OpenIddict middleware might extract it?
        // AuthenticateAsync() should have it if it was in query or if middleware parsed form.
        // But if form submission, let's rely on what service does.
        // Wait, ProcessVerificationAsync uses AuthenticateAsync result. 
        // If AuthenticateAsync fails (no user_code found), service returns VM with error.
        
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var actionResult = await _deviceFlowService.ProcessVerificationAsync(User, result);

        if (actionResult is SignInResult)
        {
            return actionResult;
        }

        if (actionResult is BadRequestObjectResult badRequest && badRequest.Value is DeviceVerificationViewModel vm)
        {
            return View(vm);
        }

        return actionResult;
    }

    [HttpGet("~/connect/verify/success")]
    [Authorize]
    public IActionResult Success()
    {
        return View();
    }
}
