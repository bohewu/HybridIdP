using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Web.IdP.Attributes;

/// <summary>
/// Reopens local logout confirmation after antiforgery has rejected the POST.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class LogoutAntiforgeryRecoveryAttribute : Attribute, IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (HttpMethods.IsPost(context.HttpContext.Request.Method) &&
            context.Result is IAntiforgeryValidationFailedResult)
        {
            // Do not replay the rejected request or carry its return URL forward.
            context.Result = new RedirectToPageResult("/Account/Logout", new { confirmationExpired = true });
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
