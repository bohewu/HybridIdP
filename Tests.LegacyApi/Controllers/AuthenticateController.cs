using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace Tests.LegacyApi.Controllers;

// Development-only generic Proof/Profile fixture; no directory or credential writes.
[ApiController]
[Route("api/authenticate")]
public class AuthenticateController(IConfiguration configuration) : ControllerBase
{
    [HttpPost("login")]
    public IActionResult Login([FromHeader(Name = "X-Internal-Secret")] string secret, [FromBody] LoginRequest request)
    {
        if (!IsAuthorized(secret)) return Unauthorized();
        if (request.ContractVersion != "1.0" || string.IsNullOrWhiteSpace(request.AccountName)) return BadRequest();
        if (request.Password != "password")
            return Ok(new { contractVersion = "1.0", outcome = request.Password == "lockout" ? "Locked" : "InvalidCredentials" });
        return Ok(new
        {
            contractVersion = "1.0", outcome = "Authenticated", providerNamespace = "example.provider",
            stableSubject = "fixture-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.AccountName))),
            canonicalAccount = request.AccountName,
            assurance = new { stableSubjectAssured = true, canonicalAccountAssured = true },
            requiredActions = Array.Empty<string>()
        });
    }

    [HttpPost("profile")]
    public IActionResult Profile([FromHeader(Name = "X-Internal-Secret")] string secret, [FromBody] ProfileRequest request)
    {
        if (!IsAuthorized(secret)) return Unauthorized();
        if (request.ContractVersion != "1.0" || request.ProviderNamespace != "example.provider" ||
            !request.StableSubject.StartsWith("fixture-", StringComparison.Ordinal)) return NotFound();
        return Ok(new
        {
            request.ContractVersion, request.ProviderNamespace, request.StableSubject,
            extraProperties = new Dictionary<string, object> { ["example_flag"] = true, ["example_code"] = "4-example" }
        });
    }

    private bool IsAuthorized(string secret) => !string.IsNullOrEmpty(configuration["FixtureSecret"]) &&
        secret == configuration["FixtureSecret"];

    public sealed record LoginRequest(string ContractVersion, string AccountName, string Password);
    public sealed record ProfileRequest(string ContractVersion, string ProviderNamespace, string StableSubject);
}
