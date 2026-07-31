using Core.Application;
using Core.Domain;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Infrastructure.IntegrationTests;

[Collection(OperationalAdminBootstrapProviderCollection.CollectionName)]
public sealed class EmailMfaAttemptStoreProviderTests(
    OperationalAdminBootstrapProviderFixture fixture)
{
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
