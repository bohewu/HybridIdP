using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Application.Interfaces;
using Core.Domain;
using Fido2NetLib;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using Web.IdP.Attributes;

namespace Web.IdP.Controllers.Account;

public record LoginOptionsRequest(string? Username);

[Route("api/passkey")]
[ApiController]
[ApiAuthorize]
public partial class PasskeyController : ControllerBase
{
    private readonly IPasskeyService _passkeyService;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<PasskeyController> _logger;

    public PasskeyController(
        IPasskeyService passkeyService,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILogger<PasskeyController> logger)
    {
        _passkeyService = passkeyService;
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
    }

    [HttpPost("register-options")]
    public async Task<IActionResult> MakeCredentialOptions(CancellationToken ct)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var options = await _passkeyService.GetRegistrationOptionsAsync(user, ct);

        // Store options in session for verification
        HttpContext.Session.SetString("fido2.attestationOptions", options.ToJson());

        LogRegistrationOptionsGenerated(user.UserName);

        return Ok(options);
    }

    [HttpPost("register")]
    [ApiAuthorize]
    public async Task<IActionResult> MakeCredential([FromBody] System.Text.Json.JsonElement attestationResponse, CancellationToken ct)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var jsonOptions = HttpContext.Session.GetString("fido2.attestationOptions");
        if (string.IsNullOrEmpty(jsonOptions))
        {
            return BadRequest(new { success = false, error = "Session expired" });
        }

        var result = await _passkeyService.RegisterCredentialsAsync(user, attestationResponse.ToString(), jsonOptions, ct);

        if (result.Success)
        {
            LogPasskeyRegistered(user.UserName);
            return Ok(new { success = true });
        }

        return BadRequest(new { success = false, error = result.Error });
    }

    [HttpPost("login-options")]
    public async Task<IActionResult> AssertionOptionsPost([FromBody] LoginOptionsRequest request, CancellationToken ct)
    {
        var options = await _passkeyService.GetAssertionOptionsAsync(request.Username, ct);
        
        HttpContext.Session.SetString("fido2.assertionOptions", options.ToJson());
        
        LogAssertionOptionsGenerated(request.Username);
        
        return Ok(options);
    }

    [HttpPost("login")]
    public async Task<IActionResult> MakeAssertion([FromBody] System.Text.Json.JsonElement clientResponse, CancellationToken ct)
    {
        var jsonOptions = HttpContext.Session.GetString("fido2.assertionOptions");
        if (string.IsNullOrEmpty(jsonOptions))
        {
            return BadRequest(new { success = false, error = "Session expired" });
        }

        var result = await _passkeyService.VerifyAssertionAsync(clientResponse.ToString(), jsonOptions, ct);

        if (result.Success && result.User != null)
        {
            await _signInManager.SignInAsync(result.User, isPersistent: false);
            LogPasskeyLogin(result.User.UserName);
            return Ok(new { success = true, username = result.User.UserName });
        }

        return BadRequest(new { success = false, error = result.Error });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Generated FIDO2 registration options for user '{UserName}'.")]
    partial void LogRegistrationOptionsGenerated(string? userName);

    [LoggerMessage(Level = LogLevel.Information, Message = "User '{UserName}' registered a new passkey.")]
    partial void LogPasskeyRegistered(string? userName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generated FIDO2 assertion options for user '{UserName}'.")]
    partial void LogAssertionOptionsGenerated(string? userName);

    [LoggerMessage(Level = LogLevel.Information, Message = "User '{UserName}' logged in with passkey.")]
    partial void LogPasskeyLogin(string? userName);
}
