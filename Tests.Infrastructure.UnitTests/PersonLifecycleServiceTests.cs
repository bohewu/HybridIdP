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
        var tokenRevoker = new Mock<IOpenIddictSubjectTokenRevoker>();
        var failureOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(fixture.Connection)
            .AddInterceptors(new ThrowOnPersonUpdateInterceptor())
            .Options;

        await using (var context = new ApplicationDbContext(failureOptions))
        {
            var service = CreateService(context, tokenRevoker.Object);

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
        tokenRevoker.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RevokeAllTokensForPersonAsync_ShouldCountOnlyConfirmedBulkRevocations()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        var tokenRevoker = new Mock<IOpenIddictSubjectTokenRevoker>();
        tokenRevoker
            .Setup(revoker => revoker.RevokeBySubjectAsync(
                fixture.UserId.ToString(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        await using var context = new ApplicationDbContext(fixture.Options);
        var service = CreateService(context, tokenRevoker.Object);

        var revokedCount = await service.RevokeAllTokensForPersonAsync(fixture.PersonId);

        Assert.Equal(2, revokedCount);
        tokenRevoker.Verify(
            revoker => revoker.RevokeBySubjectAsync(
                fixture.UserId.ToString(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        tokenRevoker.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessScheduledTransitionsAsync_ShouldPersistPendingRevocation_WhenRevocationFails()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(
            PersonStatus.Active,
            endDate: DateTime.UtcNow.Date.AddDays(-1));
        var tokenRevoker = new Mock<IOpenIddictSubjectTokenRevoker>();
        tokenRevoker
            .Setup(revoker => revoker.RevokeBySubjectAsync(
                fixture.UserId.ToString(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Injected token-store failure."));

        await using (var context = new ApplicationDbContext(fixture.Options))
        {
            var service = CreateService(context, tokenRevoker.Object);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ProcessScheduledTransitionsAsync());
        }

        await using var verificationContext = new ApplicationDbContext(fixture.Options);
        var person = await verificationContext.Persons.FindAsync(fixture.PersonId);
        var linkedUser = await verificationContext.Users.FindAsync(fixture.UserId);
        Assert.NotNull(person);
        Assert.Equal(PersonStatus.Resigned, person.Status);
        Assert.NotNull(person.ScheduledTokenRevocationPendingAt);
        Assert.NotNull(linkedUser);
        Assert.NotEqual(fixture.OriginalSecurityStamp, linkedUser.SecurityStamp);
    }

    [Fact]
    public async Task ProcessScheduledTransitionsAsync_ShouldRecoverPersistedPendingRevocation()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(
            PersonStatus.Resigned,
            endDate: DateTime.UtcNow.Date.AddDays(-1));

        await using (var setupContext = new ApplicationDbContext(fixture.Options))
        {
            var person = await setupContext.Persons.FindAsync(fixture.PersonId);
            Assert.NotNull(person);
            person.ScheduledTokenRevocationPendingAt = DateTime.UtcNow.AddMinutes(-1);
            await setupContext.SaveChangesAsync();
        }

        var tokenRevoker = new Mock<IOpenIddictSubjectTokenRevoker>();
        tokenRevoker
            .Setup(revoker => revoker.RevokeBySubjectAsync(
                fixture.UserId.ToString(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await using (var context = new ApplicationDbContext(fixture.Options))
        {
            var service = CreateService(context, tokenRevoker.Object);
            var changedCount = await service.ProcessScheduledTransitionsAsync();
            Assert.Equal(0, changedCount);
        }

        await using var verificationContext = new ApplicationDbContext(fixture.Options);
        var recoveredPerson = await verificationContext.Persons.FindAsync(fixture.PersonId);
        Assert.NotNull(recoveredPerson);
        Assert.Equal(PersonStatus.Resigned, recoveredPerson.Status);
        Assert.Null(recoveredPerson.ScheduledTokenRevocationPendingAt);
    }

    [Fact]
    public async Task ProcessScheduledTransitionsAsync_ShouldProcessMoreThanOneBoundedBatch()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(
            PersonStatus.Pending,
            startDate: DateTime.UtcNow.Date.AddDays(-1));

        await using (var setupContext = new ApplicationDbContext(fixture.Options))
        {
            setupContext.Persons.AddRange(Enumerable.Range(0, 100).Select(index => new Person
            {
                Id = Guid.NewGuid(),
                FirstName = $"Batch{index}",
                LastName = "Lifecycle",
                Status = PersonStatus.Pending,
                StartDate = DateTime.UtcNow.Date.AddDays(-1)
            }));
            await setupContext.SaveChangesAsync();
        }

        await using (var context = new ApplicationDbContext(fixture.Options))
        {
            var service = CreateService(context);
            var changedCount = await service.ProcessScheduledTransitionsAsync();
            Assert.Equal(101, changedCount);
            Assert.Empty(context.ChangeTracker.Entries<Person>());
            Assert.Empty(context.ChangeTracker.Entries<ApplicationUser>());
        }

        await using var verificationContext = new ApplicationDbContext(fixture.Options);
        Assert.Equal(
            102,
            await verificationContext.Persons.CountAsync(person => person.Status == PersonStatus.Active));
        Assert.False(await verificationContext.Persons.AnyAsync(
            person => person.Status == PersonStatus.Pending));
    }

    [Fact]
    public async Task ProcessScheduledTransitionsAsync_ShouldPropagateCancellationAfterDurableTermination()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(
            PersonStatus.Active,
            endDate: DateTime.UtcNow.Date.AddDays(-1));
        using var cancellation = new CancellationTokenSource();
        var tokenRevoker = new Mock<IOpenIddictSubjectTokenRevoker>();
        tokenRevoker
            .Setup(revoker => revoker.RevokeBySubjectAsync(
                fixture.UserId.ToString(),
                cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<int>(cancellation.Token);
            });

        await using (var context = new ApplicationDbContext(fixture.Options))
        {
            var service = CreateService(context, tokenRevoker.Object);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.ProcessScheduledTransitionsAsync(cancellation.Token));
        }

        await using (var verificationContext = new ApplicationDbContext(fixture.Options))
        {
            var person = await verificationContext.Persons.FindAsync(fixture.PersonId);
            Assert.NotNull(person);
            Assert.Equal(PersonStatus.Resigned, person.Status);
            Assert.NotNull(person.ScheduledTokenRevocationPendingAt);
        }

        var recoveryRevoker = new Mock<IOpenIddictSubjectTokenRevoker>();
        recoveryRevoker
            .Setup(revoker => revoker.RevokeBySubjectAsync(
                fixture.UserId.ToString(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await using (var recoveryContext = new ApplicationDbContext(fixture.Options))
        {
            var service = CreateService(recoveryContext, recoveryRevoker.Object);
            Assert.Equal(0, await service.ProcessScheduledTransitionsAsync());
        }

        await using var recoveredContext = new ApplicationDbContext(fixture.Options);
        var recoveredPerson = await recoveredContext.Persons.FindAsync(fixture.PersonId);
        Assert.NotNull(recoveredPerson);
        Assert.Null(recoveredPerson.ScheduledTokenRevocationPendingAt);
    }

    [Fact]
    public async Task ProcessScheduledTransitionsAsync_ShouldHonorPreCanceledTokenBeforeDatabaseWork()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(
            PersonStatus.Pending,
            startDate: DateTime.UtcNow.Date.AddDays(-1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await using (var context = new ApplicationDbContext(fixture.Options))
        {
            var service = CreateService(context);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.ProcessScheduledTransitionsAsync(cancellation.Token));
        }

        await using var verificationContext = new ApplicationDbContext(fixture.Options);
        var person = await verificationContext.Persons.FindAsync(fixture.PersonId);
        Assert.NotNull(person);
        Assert.Equal(PersonStatus.Pending, person.Status);
    }

    [Fact]
    public async Task ActivatePersonAsync_ShouldKeepPersonIneligible_WhenPendingRevocationFails()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(PersonStatus.Resigned);
        await using (var setupContext = new ApplicationDbContext(fixture.Options))
        {
            var person = await setupContext.Persons.FindAsync(fixture.PersonId);
            Assert.NotNull(person);
            person.ScheduledTokenRevocationPendingAt = DateTime.UtcNow.AddMinutes(-1);
            await setupContext.SaveChangesAsync();
        }

        var tokenRevoker = new Mock<IOpenIddictSubjectTokenRevoker>();
        tokenRevoker
            .Setup(revoker => revoker.RevokeBySubjectAsync(
                fixture.UserId.ToString(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Injected token-store failure."));

        await using (var context = new ApplicationDbContext(fixture.Options))
        {
            var service = CreateService(context, tokenRevoker.Object);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ActivatePersonAsync(
                fixture.PersonId,
                DateTime.UtcNow.Date,
                Guid.NewGuid()));
        }

        await using var verificationContext = new ApplicationDbContext(fixture.Options);
        var persistedPerson = await verificationContext.Persons.FindAsync(fixture.PersonId);
        Assert.NotNull(persistedPerson);
        Assert.Equal(PersonStatus.Resigned, persistedPerson.Status);
        Assert.NotNull(persistedPerson.ScheduledTokenRevocationPendingAt);
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
        IOpenIddictSubjectTokenRevoker? tokenRevoker = null) => new(
        context,
        tokenRevoker ?? new Mock<IOpenIddictSubjectTokenRevoker>().Object,
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
