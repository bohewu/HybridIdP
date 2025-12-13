using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace Web.IdP.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
public class LanguageController : ControllerBase
{
    [HttpPost("set")]
    public IActionResult Set([FromBody] SetLanguageRequest request)
    {
        if (string.IsNullOrEmpty(request.Culture) || 
            !IsCultureSupported(request.Culture))
        {
            return BadRequest("Unsupported culture.");
        }

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(request.Culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1) }
        );

        return Ok();
    }

    private bool IsCultureSupported(string culture)
    {
        // In a real app, you might get this from configuration or a service
        var supportedCultures = new[] { "en-US", "zh-TW" };
        return supportedCultures.Contains(culture, StringComparer.InvariantCultureIgnoreCase);
    }
}

public class SetLanguageRequest
{
    public string Culture { get; set; } = string.Empty;
}
