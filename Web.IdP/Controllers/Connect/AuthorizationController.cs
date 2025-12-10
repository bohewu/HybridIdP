using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Server.AspNetCore;
using Web.IdP.Services;

namespace Web.IdP.Controllers.Connect
{
    public class AuthorizationController : Controller
    {
        private readonly IAuthorizationService _authorizationService;

        public AuthorizationController(IAuthorizationService authorizationService)
        {
            _authorizationService = authorizationService;
        }

        [HttpGet("~/connect/authorize")]
        [HttpPost("~/connect/authorize")]
        [IgnoreAntiforgeryToken] // OpenIddict handles CSRF protection
        public async Task<IActionResult> Authorize()
        {
            var request = HttpContext.GetOpenIddictServerRequest() ??
                throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

            // If this is a POST request (consent form submission)
            if (Request.Method == "POST")
            {
                // Extract form values
                var submit = Request.Form["submit"]; // "allow" or "deny"
                var grantedScopes = Request.Form["granted_scopes"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries); 
                
                return await _authorizationService.HandleAuthorizeSubmitAsync(User, request, submit, grantedScopes);
            }
            
            // GET request (render consent or challenge)
            string? prompt = request.Prompt;
            var result = await _authorizationService.HandleAuthorizeRequestAsync(User, request, prompt);

            if (result is OkResult)
            {
                // Retrieve data from service to pass to View
                ViewData["ApplicationName"] = _authorizationService.ApplicationName;
                return View(_authorizationService.ScopeInfos);
            }

            return result;
        }
    }
}
