using Core.Application;
using Microsoft.AspNetCore.Mvc;
using Web.IdP.Attributes;

namespace Web.IdP.Controllers.Account;

/// <summary>
/// User-facing security policy endpoint.
/// Returns relevant security settings that affect the user profile UI.
/// </summary>
[ApiController]
[Route("api/account")]
[ApiAuthorize]
public class AccountSecurityController : ControllerBase
{
    private readonly ISecurityPolicyService _securityPolicyService;

    public AccountSecurityController(ISecurityPolicyService securityPolicyService)
    {
        _securityPolicyService = securityPolicyService;
    }

    /// <summary>
    /// Get current security policy settings relevant to the user profile.
    /// </summary>
    [HttpGet("security-policy")]
    public async Task<ActionResult<UserSecurityPolicyResponse>> GetSecurityPolicy()
    {
        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        
        return Ok(new UserSecurityPolicyResponse
        {
            RequireMfaForPasskey = policy.RequireMfaForPasskey,
            EnablePasskey = policy.EnablePasskey,
            EnableTotpMfa = policy.EnableTotpMfa,
            EnableEmailMfa = policy.EnableEmailMfa,
            AllowSelfPasswordChange = policy.AllowSelfPasswordChange
        });
    }
}

/// <summary>
/// DTO for user-facing security policy response.
/// Only includes settings relevant to the user profile UI.
/// </summary>
public record UserSecurityPolicyResponse
{
    public bool RequireMfaForPasskey { get; init; }
    public bool EnablePasskey { get; init; }
    public bool EnableTotpMfa { get; init; }
    public bool EnableEmailMfa { get; init; }
    public bool AllowSelfPasswordChange { get; init; }
}
