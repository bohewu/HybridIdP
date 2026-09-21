using Infrastructure;
using Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using Xunit;

namespace Tests.Infrastructure.UnitTests.Services;

public sealed class OpenIddictSubjectTokenRevokerTests
{
    [Fact]
    public async Task RevokeBySubjectAsync_ShouldRotateConcurrencyTokenAndRejectStaleUpdate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        var subject = Guid.NewGuid().ToString();
        var tokenId = Guid.NewGuid();
        const string originalConcurrencyToken = "original-concurrency-token";

        await using (var setupContext = new ApplicationDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Set<OpenIddictEntityFrameworkCoreToken<Guid>>().Add(new()
            {
                Id = tokenId,
                ConcurrencyToken = originalConcurrencyToken,
                Status = OpenIddictConstants.Statuses.Valid,
                Subject = subject,
                Type = OpenIddictConstants.TokenTypeHints.RefreshToken
            });
            await setupContext.SaveChangesAsync();
        }

        await using var staleContext = new ApplicationDbContext(options);
        var staleToken = await staleContext
            .Set<OpenIddictEntityFrameworkCoreToken<Guid>>()
            .SingleAsync(token => token.Id == tokenId);

        await using (var revocationContext = new ApplicationDbContext(options))
        {
            var revoker = new OpenIddictSubjectTokenRevoker(revocationContext);
            Assert.Equal(1, await revoker.RevokeBySubjectAsync(subject, CancellationToken.None));
        }

        await using (var verificationContext = new ApplicationDbContext(options))
        {
            var revokedToken = await verificationContext
                .Set<OpenIddictEntityFrameworkCoreToken<Guid>>()
                .SingleAsync(token => token.Id == tokenId);
            Assert.Equal(OpenIddictConstants.Statuses.Revoked, revokedToken.Status);
            Assert.NotEqual(originalConcurrencyToken, revokedToken.ConcurrencyToken);
        }

        staleToken.Status = OpenIddictConstants.Statuses.Redeemed;
        staleToken.RedemptionDate = DateTime.UtcNow;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => staleContext.SaveChangesAsync());

        await using var finalContext = new ApplicationDbContext(options);
        var finalToken = await finalContext
            .Set<OpenIddictEntityFrameworkCoreToken<Guid>>()
            .SingleAsync(token => token.Id == tokenId);
        Assert.Equal(OpenIddictConstants.Statuses.Revoked, finalToken.Status);
    }
}
