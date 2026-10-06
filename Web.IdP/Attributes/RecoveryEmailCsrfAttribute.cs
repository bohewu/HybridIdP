using Infrastructure.Options;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Web.IdP.Attributes;

// New recovery mutations are cookie/session-bound; a posted Authorization header is never an exemption.
[AttributeUsage(AttributeTargets.Class)]
public sealed class RecoveryEmailCsrfAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var options = http.RequestServices.GetService<IOptions<RecoveryEmailSelectionOptions>>()?.Value;
        if (options?.Enabled != true || !options.SelfServiceEnabled ||
            HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method) || HttpMethods.IsOptions(http.Request.Method))
        {
            await next();
            return;
        }
        try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http); }
        catch (AntiforgeryValidationException)
        {
            context.Result = new BadRequestObjectResult(new { outcome = "invalid" });
            return;
        }
        await next();
    }
}
