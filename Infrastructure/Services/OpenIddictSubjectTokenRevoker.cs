using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;

namespace Infrastructure.Services;

public sealed class OpenIddictSubjectTokenRevoker : IOpenIddictSubjectTokenRevoker
{
    private readonly ApplicationDbContext _dbContext;

    public OpenIddictSubjectTokenRevoker(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<int> RevokeBySubjectAsync(
        string subject,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        var concurrencyToken = Guid.NewGuid().ToString();
        return _dbContext.Set<OpenIddictEntityFrameworkCoreToken<Guid>>()
            .Where(token => token.Subject == subject
                && token.Status != OpenIddictConstants.Statuses.Revoked)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        token => token.Status,
                        OpenIddictConstants.Statuses.Revoked)
                    .SetProperty(
                        token => token.ConcurrencyToken,
                        concurrencyToken),
                cancellationToken);
    }
}
