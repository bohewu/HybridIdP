using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.IdP.Services;
using Core.Domain.Constants;
using Web.IdP.Attributes;
using Infrastructure.Authorization;

namespace Web.IdP.Controllers.Admin;

/// <summary>
/// Admin API controller for managing logging configuration.
/// </summary>
[Route("api/admin/logging")]
[ApiController]
[ApiAuthorize]
[ValidateCsrfForCookies]
public class LoggingController : ControllerBase
{
    private readonly IDynamicLoggingService _loggingService;

    public LoggingController(IDynamicLoggingService loggingService)
    {
        _loggingService = loggingService;
    }

    /// <summary>
    /// Get the current global log level.
    /// </summary>
    [HttpGet("level")]
    [HasPermission(Permissions.Settings.Read)]
    public async Task<IActionResult> GetLevel()
    {
        var level = await _loggingService.GetGlobalLogLevelAsync();
        return Ok(new { level });
    }

    /// <summary>
    /// Set the global log level.
    /// </summary>
    [HttpPost("level")]
    [HasPermission(Permissions.Settings.Update)]
    public async Task<IActionResult> SetLevel([FromBody] SetLogLevelRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Level))
        {
            return BadRequest("Level is required.");
        }

        try 
        {
            await _loggingService.SetGlobalLogLevelAsync(request.Level);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}

public class SetLogLevelRequest
{
    public string Level { get; set; } = default!;
}
