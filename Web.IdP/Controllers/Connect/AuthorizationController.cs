using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Web.IdP.Attributes;
using Web.IdP.Helpers;
using Web.IdP.Services;
using Web.IdP.Filters;

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
        [EnableRateLimiting("authorize")]
        [ValidateCsrfForCookies]
        [RequireClientPermission(OpenIddictConstants.Permissions.Endpoints.Authorization)]
        public async Task<IActionResult> Authorize(CancellationToken cancellationToken)
        {
            var request = HttpContext.GetOpenIddictServerRequest() ??
                throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

            // If this is a POST request (consent form submission)
            if (HttpMethods.IsPost(Request.Method))
            {
                var intent = Request.Form[AuthorizationConsentSession.FormFieldName].ToString();
                if (!AuthorizationConsentSession.TryConsume(
                        HttpContext.Session,
                        User,
                        request,
                        intent))
                {
                    return BadRequest(new
                    {
                        error = "invalid_consent_intent",
                        message = "The authorization consent interaction is invalid or has expired."
                    });
                }

                // Extract form values
                var submit = Request.Form["submit"]; // "allow" or "deny"
                var grantedScopes = Request.Form["granted_scopes"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries); 
                
                return await _authorizationService.HandleAuthorizeSubmitAsync(User, request, submit, grantedScopes, cancellationToken);
            }
            
            // GET request (render consent or challenge)
            string? prompt = request.Prompt;
            var result = await _authorizationService.HandleAuthorizeRequestAsync(User, request, prompt, cancellationToken);

            if (result is OkResult)
            {
                // Retrieve data from service to pass to View
                ViewData["ApplicationName"] = _authorizationService.ApplicationName;
                ViewData["ConsentIntent"] = AuthorizationConsentSession.Issue(
                    HttpContext.Session,
                    User,
                    request);
                return View(_authorizationService.ScopeInfos);
            }

            return result;
        }
    }
}
