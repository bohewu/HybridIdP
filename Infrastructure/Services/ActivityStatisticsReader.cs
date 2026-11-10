using Core.Application.DTOs;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;

namespace Infrastructure.Services;

/// <summary>Shared read-only sources for dashboard and monitoring statistics.</summary>
public static class ActivityStatisticsReader
{
    /// <summary>
    /// Counts authorizations, not tokens or login events. A continuing access/refresh
    /// token must supply a future expiry; authorization codes and ID tokens do not.
    /// Missing legacy ledgers and null ledger bounds rely on that token evidence.
    /// An existing revoked, expired or mismatched ledger always excludes the grant.
    /// </summary>
    public static IQueryable<OpenIddictEntityFrameworkCoreAuthorization<Guid>> EffectiveAuthorizations(
        ApplicationDbContext db, DateTime nowUtc)
    {
        return db.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().AsNoTracking()
            .Where(authorization => authorization.Status == OpenIddictConstants.Statuses.Valid &&
                authorization.Application != null &&
                db.Users.Any(user => user.Id.ToString() == authorization.Subject) &&
                authorization.Tokens.Any(token =>
                    token.Status == OpenIddictConstants.Statuses.Valid &&
                    (token.Type == OpenIddictConstants.TokenTypeIdentifiers.AccessToken ||
                     token.Type == OpenIddictConstants.TokenTypeIdentifiers.RefreshToken) &&
                    token.Subject == authorization.Subject &&
                    token.Application != null && token.Application.Id == authorization.Application.Id &&
                    token.ExpirationDate > nowUtc) &&
                !db.UserSessions.Any(session => session.AuthorizationId == authorization.Id.ToString() &&
                    (session.RevokedUtc != null ||
                     session.AbsoluteExpiresUtc <= nowUtc || session.SlidingExpiresUtc <= nowUtc ||
                     session.UserId.ToString() != authorization.Subject ||
                     session.ClientId != authorization.Application.ClientId)));
    }

    /// <summary>Counts attempts in the half-open UTC interval [startUtc, endUtc).</summary>
    public static async Task<(int Successful, int Failed)> ReadLoginCountsAsync(
        ApplicationDbContext db, DateTime startUtc, DateTime endUtc,
        CancellationToken cancellationToken = default)
    {
        var historyCounts = await db.LoginHistories.AsNoTracking()
            .Where(login => login.LoginTime >= startUtc && login.LoginTime < endUtc)
            .GroupBy(login => login.IsSuccessful)
            .Select(group => new { Successful = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var successful = historyCounts.Where(group => group.Successful).Sum(group => group.Count);
        var failed = historyCounts.Where(group => !group.Successful).Sum(group => group.Count);

        // Current writers have no overlapping failure representation. Successful
        // audit copies are excluded by the reader. No timestamp/user fuzzy dedupe.
        await foreach (var failure in FailedLoginAuditReader.ReadAsync(db, startUtc, endUtc, cancellationToken))
            failed++;

        return (successful, failed);
    }

    /// <summary>
    /// Seven UTC calendar days, oldest first, with today's bucket ending at now.
    /// ActiveSessions is the legacy wire name for successful login events here;
    /// mutable authorization/token state cannot reconstruct historical sessions.
    /// </summary>
    public static async Task<SecurityMetricsDto> ReadSecurityMetricsAsync(
        ApplicationDbContext db, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var startUtc = nowUtc.Date.AddDays(-6);
        var historyCounts = await db.LoginHistories.AsNoTracking()
            .Where(login => login.LoginTime >= startUtc && login.LoginTime < nowUtc)
            .GroupBy(login => new { Day = login.LoginTime.Date, login.IsSuccessful })
            .Select(group => new { group.Key.Day, group.Key.IsSuccessful, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var successful = new int[7];
        var failed = new int[7];
        foreach (var group in historyCounts)
        {
            var day = (group.Day - startUtc).Days;
            if (group.IsSuccessful) successful[day] += group.Count;
            else failed[day] += group.Count;
        }

        await foreach (var failure in FailedLoginAuditReader.ReadAsync(db, startUtc, nowUtc, cancellationToken))
            failed[(failure.Timestamp.Date - startUtc).Days]++;

        return new SecurityMetricsDto
        {
            LoginAttempts = successful.Zip(failed, (success, failure) => success + failure).ToList(),
            ActiveSessions = successful.ToList(),
            FailedLogins = failed.ToList()
        };
    }
}
