using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;

namespace Web.IdP.Services;

public interface IRevocationService
{
    Task<IActionResult> HandleRevocationRequestAsync(OpenIddictRequest request);
}
