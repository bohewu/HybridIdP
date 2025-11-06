using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace Web.IdP.Api;

[ApiController]
[Route("api/[controller]")]
public class LanguageController : ControllerBase
{
    [HttpPost("set")]
    public IActionResult SetLanguage([FromBody] SetLanguageRequest request)
    {
        if (string.IsNullOrEmpty(request.Culture))
        {
            return BadRequest("Culture is required");
        }

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(request.Culture)),
            new CookieOptions 
            { 
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                SameSite = SameSiteMode.Lax
            }
        );

        return Ok(new { culture = request.Culture });
    }
}

public record SetLanguageRequest(string Culture);
