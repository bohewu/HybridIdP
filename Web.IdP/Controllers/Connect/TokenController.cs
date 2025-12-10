using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Web.IdP.Services;

namespace Web.IdP.Controllers.Connect
{
    public class TokenController : Controller
    {
        private readonly ITokenService _tokenService;

        public TokenController(ITokenService tokenService)
        {
            _tokenService = tokenService;
        }

        [HttpPost("~/connect/token")]
        [EnableRateLimiting("token")]
        [IgnoreAntiforgeryToken]
        [Produces("application/json")]
        public async Task<IActionResult> Exchange()
        {
            var request = HttpContext.GetOpenIddictServerRequest();
            if (request == null)
            {
                return BadRequest("The OpenID Connect request cannot be recovered.");
            }

            System.Security.Claims.ClaimsPrincipal? schemePrincipal = null;

            if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType() || request.GrantType == OpenIddictConstants.GrantTypes.DeviceCode) 
            {
                // Retrieve the claims principal stored in the authorization code/refresh token/device code
                var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
                schemePrincipal = result.Principal;
            }

            return await _tokenService.HandleTokenRequestAsync(request, schemePrincipal);
        }
    }
}
