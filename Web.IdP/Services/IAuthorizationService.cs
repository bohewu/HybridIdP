using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

using OpenIddict.Abstractions;

namespace Web.IdP.Services
{
    public interface IAuthorizationService
    {
        string? ApplicationName { get; }
        List<ScopeInfo> ScopeInfos { get; }

        Task<IActionResult> HandleAuthorizeRequestAsync(ClaimsPrincipal? user, OpenIddictRequest request, string? prompt);
        Task<IActionResult> HandleAuthorizeSubmitAsync(ClaimsPrincipal? user, OpenIddictRequest request, string? submit, string[]? grantedScopes);
    }
}
