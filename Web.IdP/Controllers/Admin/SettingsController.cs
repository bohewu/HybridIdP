using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Core.Application;
using Core.Domain.Constants;
using Core.Application.Options;
using Microsoft.Extensions.Options;
using Web.IdP.Attributes;

namespace Web.IdP.Controllers.Admin;

[ApiController]
[Route("api/admin/[controller]")]
[ApiAuthorize]
[ValidateCsrfForCookies]
public class SettingsController : ControllerBase
{
    private const string SystemManagedSettingError =
        "System-managed settings cannot be modified";
    private readonly ISettingsService _settings;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly IOptionsSnapshot<EmailOptions> _emailOptions;

    public SettingsController(
        ISettingsService settings, 
        IEmailService emailService, 
        IConfiguration configuration,
        IOptionsSnapshot<EmailOptions> emailOptions)
    {
        _settings = settings;
        _emailService = emailService;
        _configuration = configuration;
        _emailOptions = emailOptions;
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
        
        // If it's email settings, we can use the Options pattern for better metadata
        if (prefix == "Mail.")
        {
            var effectiveOptions = _emailOptions.Value;
            var baseOptions = _configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions();
            
            var emailSettings = new[]
            {
                new { Key = SettingKeys.Email.SmtpHost, Value = kvpVal(SettingKeys.Email.SmtpHost), Default = baseOptions.SmtpHost },
                new { Key = SettingKeys.Email.SmtpPort, Value = kvpVal(SettingKeys.Email.SmtpPort), Default = baseOptions.SmtpPort.ToString() },
                new { Key = SettingKeys.Email.SmtpUsername, Value = kvpVal(SettingKeys.Email.SmtpUsername), Default = baseOptions.SmtpUsername },
                new { Key = SettingKeys.Email.SmtpPassword, Value = kvpVal(SettingKeys.Email.SmtpPassword), Default = baseOptions.SmtpPassword },
                new { Key = SettingKeys.Email.SmtpEnableSsl, Value = kvpVal(SettingKeys.Email.SmtpEnableSsl), Default = baseOptions.SmtpEnableSsl.ToString().ToLower() },
                new { Key = SettingKeys.Email.FromAddress, Value = kvpVal(SettingKeys.Email.FromAddress), Default = baseOptions.FromAddress },
                new { Key = SettingKeys.Email.FromName, Value = kvpVal(SettingKeys.Email.FromName), Default = baseOptions.FromName }
            };

            string kvpVal(string key) => settingsDict.TryGetValue(key, out var v) ? v : string.Empty;

            return Ok(emailSettings.Select(s => {
                var isOverridden = !string.IsNullOrEmpty(s.Value);
                var displayValue = isOverridden ? s.Value : s.Default;
                
                if (IsSensitive(s.Key) && !string.IsNullOrEmpty(displayValue))
                {
                    displayValue = "(set)";
                }

                return new
                {
                    key = s.Key,
                    value = displayValue,
                    isOverridden = isOverridden,
                    source = isOverridden ? "Database" : "Configuration",
                    defaultValue = s.Default,
                    dataType = "String"
                };
            }).ToArray());
        }

        var settingsArray = settingsDict.Select(kvp => 
        {
            var isOverridden = !string.IsNullOrEmpty(kvp.Value);
            var displayValue = kvp.Value;
            
            if (IsSensitive(kvp.Key) && !string.IsNullOrEmpty(displayValue))
            {
                displayValue = "(set)";
            }

            return new
            {
                key = kvp.Key,
                value = displayValue,
                isOverridden = isOverridden,
                source = isOverridden ? "Database" : "Configuration",
                defaultValue = (string?)null,
                dataType = "String"
            };
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

        var displayValue = IsSensitive(key) && !string.IsNullOrEmpty(value)
            ? "(set)"
            : value;
        return Ok(new { key, value = displayValue });
    }

    /// <summary>
    /// PUT /api/admin/settings/{key}
    /// Update or create a setting. Empty value clears the setting (uses default).
    /// Body: { "value": "..." }
    /// </summary>
    [HttpPut("{key}")]
    [Authorize(Policy = Permissions.Settings.Update)]
    public async Task<IActionResult> UpdateSetting(string key, [FromBody] UpdateSettingRequest request)
    {
        if (SettingKeys.IsSystemOwned(key))
        {
            return BadRequest(new { error = SystemManagedSettingError });
        }

        var updatedBy = User.Identity?.Name ?? User.FindFirst("sub")?.Value ?? "unknown";
        
        if (IsSensitive(key) && request.Value == "(set)")
        {
            return Ok(new { key, message = "Setting preserved (masked value ignored)" });
        }

        // Allow empty value to clear the setting (will use default from code)
        var valueToSave = request.Value ?? string.Empty;
        try
        {
            await _settings.SetValueAsync(key, valueToSave, updatedBy);
        }
        catch (SystemManagedSettingException)
        {
            return BadRequest(new { error = SystemManagedSettingError });
        }

        return Ok(new { key, value = IsSensitive(key) ? "(set)" : valueToSave, message = "Setting updated successfully" });
    }

    private static bool IsSensitive(string key)
    {
        return key.Contains("Password", StringComparison.OrdinalIgnoreCase) || 
               key.Contains("Secret", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// POST /api/admin/settings/email/test
    /// Send a test email using provided settings.
    /// </summary>
    [HttpPost("email/test")]
    [Authorize(Policy = Permissions.Settings.Update)]
    public async Task<IActionResult> TestEmail([FromBody] TestMailSettingsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.To))
            return BadRequest(new { error = "Recipient email is required" });

        try
        {
            await _emailService.SendTestEmailAsync(request.Settings, request.To);
            return Ok(new { message = "Test email sent successfully" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = $"Failed to send email: {ex.Message}" });
        }
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
public record TestMailSettingsRequest
{
    public Core.Application.MailSettingsDto Settings { get; set; } = new();
    public string To { get; set; } = string.Empty;
}
