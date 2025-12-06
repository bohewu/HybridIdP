using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using System.Security.Claims;

namespace Web.IdP.Services
{
    public interface ITokenService
    {
        Task<IActionResult> HandleTokenRequestAsync(OpenIddictRequest request, ClaimsPrincipal? schemePrincipal);
    }
}
