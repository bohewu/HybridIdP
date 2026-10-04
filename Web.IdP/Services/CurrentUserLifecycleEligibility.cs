using Core.Application;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Web.IdP.Services;

/// <summary>
/// Reads the current local account and linked Person lifecycle state before issuance.
/// </summary>
public sealed class CurrentUserLifecycleEligibility : ICurrentUserLifecycleEligibility
{
    private readonly IApplicationDbContext _context;

    public CurrentUserLifecycleEligibility(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> IsEligibleAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return false;
        }

        try
        {
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
                return false;
            }

            if (!user.PersonId.HasValue)
            {
                return true;
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

            return person?.CanAuthenticate() == true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
}
