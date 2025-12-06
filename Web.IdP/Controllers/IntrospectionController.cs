using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Server.AspNetCore;
using Web.IdP.Services;

namespace Web.IdP.Controllers;

public class IntrospectionController : Controller
{
    private readonly IIntrospectionService _introspectionService;

    public IntrospectionController(IIntrospectionService introspectionService)
    {
        _introspectionService = introspectionService;
    }

    [HttpPost("~/connect/introspect")]
    [IgnoreAntiforgeryToken]
    [Produces("application/json")]
    public async Task<IActionResult> Introspect()
    {
        var request = HttpContext.GetOpenIddictServerRequest();
        if (request == null)
        {
             return BadRequest("The OpenID Connect request cannot be retrieved.");
        }

        return await _introspectionService.HandleIntrospectionRequestAsync(request);
    }
}
