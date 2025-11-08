using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Core.Application;
using Core.Domain.Constants;

namespace Web.IdP.Api;

[ApiController]
[Route("api/admin/[controller]")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly ISettingsService _settings;

    public SettingsController(ISettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// GET /api/admin/settings?prefix=branding.
    /// Retrieve all settings with a given prefix.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Permissions.Settings.Read)]
    public async Task<IActionResult> GetByPrefix([FromQuery] string? prefix = null)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return BadRequest(new { error = "Prefix parameter is required" });
        }

        var settingsDict = await _settings.GetByPrefixAsync(prefix);
        
        // Convert dictionary to array of objects with key, value, dataType
        var settingsArray = settingsDict.Select(kvp => new
        {
            key = kvp.Key,
            value = kvp.Value,
            dataType = "String" // For now, all settings are strings in the dict
        }).ToArray();
        
        return Ok(settingsArray);
    }

    /// <summary>
    /// GET /api/admin/settings/{key}
    /// Retrieve a single setting by exact key.
    /// </summary>
    [HttpGet("{key}")]
    [Authorize(Policy = Permissions.Settings.Read)]
    public async Task<IActionResult> GetByKey(string key)
    {
        var value = await _settings.GetValueAsync(key);
        if (value == null)
        {
            return NotFound(new { error = $"Setting '{key}' not found" });
        }
        return Ok(new { key, value });
    }

    /// <summary>
    /// PUT /api/admin/settings/{key}
    /// Update or create a setting.
    /// Body: { "value": "..." }
    /// </summary>
    [HttpPut("{key}")]
    [Authorize(Policy = Permissions.Settings.Update)]
    public async Task<IActionResult> UpdateSetting(string key, [FromBody] UpdateSettingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Value))
        {
            return BadRequest(new { error = "Value is required" });
        }

        var updatedBy = User.Identity?.Name ?? User.FindFirst("sub")?.Value ?? "unknown";
        await _settings.SetValueAsync(key, request.Value, updatedBy);

        return Ok(new { key, value = request.Value, message = "Setting updated successfully" });
    }

    /// <summary>
    /// POST /api/admin/settings/invalidate
    /// Invalidate cache for a specific key or prefix.
    /// Body: { "key": "..." } or empty for full cache clear.
    /// </summary>
    [HttpPost("invalidate")]
    [Authorize(Policy = Permissions.Settings.Update)]
    public async Task<IActionResult> InvalidateCache([FromBody] InvalidateCacheRequest? request)
    {
        await _settings.InvalidateAsync(request?.Key);
        var message = string.IsNullOrEmpty(request?.Key)
            ? "All settings cache invalidated"
            : $"Cache for '{request.Key}' invalidated";
        return Ok(new { message });
    }
}

public record UpdateSettingRequest(string Value);
public record InvalidateCacheRequest(string? Key);
