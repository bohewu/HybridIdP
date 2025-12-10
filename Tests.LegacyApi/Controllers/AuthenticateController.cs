using Microsoft.AspNetCore.Mvc;

namespace Tests.LegacyApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthenticateController : ControllerBase
{
    private const string LegacySecret = "LegacyDev@123";

    [HttpPost("login")]
    public IActionResult Login([FromHeader(Name = "X-Internal-Secret")] string secret, [FromBody] LoginRequest request)
    {
        if (secret != LegacySecret)
        {
            return Unauthorized(new { message = "Invalid secret" });
        }

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
             return BadRequest(new { message = "Username and password are required" });
        }

        // Mock Logic:
        // Password "password" -> Success
        // Password "lockout" -> Locked
        // Others -> Invalid

        if (request.Password == "password")
        {
             return Ok(new 
             {
                 Authenticated = true,
                 UserId = "1001",
                 SsoUuid = Guid.NewGuid().ToString(),
                 Username = request.Username,
                 Email = $"{request.Username}@example.com",
                 NationalId = "M123456789",
                 PassportNumber = (string?)null,
                 ResidentCertificateNumber = (string?)null
             });
        }
        else if (request.Password == "lockout")
        {
             return Ok(new 
             {
                 Authenticated = false,
                 IsLocked = true,
                 LockoutEnd = DateTime.UtcNow.AddMinutes(15)
             });
        }

        return Ok(new { Authenticated = false });
    }

    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
