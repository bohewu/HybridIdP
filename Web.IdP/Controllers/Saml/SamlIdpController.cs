using Core.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Web.IdP.Services.Saml;
using Web.IdP.Services.Saml.Idp;

namespace Web.IdP.Controllers.Saml;

[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SamlIdpController(SamlFederationRuntime runtime,
    ILogger<SamlIdpController> logger, IStringLocalizer<SharedResource> localizer) : Controller
{
    [HttpGet(SamlProfile.IdpMetadataPath)]
    public IActionResult Metadata() => runtime.Options.Idp.Enabled
        ? Content(HttpContext.RequestServices.GetRequiredService<SamlIdpProtocol>().Metadata(), "application/samlmetadata+xml")
        : NotFound();

    [HttpGet(SamlProfile.IdpSingleSignOnPath)]
    public Task<IActionResult> SingleSignOn(CancellationToken ct) => ExecuteAsync(
        flow => flow.BeginAsync(HttpContext, ct), ct);

    [HttpGet(SamlProfile.IdpContinuePath)]
    public Task<IActionResult> Continue([FromQuery] string? state, CancellationToken ct) => ExecuteAsync(
        flow => flow.ContinueAsync(HttpContext, state, localizer["Next"], ct), ct);

    [HttpGet("/saml/idp/post.js")]
    public IActionResult PostScript() => runtime.Options.Idp.Enabled
        ? Content("document.getElementById('saml-post').submit();", "application/javascript") : NotFound();

    private async Task<IActionResult> ExecuteAsync(Func<SamlIdpFlow, Task<IActionResult>> action, CancellationToken ct)
    {
        if (!runtime.Options.Idp.Enabled) return NotFound();
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!Request.IsHttps) return BadRequest();
        try { return await action(HttpContext.RequestServices.GetRequiredService<SamlIdpFlow>()); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Neither the SAML message, cookie, private certificate, nor identity is logged.
            logger.LogWarning("SAML IdP ceremony denied ({FailureType}).", ex.GetType().Name);
            return BadRequest();
        }
    }
}
