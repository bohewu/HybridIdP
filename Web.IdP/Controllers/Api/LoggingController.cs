using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.IdP.Services;
using Core.Domain.Constants;

namespace Web.IdP.Controllers.Api;

[Route("api/logging")]
[ApiController]
[Authorize(Policy = "HasAnyAdminAccess")]
public class LoggingController : ControllerBase
{
    private readonly IDynamicLoggingService _loggingService;

    public LoggingController(IDynamicLoggingService loggingService)
    {
        _loggingService = loggingService;
    }

    [HttpGet("level")]
    [Authorize(Policy = Permissions.Settings.Read)]
    public async Task<IActionResult> GetLevel()
    {
        var level = await _loggingService.GetGlobalLogLevelAsync();
        return Ok(new { level });
    }

    [HttpPost("level")]
    [Authorize(Policy = Permissions.Settings.Update)]
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
