using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Web.IdP.Services;

/// <summary>
/// Rejects application-cookie principals whose current identity lifecycle state is ineligible.
/// </summary>
public sealed class ApplicationCookieCurrentStateValidator
{
    private readonly ICurrentUserLifecycleEligibility _lifecycleEligibility;
    private readonly IMigrationIssuanceGuard _migrationIssuanceGuard;

    public ApplicationCookieCurrentStateValidator(
        Core.Application.IApplicationDbContext context,
        IMigrationIssuanceGuard migrationIssuanceGuard)
        : this(new CurrentUserLifecycleEligibility(context), migrationIssuanceGuard)
    {
    }

    public ApplicationCookieCurrentStateValidator(
        ICurrentUserLifecycleEligibility lifecycleEligibility,
        IMigrationIssuanceGuard migrationIssuanceGuard)
    {
        _lifecycleEligibility = lifecycleEligibility;
        _migrationIssuanceGuard = migrationIssuanceGuard;
    }

    public async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var userIdValue = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            context.RejectPrincipal();
            return;
        }

        var cancellationToken = context.HttpContext.RequestAborted;
        if (!await _lifecycleEligibility.IsEligibleAsync(userId, cancellationToken))
        {
            context.RejectPrincipal();
            return;
        }

        if (!await _migrationIssuanceGuard.CanIssueAsync(userId, cancellationToken))
        {
            context.RejectPrincipal();
            return;
        }

    }

    public static Func<CookieValidatePrincipalContext, Task> Compose(
        Func<CookieValidatePrincipalContext, Task>? securityStampValidator)
    {
        return async context =>
        {
            if (securityStampValidator is not null)
            {
                await securityStampValidator(context);
            }

            if (context.Principal is null)
            {
                return;
            }

            var validator = context.HttpContext.RequestServices
                .GetRequiredService<ApplicationCookieCurrentStateValidator>();
            await validator.ValidateAsync(context);
        };
    }
}
