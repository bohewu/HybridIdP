using Core.Application;
using Core.Domain;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;

namespace Tests.Infrastructure.IntegrationTests;

[Collection(OperationalAdminBootstrapProviderCollection.CollectionName)]
public sealed class EmailMfaAttemptStoreProviderTests(
    OperationalAdminBootstrapProviderFixture fixture)
{
    [Theory]
    [InlineData(OperationalAdminBootstrapProviderFixture.SqlServer, false)]
    [InlineData(OperationalAdminBootstrapProviderFixture.PostgreSql, false)]
    [InlineData(OperationalAdminBootstrapProviderFixture.SqlServer, true)]
    [InlineData(OperationalAdminBootstrapProviderFixture.PostgreSql, true)]
    public async Task PendingCode_ShouldPreserveBudgetAcrossIdentityWritesAndRejectStaleResurrection(string providerName, bool valid)
    {
        var database = fixture.GetDatabase(providerName);
        await database.ResetAsync();
        await using var services = database.CreateServices();
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IEmailMfaAttemptStore>();
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "budget-user", LockoutEnabled = true,
            EmailMfaCode = "test-only-hash", EmailMfaCodeExpiry = DateTime.UtcNow.AddMinutes(10) };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        await using var staleScope = services.CreateAsyncScope();
        var staleUsers = staleScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var staleUser = (await staleUsers.FindByIdAsync(user.Id.ToString()))!;
        var previousStamp = staleUser.ConcurrencyStamp;
        var attempts = valid ? 1 : 5;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            Assert.Equal(attempt == 5 ? EmailMfaAttemptReservation.FinalAttempt : EmailMfaAttemptReservation.Reserved,
                await store.TryReserveAttemptAsync(user.Id, "test-only-hash", DateTime.UtcNow, 5));
            Assert.Equal(attempt, user.EmailMfaVerificationAttempts);
            if (valid || attempt == 5)
            {
                if (valid)
                {
                    user.EmailMfaCode = null;
                    user.EmailMfaCodeExpiry = null;
                    user.EmailMfaVerificationAttempts = 0;
                    Assert.True((await users.UpdateAsync(user)).Succeeded);
                }
                else await store.InvalidatePendingCodeAsync(user.Id, "test-only-hash");
            }
            if (!valid) Assert.True((await users.AccessFailedAsync(user)).Succeeded);
            var persisted = await db.Users.AsNoTracking().SingleAsync(value => value.Id == user.Id);
            Assert.Equal(attempt == attempts ? 0 : attempt, persisted.EmailMfaVerificationAttempts);
        }
        Assert.NotEqual(previousStamp, user.ConcurrencyStamp);
        Assert.False((await staleUsers.UpdateAsync(staleUser)).Succeeded);
        var final = await db.Users.AsNoTracking().SingleAsync(value => value.Id == user.Id);
        Assert.Null(final.EmailMfaCode);
        Assert.Null(final.EmailMfaCodeExpiry);
        Assert.Equal(0, final.EmailMfaVerificationAttempts);
        Assert.Equal(EmailMfaAttemptReservation.Rejected,
            await store.TryReserveAttemptAsync(user.Id, "test-only-hash", DateTime.UtcNow, 5));
    }

    [Theory]
    [InlineData(OperationalAdminBootstrapProviderFixture.SqlServer)]
    [InlineData(OperationalAdminBootstrapProviderFixture.PostgreSql)]
    public async Task TryReserveAttemptAsync_ShouldAtomicallyLimitParallelRequests(
        string providerName)
    {
        const int maxAttempts = 5;
        const string pendingCodeHash = "TEST_ONLY_HASHED_EMAIL_MFA_CODE";
        var database = fixture.GetDatabase(providerName);
        await database.ResetAsync();
        await using var services = database.CreateServices();
        var userId = Guid.NewGuid();

        await using (var seedScope = services.CreateAsyncScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = $"email-mfa-attempt-{Guid.NewGuid():N}",
                EmailMfaCode = pendingCodeHash,
                EmailMfaCodeExpiry = DateTime.UtcNow.AddMinutes(10)
            });
            await dbContext.SaveChangesAsync();
        }

        var reservations = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(async _ =>
            {
                await using var scope = services.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IEmailMfaAttemptStore>();
                return await store.TryReserveAttemptAsync(
                    userId,
                    pendingCodeHash,
                    DateTime.UtcNow,
                    maxAttempts);
            }));

        Assert.Equal(
            maxAttempts - 1,
            reservations.Count(result => result == EmailMfaAttemptReservation.Reserved));
        Assert.Single(
            reservations,
            result => result == EmailMfaAttemptReservation.FinalAttempt);
        Assert.Equal(
            reservations.Length - maxAttempts,
            reservations.Count(result => result == EmailMfaAttemptReservation.Rejected));

        await using (var verifyScope = services.CreateAsyncScope())
        {
            var dbContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await dbContext.Users.AsNoTracking().SingleAsync(item => item.Id == userId);
            Assert.Equal(maxAttempts, user.EmailMfaVerificationAttempts);
        }

        await using (var invalidateScope = services.CreateAsyncScope())
        {
            var store = invalidateScope.ServiceProvider.GetRequiredService<IEmailMfaAttemptStore>();
            await store.InvalidatePendingCodeAsync(userId, pendingCodeHash);
        }

        await using (var finalScope = services.CreateAsyncScope())
        {
            var dbContext = finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await dbContext.Users.AsNoTracking().SingleAsync(item => item.Id == userId);
            Assert.Null(user.EmailMfaCode);
            Assert.Null(user.EmailMfaCodeExpiry);
            Assert.Equal(0, user.EmailMfaVerificationAttempts);
        }
    }
}
