using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Server.AspNetCore;
using Web.IdP.Services;

namespace Web.IdP.Controllers;

public class RevocationController : Controller
{
    private readonly IRevocationService _revocationService;

    public RevocationController(IRevocationService revocationService)
    {
        _revocationService = revocationService;
    }

    [HttpPost("~/connect/revoke")]
    [IgnoreAntiforgeryToken]
    [Produces("application/json")]
    public async Task<IActionResult> Revoke()
    {
        var request = HttpContext.GetOpenIddictServerRequest();
        if (request == null)
        {
            return BadRequest("The OpenID Connect request cannot be retrieved.");
        }

        return await _revocationService.HandleRevocationRequestAsync(request);
    }
}
