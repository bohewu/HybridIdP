using System.Data.Common;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using OpenIddict.Abstractions;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class PersonLifecycleServiceTests
{
    [Fact]
    public async Task TerminatePersonAsync_ShouldRotateLinkedUserSecurityStamp()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        await using var context = new ApplicationDbContext(fixture.Options);
        var service = CreateService(context);

        var result = await service.TerminatePersonAsync(
            fixture.PersonId,
            effectiveDate: null,
            Guid.NewGuid(),
            revokeTokens: false);

        Assert.True(result);
        await AssertLifecycleStampRotatedAsync(fixture, PersonStatus.Resigned);
    }

    [Fact]
    public async Task SuspendPersonAsync_ShouldRotateLinkedUserSecurityStamp()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        await using var context = new ApplicationDbContext(fixture.Options);
        var service = CreateService(context);

        var result = await service.SuspendPersonAsync(
            fixture.PersonId,
            Guid.NewGuid(),
            revokeTokens: false);

        Assert.True(result);
        await AssertLifecycleStampRotatedAsync(fixture, PersonStatus.Suspended);
    }

    [Fact]
    public async Task ChangeStatusAsync_ShouldRotateLinkedUserSecurityStamp_WhenEligibilityChanges()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        await using var context = new ApplicationDbContext(fixture.Options);
        var service = CreateService(context);

        var result = await service.ChangeStatusAsync(
            fixture.PersonId,
            PersonStatus.Terminated,
            Guid.NewGuid());

        Assert.True(result);
        await AssertLifecycleStampRotatedAsync(fixture, PersonStatus.Terminated);
    }

    [Fact]
    public async Task SoftDeletePersonAsync_ShouldRotateLinkedUserSecurityStamp()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        await using var context = new ApplicationDbContext(fixture.Options);
        var service = CreateService(context);

        var result = await service.SoftDeletePersonAsync(
            fixture.PersonId,
            Guid.NewGuid(),
            revokeTokens: false);

        Assert.True(result);

        await using var verificationContext = new ApplicationDbContext(fixture.Options);
        var person = await verificationContext.Persons.FindAsync(fixture.PersonId);
        var linkedUser = await verificationContext.Users.FindAsync(fixture.UserId);
        Assert.NotNull(person);
        Assert.True(person.IsDeleted);
        Assert.NotNull(linkedUser);
        Assert.NotEqual(fixture.OriginalSecurityStamp, linkedUser.SecurityStamp);
    }

    [Fact]
    public async Task ProcessScheduledTransitionsAsync_ShouldRotateLinkedUserSecurityStamps()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(
            PersonStatus.Pending,
            DateTime.UtcNow.Date.AddDays(-1));
        await using var context = new ApplicationDbContext(fixture.Options);
        var service = CreateService(context);

        var count = await service.ProcessScheduledTransitionsAsync();

        Assert.Equal(1, count);
        await AssertLifecycleStampRotatedAsync(fixture, PersonStatus.Active);
    }

    [Fact]
    public async Task ProcessScheduledTransitionsAsync_ShouldNotPartiallyCommitOrStartTokenRevocation_WhenSaveFails()
    {
        var originalEndDate = DateTime.UtcNow.Date.AddDays(-1);
        await using var fixture = await LifecycleFixture.CreateAsync(
            PersonStatus.Active,
            endDate: originalEndDate);
        var tokenManager = new Mock<IOpenIddictTokenManager>();
        var failureOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(fixture.Connection)
            .AddInterceptors(new ThrowOnPersonUpdateInterceptor())
            .Options;

        await using (var context = new ApplicationDbContext(failureOptions))
        {
            var service = CreateService(context, tokenManager.Object);

            await Assert.ThrowsAsync<DbUpdateException>(() => service.ProcessScheduledTransitionsAsync());
        }

        await using var verificationContext = new ApplicationDbContext(fixture.Options);
        var person = await verificationContext.Persons.FindAsync(fixture.PersonId);
        var linkedUser = await verificationContext.Users.FindAsync(fixture.UserId);
        Assert.NotNull(person);
        Assert.Equal(PersonStatus.Active, person.Status);
        Assert.Equal(originalEndDate, person.EndDate);
        Assert.NotNull(linkedUser);
        Assert.Equal(fixture.OriginalSecurityStamp, linkedUser.SecurityStamp);
        tokenManager.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SuspendPersonAsync_ShouldNotPartiallyCommitPersonOrSecurityStamp_WhenSaveFails()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        var failureOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(fixture.Connection)
            .AddInterceptors(new ThrowOnPersonUpdateInterceptor())
            .Options;

        await using (var context = new ApplicationDbContext(failureOptions))
        {
            var service = CreateService(context);

            await Assert.ThrowsAsync<DbUpdateException>(() => service.SuspendPersonAsync(
                fixture.PersonId,
                Guid.NewGuid(),
                revokeTokens: false));
        }

        await using var verificationContext = new ApplicationDbContext(fixture.Options);
        var person = await verificationContext.Persons.FindAsync(fixture.PersonId);
        var linkedUser = await verificationContext.Users.FindAsync(fixture.UserId);
        Assert.NotNull(person);
        Assert.Equal(PersonStatus.Active, person.Status);
        Assert.NotNull(linkedUser);
        Assert.Equal(fixture.OriginalSecurityStamp, linkedUser.SecurityStamp);
    }

    private static PersonLifecycleService CreateService(
        ApplicationDbContext context,
        IOpenIddictTokenManager? tokenManager = null) => new(
        context,
        tokenManager ?? new Mock<IOpenIddictTokenManager>().Object,
        new Mock<ILogger<PersonLifecycleService>>().Object);

    private static async Task AssertLifecycleStampRotatedAsync(
        LifecycleFixture fixture,
        PersonStatus expectedStatus)
    {
        await using var verificationContext = new ApplicationDbContext(fixture.Options);
        var person = await verificationContext.Persons.FindAsync(fixture.PersonId);
        var linkedUser = await verificationContext.Users.FindAsync(fixture.UserId);
        var unrelatedUser = await verificationContext.Users.FindAsync(fixture.UnrelatedUserId);

        Assert.NotNull(person);
        Assert.Equal(expectedStatus, person.Status);
        Assert.NotNull(linkedUser);
        Assert.NotEqual(fixture.OriginalSecurityStamp, linkedUser.SecurityStamp);
        Assert.NotNull(unrelatedUser);
        Assert.Equal(fixture.UnrelatedSecurityStamp, unrelatedUser.SecurityStamp);
    }

    private sealed class LifecycleFixture : IAsyncDisposable
    {
        private LifecycleFixture(SqliteConnection connection, DbContextOptions<ApplicationDbContext> options)
        {
            Connection = connection;
            Options = options;
        }

        public SqliteConnection Connection { get; }
        public DbContextOptions<ApplicationDbContext> Options { get; }
        public Guid PersonId { get; private init; }
        public Guid UserId { get; private init; }
        public Guid UnrelatedUserId { get; private init; }
        public string OriginalSecurityStamp { get; private init; } = string.Empty;
        public string UnrelatedSecurityStamp { get; private init; } = string.Empty;

        public static async Task<LifecycleFixture> CreateAsync(
            PersonStatus status = PersonStatus.Active,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;
            await using var context = new ApplicationDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var person = new Person
            {
                Id = Guid.NewGuid(),
                FirstName = "Lifecycle",
                LastName = "Person",
                Status = status,
                StartDate = startDate,
                EndDate = endDate
            };
            var unrelatedPerson = new Person
            {
                Id = Guid.NewGuid(),
                FirstName = "Unrelated",
                LastName = "Person",
                Status = PersonStatus.Active
            };
            var user = CreateUser(person.Id, "lifecycle@example.test", "original-stamp");
            var unrelatedUser = CreateUser(unrelatedPerson.Id, "unrelated@example.test", "unrelated-stamp");
            context.AddRange(person, unrelatedPerson, user, unrelatedUser);
            await context.SaveChangesAsync();

            return new LifecycleFixture(connection, options)
            {
                PersonId = person.Id,
                UserId = user.Id,
                UnrelatedUserId = unrelatedUser.Id,
                OriginalSecurityStamp = user.SecurityStamp!,
                UnrelatedSecurityStamp = unrelatedUser.SecurityStamp!
            };
        }

        public ValueTask DisposeAsync() => Connection.DisposeAsync();

        private static ApplicationUser CreateUser(Guid personId, string email, string securityStamp) => new()
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PersonId = personId,
            IsActive = true,
            SecurityStamp = securityStamp
        };
    }

    private sealed class ThrowOnPersonUpdateInterceptor : DbCommandInterceptor
    {
        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            ThrowWhenUpdatingPerson(command);
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowWhenUpdatingPerson(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            ThrowWhenUpdatingPerson(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ThrowWhenUpdatingPerson(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static void ThrowWhenUpdatingPerson(DbCommand command)
        {
            if (command.CommandText.Contains("UPDATE \"Persons\"", StringComparison.Ordinal))
            {
                throw new DbUpdateException("Injected Person persistence failure.");
            }
        }
    }
}
