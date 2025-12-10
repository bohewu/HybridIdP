using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Server.AspNetCore;
using Web.IdP.Services;

namespace Web.IdP.Controllers.Admin;

/// <summary>
/// OpenID Connect UserInfo endpoint
/// Returns user claims based on the granted scopes
/// Requires 'openid' scope per OIDC specification
/// </summary>
[ApiController]
public class UserinfoController : ControllerBase
{
    private readonly IUserInfoService _userInfoService;

    public UserinfoController(IUserInfoService userInfoService)
    {
        _userInfoService = userInfoService;
    }

    [Authorize(
        AuthenticationSchemes = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
        Policy = "RequireScope:openid")]
    [HttpGet("~/connect/userinfo")]
    [HttpPost("~/connect/userinfo")]
    public async Task<IActionResult> Userinfo()
    {
        var userInfo = await _userInfoService.GetUserInfoAsync(User);
        return Ok(userInfo);
    }
}
