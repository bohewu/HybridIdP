using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Web.IdP.Helpers;

namespace Tests.Infrastructure.IntegrationTests;

[Collection(OperationalAdminBootstrapProviderCollection.CollectionName)]
public sealed class PasswordConfirmationProviderTests(OperationalAdminBootstrapProviderFixture fixture)
{
    [Theory]
    [InlineData(OperationalAdminBootstrapProviderFixture.SqlServer, 3)]
    [InlineData(OperationalAdminBootstrapProviderFixture.PostgreSql, 3)]
    [InlineData(OperationalAdminBootstrapProviderFixture.SqlServer, 5)]
    [InlineData(OperationalAdminBootstrapProviderFixture.PostgreSql, 5)]
    [InlineData(OperationalAdminBootstrapProviderFixture.SqlServer, 7)]
    [InlineData(OperationalAdminBootstrapProviderFixture.PostgreSql, 7)]
    public async Task CheckAsync_ShouldPersistConfiguredBudgetWithoutChangingSharedIdentityOptions(string providerName, int maxAttempts)
    {
        var database = fixture.GetDatabase(providerName);
        await database.ResetAsync();
        await using var services = database.CreateServices();
        await using var scope = services.CreateAsyncScope();
        await using var otherScope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var otherUsers = otherScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var originalOptions = users.Options;
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "confirmation-user", LockoutEnabled = true };
        var password = string.Concat(Guid.NewGuid().ToString("N"), 'A', 'a', '1', '!');
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        var policy = new SecurityPolicy { MaxFailedAccessAttempts = maxAttempts, LockoutDurationMinutes = 15 };
        Assert.True(await PasswordConfirmation.CheckAsync(users, user, password, policy));
        var started = DateTimeOffset.UtcNow;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            Assert.False(await PasswordConfirmation.CheckAsync(users, user, "invalid-confirmation", policy));
            var persisted = await db.Users.AsNoTracking().SingleAsync(value => value.Id == user.Id);
            Assert.Equal(attempt == maxAttempts ? 0 : attempt, persisted.AccessFailedCount);
            if (attempt < maxAttempts) Assert.Null(persisted.LockoutEnd);
            else Assert.InRange(persisted.LockoutEnd!.Value, started.AddMinutes(15), DateTimeOffset.UtcNow.AddMinutes(15));
            Assert.Same(originalOptions, users.Options);
            Assert.Same(originalOptions, otherUsers.Options);
            Assert.Equal(5, otherUsers.Options.Lockout.MaxFailedAccessAttempts);
            Assert.Equal(TimeSpan.FromMinutes(5), otherUsers.Options.Lockout.DefaultLockoutTimeSpan);
        }
        Assert.False(await PasswordConfirmation.CheckAsync(users, user, password, policy));
        Assert.True(await users.IsLockedOutAsync(user));
    }
}
