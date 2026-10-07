using Core.Application;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

/// <summary>
/// Uses conditional database updates so parallel requests cannot exceed a pending
/// email MFA code's verification budget.
/// </summary>
public sealed class EmailMfaAttemptStore : IEmailMfaAttemptStore
{
    private readonly ApplicationDbContext _dbContext;

    public EmailMfaAttemptStore(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EmailMfaAttemptReservation> TryReserveAttemptAsync(
        Guid userId,
        string codeHash,
        DateTime utcNow,
        int maxAttempts,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        var reserved = await MatchingPendingCode(userId, codeHash, utcNow)
            .Where(user => user.EmailMfaVerificationAttempts < maxAttempts - 1)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    user => user.EmailMfaVerificationAttempts,
                    user => user.EmailMfaVerificationAttempts + 1)
                    .SetProperty(user => user.ConcurrencyStamp, Guid.NewGuid().ToString()),
                ct);

        if (reserved == 1)
        {
            await ReloadTrackedUserAsync(userId, ct);
            return EmailMfaAttemptReservation.Reserved;
        }

        var finalAttempt = await MatchingPendingCode(userId, codeHash, utcNow)
            .Where(user => user.EmailMfaVerificationAttempts == maxAttempts - 1)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    user => user.EmailMfaVerificationAttempts,
                    maxAttempts)
                    .SetProperty(user => user.ConcurrencyStamp, Guid.NewGuid().ToString()),
                ct);

        await ReloadTrackedUserAsync(userId, ct);
        return finalAttempt == 1
            ? EmailMfaAttemptReservation.FinalAttempt
            : EmailMfaAttemptReservation.Rejected;
    }

    public async Task InvalidatePendingCodeAsync(
        Guid userId,
        string codeHash,
        CancellationToken ct = default)
    {
        await _dbContext.Users
            .Where(user => user.Id == userId && user.EmailMfaCode == codeHash)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.EmailMfaCode, (string?)null)
                    .SetProperty(user => user.EmailMfaCodeExpiry, (DateTime?)null)
                    .SetProperty(user => user.EmailMfaVerificationAttempts, 0)
                    .SetProperty(user => user.ConcurrencyStamp, Guid.NewGuid().ToString()),
                ct);
        await ReloadTrackedUserAsync(userId, ct);
    }

    private async Task ReloadTrackedUserAsync(Guid userId, CancellationToken ct)
    {
        var entry = _dbContext.ChangeTracker.Entries<Core.Domain.ApplicationUser>()
            .SingleOrDefault(entry => entry.Entity.Id == userId);
        if (entry != null) await entry.ReloadAsync(ct);
    }

    private IQueryable<Core.Domain.ApplicationUser> MatchingPendingCode(
        Guid userId,
        string codeHash,
        DateTime utcNow) =>
        _dbContext.Users.Where(user =>
            user.Id == userId &&
            user.EmailMfaCode == codeHash &&
            user.EmailMfaCodeExpiry.HasValue &&
            user.EmailMfaCodeExpiry.Value > utcNow);
}
