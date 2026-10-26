using Core.Application.Interfaces;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class SamlStateStoreTests
{
    [Fact]
    public async Task TryTakeAsync_ShouldReturnPayloadOnlyOnceAcrossInstances()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.True(await fixture.Store().TryAddAsync(SamlStatePurpose.SpCorrelation, "request", "private-state", fixture.Expiry));
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            fixture.Store().TryTakeAsync(SamlStatePurpose.SpCorrelation, "request"))));
        Assert.Single(results, result => result == "private-state");
        Assert.Equal(7, results.Count(result => result is null));
    }

    [Fact]
    public async Task TryAddAsync_ShouldReserveReplayExactlyOnceAcrossInstances()
    {
        await using var fixture = await Fixture.CreateAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            fixture.Store().TryAddAsync(SamlStatePurpose.SpAssertionReplay, "partner:assertion", "", fixture.Expiry))));
        Assert.Single(results, result => result);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store().TryTakeAsync(
            SamlStatePurpose.SpAssertionReplay, "partner:assertion"));
        Assert.False(await fixture.Store().TryAddAsync(SamlStatePurpose.SpAssertionReplay, "partner:assertion", "", fixture.Expiry));
    }

    [Fact]
    public async Task TryAddAsync_ShouldSeparatePurposesAndProtectPayloadAtRest()
    {
        await using var fixture = await Fixture.CreateAsync();
        var store = fixture.Store();
        Assert.True(await store.TryAddAsync(SamlStatePurpose.IdpContinuation, "same", "local-user-private", fixture.Expiry));
        Assert.True(await store.TryAddAsync(SamlStatePurpose.SpCorrelation, "same", "external-state", fixture.Expiry));
        await using (var database = new ApplicationDbContext(fixture.Options))
        {
            var rows = await database.Set<SamlStateRecord>().ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, row =>
            {
                Assert.Equal(64, row.KeyHash.Length);
                Assert.DoesNotContain("local-user-private", row.ProtectedPayload);
                Assert.DoesNotContain("external-state", row.ProtectedPayload);
            });
        }
        Assert.Equal("local-user-private", await store.TryTakeAsync(SamlStatePurpose.IdpContinuation, "same"));
        Assert.Equal("external-state", await store.TryTakeAsync(SamlStatePurpose.SpCorrelation, "same"));
    }

    [Fact]
    public async Task TryTakeAsync_ShouldRejectExpiredAndWrongKeyRingState()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.True(await fixture.Store().TryAddAsync(SamlStatePurpose.SpCorrelation, "expired", "payload", fixture.Expiry));
        fixture.Clock.Now = fixture.Expiry;
        Assert.Null(await fixture.Store().TryTakeAsync(SamlStatePurpose.SpCorrelation, "expired"));
        Assert.True(await fixture.Store().TryAddAsync(SamlStatePurpose.SpCorrelation, "wrong-key", "payload", fixture.Expiry));
        var wrongRing = new SamlStateStore(fixture.Options, new EphemeralDataProtectionProvider(), fixture.Clock);
        Assert.Null(await wrongRing.TryTakeAsync(SamlStatePurpose.SpCorrelation, "wrong-key"));
        Assert.Null(await fixture.Store().TryTakeAsync(SamlStatePurpose.SpCorrelation, "wrong-key"));
        await using var database = new ApplicationDbContext(fixture.Options);
        Assert.Empty(await database.Set<SamlStateRecord>().ToListAsync());
    }

    [Fact]
    public async Task TryAddAsync_ShouldRejectUnboundedOrExpiredInput()
    {
        await using var fixture = await Fixture.CreateAsync();
        var store = fixture.Store();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.TryAddAsync(
            SamlStatePurpose.SpCorrelation, "key", "", fixture.Clock.Now));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.TryAddAsync(
            SamlStatePurpose.SpCorrelation, "key", "", fixture.Clock.Now.AddMinutes(11)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.TryAddAsync(
            SamlStatePurpose.SpCorrelation, "key", new string('x', 32769), fixture.Expiry));
        await Assert.ThrowsAsync<ArgumentException>(() => store.TryAddAsync(
            SamlStatePurpose.SpCorrelation, new string('x', 2049), "", fixture.Expiry));
    }

    [Fact]
    public async Task TryAddAsync_ShouldPropagateDatabaseFailure()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var database = new ApplicationDbContext(fixture.Options);
        await database.Database.ExecuteSqlRawAsync("DROP TABLE SamlStates");
        await Assert.ThrowsAsync<SqliteException>(() => fixture.Store().TryAddAsync(
            SamlStatePurpose.SpCorrelation, "key", "", fixture.Expiry));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _keeper;
        private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();
        public DbContextOptions<ApplicationDbContext> Options { get; }
        public TestClock Clock { get; } = new();
        public DateTimeOffset Expiry => Clock.Now.AddMinutes(5);

        private Fixture(SqliteConnection keeper, DbContextOptions<ApplicationDbContext> options)
        {
            _keeper = keeper;
            Options = options;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connectionString = $"Data Source=saml-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=10";
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connectionString).Options;
            await using var database = new ApplicationDbContext(options);
            await database.Database.EnsureCreatedAsync();
            return new Fixture(keeper, options);
        }

        public SamlStateStore Store() => new(Options, _protection, Clock);
        public ValueTask DisposeAsync() => _keeper.DisposeAsync();
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
