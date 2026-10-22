using System.Text.Json;
using Core.Application;
using Core.Application.Ports;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.IdP.Attributes;
using Web.IdP.Services;

namespace Web.IdP.Controllers.Admin;

[ApiController]
[Route("api/admin/query-configuration")]
[ApiAuthorize]
[ValidateCsrfForCookies]
public sealed class QueryConfigurationsController(INeutralQueryService queries, IAuditService audit) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = DataSourceAdministrationRequirement.AdministrationPolicy)]
    [Authorize(Policy = Permissions.Settings.Read)]
    public Task<IActionResult> Get(CancellationToken cancellationToken) => ExecuteAsync(async () =>
        Ok(await queries.GetConfigurationAsync(cancellationToken)));

    [HttpPut]
    [Authorize(Policy = DataSourceAdministrationRequirement.AdministrationPolicy)]
    [Authorize(Policy = Permissions.Settings.Update)]
    public Task<IActionResult> Put(QueryConfigurationRequest request, CancellationToken cancellationToken) => ExecuteAsync(async () =>
    {
        var result = await queries.SetConfigurationAsync(request, cancellationToken);
        await audit.LogAdministrativeEventAsync("QueryConfigurationChanged", "QueryConfiguration", "global",
            JsonSerializer.Serialize(new { ConfigurationCount = result.Configurations.Count }), cancellationToken);
        return Ok(result);
    });

    [HttpGet("{capability}/{sourceId}/users/{userId:guid}/approval")]
    [Authorize(Policy = Permissions.Settings.Read)]
    [Authorize(Policy = Permissions.Users.Read)]
    public Task<IActionResult> GetApproval(string capability, string sourceId, Guid userId, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await queries.GetApprovalAsync(capability, sourceId, userId, cancellationToken)));

    [HttpPut("{capability}/{sourceId}/users/{userId:guid}/approval")]
    [Authorize(Policy = Permissions.Settings.Update)]
    [Authorize(Policy = Permissions.Users.Update)]
    public Task<IActionResult> PutApproval(string capability, string sourceId, Guid userId, QueryApprovalRequest request,
        CancellationToken cancellationToken) => ExecuteAsync(async () =>
    {
        var result = await queries.SetApprovalAsync(capability, sourceId, userId, request, cancellationToken);
        await audit.LogAdministrativeEventAsync("QueryValueApprovalChanged", "QueryValueApproval", userId.ToString(),
            JsonSerializer.Serialize(new { request.Approved }), cancellationToken);
        return Ok(result);
    });

    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (NeutralQueryException exception)
        {
            var body = new { error = exception.Code };
            return exception.Code switch
            {
                "not_found" => NotFound(body),
                "conflict" or "invalid_stored_state" => Conflict(body),
                _ => BadRequest(body)
            };
        }
    }
}
