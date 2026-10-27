using System.Security.Claims;
using System.Text.Json;
using Core.Application;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Web.IdP.Services.Saml.Idp;

namespace Web.IdP.Controllers.Admin;

[ApiController]
[Route("api/admin/saml-attributes")]
[Authorize(AuthenticationSchemes = "Identity.Application")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SamlAttributesController(SamlAttributeService attributes, IAuditService audit) : ControllerBase
{
    [HttpGet("partners")]
    [HasPermission(Permissions.Settings.Read)]
    public IActionResult Partners() => Ok(new { enabled = attributes.Enabled, items = attributes.Partners() });

    [HttpGet("catalog")]
    [HasPermission(Permissions.Settings.Read)]
    public Task<IActionResult> Catalog() => ExecuteAsync(() =>
    {
        attributes.EnsureEnabled();
        return Task.FromResult<IActionResult>(Ok(new { items = SamlAttributePolicy.Catalog }));
    });

    [HttpGet("policy")]
    [HasPermission(Permissions.Settings.Read)]
    public Task<IActionResult> Policy([FromQuery] string entityId, CancellationToken ct) =>
        ExecuteAsync(async () => Ok(await attributes.GetPolicyAsync(entityId, ct)));

    [HttpPut("policy")]
    [HasPermission(Permissions.Settings.Update)]
    public Task<IActionResult> SavePolicy([FromBody] SamlAttributePolicyDto request, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var saved = await attributes.SavePolicyAsync(request, User.FindFirstValue(ClaimTypes.NameIdentifier), ct);
        await audit.LogAdministrativeEventAsync("SamlAttributePolicyUpdated", "SamlPartner", saved.EntityId,
            JsonSerializer.Serialize(new { mappingCount = saved.Mappings.Length }), ct);
        return Ok(saved);
    });

    [HttpGet("accounts")]
    [HasPermission(Permissions.Claims.Preview)]
    public Task<IActionResult> Accounts([FromQuery] string? search = null, [FromQuery] int skip = 0,
        [FromQuery] int take = 50, CancellationToken ct = default) => ExecuteAsync(async () =>
            Ok(new { items = await attributes.AccountsAsync(search, skip, take, ct) }));

    [HttpPost("preview")]
    [HasPermission(Permissions.Claims.Preview)]
    public Task<IActionResult> Preview([FromBody] SamlAttributePreviewRequest request, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var result = await attributes.ResolveAsync(request.EntityId, request.UserId, ct);
        await audit.LogAdministrativeEventAsync("SamlAttributesPreviewed", "User", result.UserId.ToString(),
            JsonSerializer.Serialize(new { entityId = result.EntityId, attributeCount = result.Attributes.Count,
                omittedCount = result.Omitted.Count, readMode = "local_read_only" }), ct);
        return Ok(result);
    });

    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (SamlAttributeException error)
        {
            return StatusCode(error.Code == "idp_disabled" ? 404 : 400, new { code = error.Code });
        }
    }
}
