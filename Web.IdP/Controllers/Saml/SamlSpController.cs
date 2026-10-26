using Core.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Web.IdP.Services.Saml;
using Web.IdP.Services.Saml.Sp;

namespace Web.IdP.Controllers.Saml;

[AllowAnonymous]
public sealed class SamlSpController(SamlFederationRuntime runtime, ILogger<SamlSpController> logger) : Controller
{
    [HttpGet(SamlProfile.SpMetadataPath)]
    public IActionResult Metadata()
    {
        if (!runtime.Options.Sp.Enabled) return NotFound();
        return Content(HttpContext.RequestServices.GetRequiredService<SamlSpService>().Metadata(), "application/samlmetadata+xml");
    }

    [HttpPost(SamlProfile.SpAssertionConsumerServicePath)]
    [IgnoreAntiforgeryToken] // Cross-site protocol POST is bound by protected browser nonce + one-use request state.
    [EnableRateLimiting("login")]
    [RequestSizeLimit(256 * 1024)]
    [RequestFormLimits(ValueCountLimit = 2, KeyLengthLimit = 32, ValueLengthLimit = SamlSpResponseValidator.MaximumEncodedResponseLength)]
    public async Task<IActionResult> AssertionConsumerService(CancellationToken cancellationToken)
    {
        if (!runtime.Options.Sp.Enabled) return NotFound();
        Response.Headers.CacheControl = "no-store";
        try
        {
            if (!Request.IsHttps || !Request.HasFormContentType || Request.ContentType?.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) != true)
                return BadRequest();
            var form = await Request.ReadFormAsync(cancellationToken);
            if (form.Count != 2 || form["SAMLResponse"].Count != 1 || form["RelayState"].Count != 1)
                return BadRequest();
            var callback = await HttpContext.RequestServices.GetRequiredService<SamlSpService>()
                .CompleteAsync(HttpContext, form["SAMLResponse"].ToString(), form["RelayState"].ToString(), cancellationToken);
            return LocalRedirect(callback);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("SAML SP response was rejected.");
            return BadRequest();
        }
    }
}
