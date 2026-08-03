using System.Security.Claims;
using Core.Application;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Web.IdP.Services;

/// <summary>
/// Rejects application-cookie principals whose current identity lifecycle state is ineligible.
/// </summary>
public sealed class ApplicationCookieCurrentStateValidator
{
    private readonly IApplicationDbContext _context;

    public ApplicationCookieCurrentStateValidator(IApplicationDbContext context)
    {
        _context = context;
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
        var user = await _context.Users
            .AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new
            {
                candidate.IsActive,
                candidate.IsDeleted,
                candidate.LockoutEnd,
                candidate.PersonId
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null ||
            !user.IsActive ||
            user.IsDeleted ||
            (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow))
        {
            context.RejectPrincipal();
            return;
        }

        if (!user.PersonId.HasValue)
        {
            return;
        }

        var person = await _context.Persons
            .AsNoTracking()
            .Where(candidate => candidate.Id == user.PersonId.Value)
            .Select(candidate => new Person
            {
                IsDeleted = candidate.IsDeleted,
                Status = candidate.Status,
                StartDate = candidate.StartDate,
                EndDate = candidate.EndDate
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (person is null || !person.CanAuthenticate())
        {
            context.RejectPrincipal();
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
