using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;

namespace Web.IdP.Services;

public interface IIntrospectionService
{
    Task<IActionResult> HandleIntrospectionRequestAsync(OpenIddictRequest request);
}
