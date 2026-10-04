using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Server.AspNetCore;
using Web.IdP.Attributes;
using Web.IdP.Helpers;
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
    [Authorize]
    public async Task<IActionResult> Verify()
    {
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var vm = await _deviceFlowService.PrepareVerificationViewModelAsync(result);
        ViewData["DeviceVerificationIntent"] = DeviceVerificationSession.Issue(
            HttpContext.Session,
            User,
            result);
        return View(vm);
    }

    [HttpPost("~/connect/verify")]
    [Authorize, ValidateCsrfForCookies]
    public async Task<IActionResult> Verify(string? user_code)
    {
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var intent = Request.Form[DeviceVerificationSession.FormFieldName].ToString();
        if (!DeviceVerificationSession.TryConsume(
                HttpContext.Session,
                User,
                result,
                intent))
        {
            var expiredViewModel = await _deviceFlowService.PrepareVerificationViewModelAsync(result);
            ViewData["DeviceVerificationInteractionExpired"] = true;
            ViewData["DeviceVerificationIntent"] = DeviceVerificationSession.Issue(
                HttpContext.Session,
                User,
                result);
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(expiredViewModel);
        }

        var actionResult = await _deviceFlowService.ProcessVerificationAsync(User, result);

        if (actionResult is SignInResult)
        {
            return actionResult;
        }

        if (actionResult is BadRequestObjectResult badRequest && badRequest.Value is DeviceVerificationViewModel vm)
        {
            ViewData["DeviceVerificationIntent"] = DeviceVerificationSession.Issue(
                HttpContext.Session,
                User,
                result);
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
