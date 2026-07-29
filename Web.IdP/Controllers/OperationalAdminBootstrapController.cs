using Core.Application;
using Core.Application.Options;
using Core.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Web.IdP.Models;

namespace Web.IdP.Controllers;

[Route("api/operational-bootstrap")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public sealed partial class OperationalAdminBootstrapController : ControllerBase
{
    public const string HeaderName = "X-HybridAuth-Bootstrap-Token";
    private const string CompletedCode = "operational_bootstrap_completed";
    private const string UnavailableCode = "operational_bootstrap_unavailable";
    private readonly IOperationalAdminBootstrapService _bootstrapService;
    private readonly IOptions<OperationalAdminBootstrapOptions> _options;
    private readonly ILogger<OperationalAdminBootstrapController> _logger;

    public OperationalAdminBootstrapController(
        IOperationalAdminBootstrapService bootstrapService,
        IOptions<OperationalAdminBootstrapOptions> options,
        ILogger<OperationalAdminBootstrapController> logger)
    {
        _bootstrapService = bootstrapService;
        _options = options;
        _logger = logger;
    }

    [HttpPost("admin")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [EnableRateLimiting("operational-bootstrap")]
    public async Task<IActionResult> BootstrapAdminAsync(
        [FromBody] OperationalAdminBootstrapRequest? request,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var utcNow = DateTimeOffset.UtcNow;

        if (!Request.IsHttps)
        {
            LogDenied(correlationId, "https_required", utcNow);
            return Unavailable(correlationId);
        }

        var presentedToken = Request.Headers[HeaderName].ToString();
        if (!OperationalAdminBootstrapTokenValidator.IsAuthorized(
                _options.Value,
                presentedToken,
                utcNow))
        {
            LogDenied(correlationId, "authorization_failed", utcNow);
            return Unavailable(correlationId);
        }

        if (!ModelState.IsValid || !IsValidRequest(request))
        {
            LogDenied(correlationId, "request_invalid", utcNow);
            return Unavailable(correlationId);
        }

        var command = new OperationalAdminBootstrapCommand(
            request!.Email!.Trim(),
            request.Name!.Trim(),
            request.Password!,
            correlationId);
        var result = await _bootstrapService.BootstrapAsync(command, cancellationToken);

        var response = new OperationalAdminBootstrapResponse(
            result.Succeeded ? CompletedCode : UnavailableCode,
            correlationId);
        return result.Succeeded
            ? StatusCode(StatusCodes.Status201Created, response)
            : NotFound(response);
    }

    private static bool IsValidRequest(OperationalAdminBootstrapRequest? request) =>
        request is not null &&
        !string.IsNullOrWhiteSpace(request.Email) &&
        request.Email.Length <= 256 &&
        !string.IsNullOrWhiteSpace(request.Name) &&
        request.Name.Length <= 200 &&
        !string.IsNullOrEmpty(request.Password) &&
        request.Password.Length <= 1024;

    private IActionResult Unavailable(string correlationId) =>
        NotFound(new OperationalAdminBootstrapResponse(
            UnavailableCode,
            correlationId));

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Operational administrator bootstrap request denied. CorrelationId={CorrelationId} Reason={Reason} Utc={Utc}")]
    private partial void LogDenied(
        string correlationId,
        string reason,
        DateTimeOffset utc);
}
