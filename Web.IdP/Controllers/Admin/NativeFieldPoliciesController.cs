using System.Text.Json;
using Core.Application;
using Core.Application.Ports;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.IdP.Attributes;

namespace Web.IdP.Controllers.Admin;

[ApiController]
[Route("api/admin/native-field-policies")]
[ApiAuthorize]
[ValidateCsrfForCookies]
public sealed class NativeFieldPoliciesController(INativeFieldPolicyService policies, IAuditService audit) : ControllerBase
{
    [HttpGet("mappings")]
    [Authorize(Policy = Permissions.Settings.Read)]
    public Task<IActionResult> GetMappings(CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await policies.GetMappingsAsync(cancellationToken)));

    [HttpPut("mappings")]
    [Authorize(Policy = Permissions.Settings.Update)]
    public Task<IActionResult> SetMappings(NativeFieldMappingsRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var result = await policies.SetMappingsAsync(request, cancellationToken);
            await audit.LogAdministrativeEventAsync("NativeFieldMappingsChanged", "NativeFieldMappings", "global",
                JsonSerializer.Serialize(new { MappingCount = result.Mappings.Count }), cancellationToken);
            return Ok(result);
        });

    [HttpGet("sources")]
    [Authorize(Policy = Permissions.Settings.Read)]
    public Task<IActionResult> GetSources(CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await policies.GetSourcesAsync(cancellationToken)));

    [HttpPut("sources/{source}")]
    [Authorize(Policy = Permissions.Settings.Update)]
    public Task<IActionResult> SetPaused(NativeFieldSource source, NativeFieldPauseRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            await policies.SetPausedAsync(source, request, cancellationToken);
            await audit.LogAdministrativeEventAsync("NativeFieldSourcePauseChanged", "NativeFieldSource", source.ToString(),
                JsonSerializer.Serialize(new { request.Paused }), cancellationToken);
            return NoContent();
        });

    [HttpGet("users/{id:guid}")]
    [Authorize(Policy = Permissions.Users.Read)]
    public Task<IActionResult> GetUser(Guid id, CancellationToken cancellationToken) => GetTargetAsync(NativeFieldTarget.User, id, cancellationToken);

    [HttpPut("sources/{source}/first-name-fallback")]
    [Authorize(Policy = Permissions.Settings.Update)]
    public Task<IActionResult> SetFirstNameFallback(NativeFieldSource source, NativeFieldFirstNameFallbackRequest request,
        CancellationToken cancellationToken) => ExecuteAsync(async () =>
        {
            await policies.SetFirstNameFallbackAsync(source, request, cancellationToken);
            await audit.LogAdministrativeEventAsync("NativeFieldFirstNameFallbackChanged", "NativeFieldSource", source.ToString(),
                JsonSerializer.Serialize(new { request.Enabled }), cancellationToken);
            return NoContent();
        });

    [HttpGet("persons/{id:guid}")]
    [Authorize(Policy = Permissions.Persons.Read)]
    public Task<IActionResult> GetPerson(Guid id, CancellationToken cancellationToken) => GetTargetAsync(NativeFieldTarget.Person, id, cancellationToken);

    [HttpPut("users/{id:guid}/{field}")]
    [Authorize(Policy = Permissions.Users.Update)]
    public Task<IActionResult> UpdateUser(Guid id, NativeField field, NativeFieldUpdateRequest request, CancellationToken cancellationToken) =>
        UpdateTargetAsync(NativeFieldTarget.User, id, field, request, cancellationToken);

    [HttpPut("persons/{id:guid}/{field}")]
    [Authorize(Policy = Permissions.Persons.Update)]
    public Task<IActionResult> UpdatePerson(Guid id, NativeField field, NativeFieldUpdateRequest request, CancellationToken cancellationToken) =>
        UpdateTargetAsync(NativeFieldTarget.Person, id, field, request, cancellationToken);

    private Task<IActionResult> GetTargetAsync(NativeFieldTarget target, Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await policies.GetTargetAsync(target, id, cancellationToken)));

    private Task<IActionResult> UpdateTargetAsync(NativeFieldTarget target, Guid id, NativeField field,
        NativeFieldUpdateRequest request, CancellationToken cancellationToken) => ExecuteAsync(async () =>
    {
        await policies.UpdateAsync(target, id, field, request, cancellationToken);
        // Never serialize the request: it contains a native value and source subject.
        await audit.LogAdministrativeEventAsync("NativeFieldPolicyChanged", target.ToString(), id.ToString(),
            JsonSerializer.Serialize(new { Field = field.ToString(), Operation = request.Operation.ToString(), request.AllowOverwrite }), cancellationToken);
        return Ok(await policies.GetTargetAsync(target, id, cancellationToken));
    });

    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (NativeFieldPolicyException exception)
        {
            var body = new { error = exception.Code };
            return exception.Code switch
            {
                "not_found" => NotFound(body),
                "conflict" => Conflict(body),
                "invalid_stored_state" => StatusCode(409, body),
                _ => BadRequest(body)
            };
        }
    }
}
