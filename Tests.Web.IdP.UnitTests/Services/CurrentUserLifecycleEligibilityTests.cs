using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure;
using Infrastructure.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Web.IdP.Services;

namespace Tests.Web.IdP.UnitTests.Services;

public sealed class CurrentUserLifecycleEligibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IsEligibleAsync_ShouldAllow_WhenExactApprovedEnabledAndLocallyEligible(bool external)
    {
        using var fixture = new Fixture(external);
        Assert.True(await fixture.Service.IsEligibleAsync(fixture.User.Id));
        Assert.Equal(1, fixture.Client.Calls);
        Assert.Equal(fixture.Tuple, fixture.Client.LastBinding);
        Assert.All(fixture.Database.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("lockout")]
    [InlineData("missing-person")]
    [InlineData("person-deleted")]
    [InlineData("person-suspended")]
    [InlineData("person-future")]
    [InlineData("person-expired")]
    public async Task IsEligibleAsync_ShouldDenyWithoutLookup_WhenLocalPolicyDenies(string change)
    {
        using var fixture = new Fixture();
        await fixture.ChangeLocalAsync(change);
        Assert.False(await fixture.Service.IsEligibleAsync(fixture.User.Id));
        Assert.Equal(0, fixture.Client.Calls);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("lockout")]
    [InlineData("missing-person")]
    [InlineData("person-deleted")]
    [InlineData("person-suspended")]
    [InlineData("person-future")]
    [InlineData("person-expired")]
    [InlineData("person-link")]
    [InlineData("security-stamp")]
    [InlineData("concurrency-stamp")]
    public async Task IsEligibleAsync_ShouldRereadUntrackedLocalState_WhenItChangesDuringLookup(string change)
    {
        using var fixture = new Fixture();
        fixture.Client.BeforeReturn = () => fixture.ChangeLocalAsync(change);
        Assert.False(await fixture.Service.IsEligibleAsync(fixture.User.Id));
        Assert.True(fixture.User.IsActive); // The tracked object was not refreshed/mutated by the policy.
    }

    [Theory]
    [InlineData("namespace")]
    [InlineData("subject")]
    [InlineData("authority")]
    [InlineData("mapping")]
    [InlineData("correlation-failure")]
    [InlineData("invalid-request-id")]
    [InlineData("missing-evidence")]
    [InlineData("unavailable")]
    public async Task IsEligibleAsync_ShouldDeny_WhenEvidenceIsNotAccepted(string change)
    {
        using var fixture = new Fixture();
        var response = fixture.Response;
        fixture.Response = change switch
        {
            "namespace" => response with { Binding = fixture.Tuple with { ProviderNamespace = "Issuer" } },
            "subject" => response with { Binding = fixture.Tuple with { StableSubject = "subject" } },
            "authority" => response with { Evidence = response.Evidence! with { SourceAuthority = "Authority" } },
            "mapping" => response with { Evidence = response.Evidence! with { MappingVersion = "Mapping-1" } },
            "invalid-request-id" => response with { RequestId = Guid.Empty.ToString() },
            "missing-evidence" => response with { Evidence = null },
            "unavailable" => response with { Outcome = ProviderLifecycleOutcome.Unavailable },
            _ => response
        };
        if (change == "correlation-failure") fixture.Client.Failure = ProviderLifecycleFailure.Malformed;
        Assert.False(await fixture.Service.IsEligibleAsync(fixture.User.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IsEligibleAsync_ShouldDenyRequiredAccount_WhenContractOrTransportUnavailable(bool contractResponse)
    {
        using var fixture = new Fixture();
        fixture.Response = new ProviderLifecycleResponse
        {
            RequestId = Guid.NewGuid().ToString("D"), Outcome = ProviderLifecycleOutcome.Unavailable
        };
        fixture.Client.Failure = contractResponse ? ProviderLifecycleFailure.None : ProviderLifecycleFailure.Unavailable;
        fixture.Client.SuppressResponse = !contractResponse;
        Assert.False(await fixture.Service.IsEligibleAsync(fixture.User.Id));
        Assert.Equal(1, fixture.Client.Calls);
        Assert.All(fixture.Database.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    [Theory]
    [InlineData(ProviderLifecycleAccountState.Disabled)]
    [InlineData(ProviderLifecycleAccountState.Retired)]
    [InlineData(ProviderLifecycleAccountState.Superseded)]
    [InlineData(ProviderLifecycleAccountState.Unknown)]
    public async Task IsEligibleAsync_ShouldDenyOnlyQueriedAccountWithoutMutationOrSuccessorLookup(
        ProviderLifecycleAccountState state)
    {
        using var fixture = new Fixture();
        fixture.Response = fixture.Response with
        {
            AccountState = state,
            Successor = new ProviderLifecycleBinding { ProviderNamespace = "issuer", StableSubject = "R0001" }
        };
        var sibling = new ApplicationUser { Id = Guid.NewGuid(), IsActive = true, PersonId = fixture.Person.Id };
        fixture.Database.Users.Add(sibling);
        var session = new UserSession { Id = Guid.NewGuid(), UserId = fixture.User.Id };
        fixture.Database.UserSessions.Add(session);
        await fixture.Database.SaveChangesAsync();
        Assert.False(await fixture.Service.IsEligibleAsync(fixture.User.Id));
        Assert.True(await fixture.Service.IsEligibleAsync(sibling.Id));
        Assert.Equal(1, fixture.Client.Calls);
        await using var fresh = fixture.FreshDatabase();
        var user = await fresh.Users.SingleAsync(candidate => candidate.Id == fixture.User.Id);
        Assert.True(user.IsActive);
        Assert.False(user.IsDeleted);
        Assert.Equal(fixture.User.SecurityStamp, user.SecurityStamp);
        Assert.Equal(fixture.User.ConcurrencyStamp, user.ConcurrencyStamp);
        Assert.Equal(PersonStatus.Active, (await fresh.Persons.SingleAsync()).Status);
        Assert.Equal(2, await fresh.Users.CountAsync());
        Assert.Single(await fresh.ProviderSubjectDirectoryBindings.ToListAsync());
        Assert.Null((await fresh.UserSessions.SingleAsync()).RevokedUtc);
        Assert.All(fixture.Database.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    [Theory]
    [InlineData("observed-now", true)]
    [InlineData("max-age", true)]
    [InlineData("too-old", false)]
    [InlineData("future-observation", false)]
    [InlineData("start-now", true)]
    [InlineData("future-start", false)]
    [InlineData("end-now", false)]
    [InlineData("before-end", true)]
    [InlineData("equal-interval", false)]
    [InlineData("reversed-interval", false)]
    public async Task IsEligibleAsync_ShouldApplyZeroSkewActionTimeBoundaries(string boundary, bool expected)
    {
        using var fixture = new Fixture();
        var evidence = fixture.Response.Evidence!;
        fixture.Response = fixture.Response with { Evidence = boundary switch
        {
            "observed-now" => evidence with { ObservedAt = Fixture.Now },
            "max-age" => evidence with { ObservedAt = Fixture.Now.AddSeconds(-60) },
            "too-old" => evidence with { ObservedAt = Fixture.Now.AddSeconds(-60).AddTicks(-1) },
            "future-observation" => evidence with { ObservedAt = Fixture.Now.AddTicks(1) },
            "start-now" => evidence with { EffectiveFrom = Fixture.Now },
            "future-start" => evidence with { EffectiveFrom = Fixture.Now.AddTicks(1) },
            "end-now" => evidence with { EffectiveUntil = Fixture.Now },
            "before-end" => evidence with { EffectiveUntil = Fixture.Now.AddTicks(1) },
            "equal-interval" => evidence with { EffectiveFrom = Fixture.Now, EffectiveUntil = Fixture.Now },
            _ => evidence with { EffectiveFrom = Fixture.Now.AddSeconds(1), EffectiveUntil = Fixture.Now }
        }};
        Assert.Equal(expected, await fixture.Service.IsEligibleAsync(fixture.User.Id));
    }

    [Theory]
    [InlineData("expired-evidence")]
    [InlineData("stale-evidence")]
    [InlineData("person-midnight")]
    [InlineData("backwards")]
    [InlineData("unreadable")]
    [InlineData("non-utc")]
    public async Task IsEligibleAsync_ShouldDeny_WhenClockOrEligibilityChangesBeforeAction(string change)
    {
        using var fixture = new Fixture();
        fixture.Person.EndDate = Fixture.Now.Date;
        await fixture.Database.SaveChangesAsync();
        if (change == "person-midnight")
        {
            fixture.Time.Now = new DateTimeOffset(Fixture.Now.Date.AddDays(1).AddTicks(-1), TimeSpan.Zero);
            fixture.Response = fixture.Response with { Evidence = fixture.Response.Evidence! with
            {
                ObservedAt = fixture.Time.Now, EffectiveFrom = fixture.Time.Now,
                EffectiveUntil = fixture.Time.Now.AddMinutes(1)
            }};
        }
        fixture.Client.BeforeReturn = () =>
        {
            fixture.Time.Now = change switch
            {
                "expired-evidence" => Fixture.Now.AddMinutes(5),
                "stale-evidence" => Fixture.Now.AddSeconds(60),
                "person-midnight" => new DateTimeOffset(Fixture.Now.Date.AddDays(1), TimeSpan.Zero),
                "backwards" => Fixture.Now.AddTicks(-1),
                "non-utc" => Fixture.Now.ToOffset(TimeSpan.FromHours(1)),
                _ => Fixture.Now
            };
            fixture.Time.Unreadable = change == "unreadable";
            return Task.CompletedTask;
        };
        Assert.False(await fixture.Service.IsEligibleAsync(fixture.User.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IsEligibleAsync_ShouldDeny_WhenRequiredBindingMissingOrAmbiguous(bool ambiguous)
    {
        using var fixture = new Fixture();
        if (ambiguous)
            fixture.Database.ProviderSubjectDirectoryBindings.Add(new ProviderSubjectDirectoryBinding(
                fixture.User.Id, "issuer", "OtherSubject", Guid.NewGuid(), Fixture.Now.UtcDateTime));
        else fixture.Database.ProviderSubjectDirectoryBindings.Remove(fixture.DirectoryBinding);
        await fixture.Database.SaveChangesAsync();
        Assert.False(await fixture.Service.IsEligibleAsync(fixture.User.Id));
        Assert.Equal(0, fixture.Client.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IsEligibleAsync_ShouldUseOrdinalStoredSelectors_WhenCaseDiffers(bool external)
    {
        using var fixture = new Fixture(external);
        if (external) fixture.Options.RequiredAccounts[0].ExternalLoginProvider = "externalauth";
        else fixture.Options.RequiredAccounts[0].ProviderNamespace = "Issuer";
        Assert.False(await fixture.Service.IsEligibleAsync(fixture.User.Id));
        Assert.Equal(0, fixture.Client.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IsEligibleAsync_ShouldDeny_WhenExternalLinkIsMissingOrAmbiguous(bool ambiguous)
    {
        using var fixture = new Fixture(true);
        if (ambiguous) fixture.Database.UserLogins.Add(new IdentityUserLogin<Guid>
            { UserId = fixture.User.Id, LoginProvider = "ExternalAuth", ProviderKey = "AnotherSubject" });
        else fixture.Database.UserLogins.RemoveRange(fixture.Database.UserLogins);
        await fixture.Database.SaveChangesAsync();
        Assert.False(await fixture.Service.IsEligibleAsync(fixture.User.Id));
        Assert.Equal(0, fixture.Client.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IsEligibleAsync_ShouldDeny_WhenBindingChangesDuringLookup(bool external)
    {
        using var fixture = new Fixture(external);
        fixture.Client.BeforeReturn = async () =>
        {
            await using var fresh = fixture.FreshDatabase();
            if (external)
            {
                fresh.UserLogins.RemoveRange(await fresh.UserLogins.ToListAsync());
                fresh.UserLogins.Add(new IdentityUserLogin<Guid>
                    { UserId = fixture.User.Id, LoginProvider = "ExternalAuth", ProviderKey = "NewSubject" });
            }
            else
            {
                fresh.ProviderSubjectDirectoryBindings.RemoveRange(await fresh.ProviderSubjectDirectoryBindings.ToListAsync());
                fresh.ProviderSubjectDirectoryBindings.Add(new ProviderSubjectDirectoryBinding(fixture.User.Id,
                    "issuer", "Subject", fixture.DirectoryBinding.DirectoryObjectId, Fixture.Now.UtcDateTime));
            }
            await fresh.SaveChangesAsync();
        };
        Assert.False(await fixture.Service.IsEligibleAsync(fixture.User.Id));
    }

    [Theory]
    [InlineData("enabled")]
    [InlineData("cohort")]
    [InlineData("namespace")]
    [InlineData("source")]
    [InlineData("mapping")]
    [InlineData("max-age")]
    [InlineData("timeout")]
    [InlineData("endpoint")]
    [InlineData("secret")]
    public async Task IsEligibleAsync_ShouldDeny_WhenPolicyChangesDuringLookup(string change)
    {
        using var fixture = new Fixture();
        fixture.Client.BeforeReturn = () =>
        {
            var account = fixture.Options.RequiredAccounts[0];
            switch (change)
            {
                case "enabled": fixture.Options.Enabled = false; break;
                case "cohort": account.LocalAccountId = Guid.NewGuid(); break;
                case "namespace": account.ProviderNamespace = "other"; break;
                case "source": account.SourceAuthority = "other"; break;
                case "mapping": account.MappingVersion = "other"; break;
                case "max-age": fixture.Options.MaxAgeSeconds = 300; break;
                case "timeout": fixture.Options.TimeoutSeconds = 6; break;
                case "endpoint": fixture.Options.Endpoint = "https://other.invalid/lifecycle"; break;
                case "secret": fixture.Options.SharedSecret = "another-fixture-secret"; break;
            }
            return Task.CompletedTask;
        };
        Assert.False(await fixture.Service.IsEligibleAsync(fixture.User.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IsEligibleAsync_ShouldPreserveNoPersonLocalEligibility_WhenDisabledOrUncovered(bool disabled)
    {
        using var fixture = new Fixture();
        fixture.User.PersonId = null;
        fixture.User.Person = null;
        await fixture.Database.SaveChangesAsync();
        if (disabled) fixture.Options.Enabled = false;
        else fixture.Options.RequiredAccounts[0].LocalAccountId = Guid.NewGuid();
        Assert.True(await fixture.Service.IsEligibleAsync(fixture.User.Id));
        Assert.Equal(0, fixture.Client.Calls);
        fixture.User.IsActive = false;
        await fixture.Database.SaveChangesAsync();
        Assert.False(await fixture.Service.IsEligibleAsync(fixture.User.Id));
    }

    [Fact]
    public async Task IsEligibleAsync_ShouldPreserveLocalBehavior_WhenDisabledScopeContainsInvalidUnusedEntry()
    {
        using var fixture = new Fixture();
        fixture.Options.Enabled = false;
        fixture.Options.RequiredAccounts = [null!];
        Assert.True(await fixture.Service.IsEligibleAsync(fixture.User.Id));
        Assert.Equal(0, fixture.Client.Calls);
    }

    [Fact]
    public async Task IsEligibleAsync_ShouldLookupAgain_WhenCalledAtLaterCheckpoint()
    {
        using var fixture = new Fixture();
        Assert.True(await fixture.Service.IsEligibleAsync(fixture.User.Id));
        fixture.Response = fixture.Response with { AccountState = ProviderLifecycleAccountState.Disabled };
        Assert.False(await fixture.Service.IsEligibleAsync(fixture.User.Id));
        Assert.Equal(2, fixture.Client.Calls);
    }

    [Fact]
    public async Task IsEligibleAsync_ShouldPropagateCancellation_EvenWhenClientReturnsEvidence()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Client.BeforeReturn = () => { cancellation.Cancel(); return Task.CompletedTask; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service.IsEligibleAsync(fixture.User.Id, cancellation.Token));
    }

    [Fact]
    public async Task IsEligibleAsync_ShouldDeny_WhenClientThrows()
    {
        using var fixture = new Fixture();
        fixture.Client.BeforeReturn = () => throw new HttpRequestException("synthetic failure");
        Assert.False(await fixture.Service.IsEligibleAsync(fixture.User.Id));
    }

    [Fact]
    public async Task IsEligibleAsync_ShouldDeny_WhenEnabledPolicyInvalid()
    {
        using var fixture = new Fixture();
        fixture.Options.MaxAgeSeconds = 0;
        Assert.False(await fixture.Service.IsEligibleAsync(fixture.User.Id));
        Assert.Equal(0, fixture.Client.Calls);
    }

    [Fact]
    public void Clock_ShouldLatchDenial_WhenBackwardMovementDetectedAcrossCheckpoints()
    {
        var time = new TestTimeProvider();
        var clock = new ProviderLifecycleClock(time);
        Assert.True(clock.TryGetUtcNow(out _));
        time.Now = Fixture.Now.AddTicks(-1);
        Assert.False(clock.TryGetUtcNow(out _));
        time.Now = Fixture.Now.AddMinutes(1);
        Assert.False(clock.TryGetUtcNow(out _));
    }

    private sealed class Fixture : IDisposable
    {
        public static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
        private readonly DbContextOptions<ApplicationDbContext> _dbOptions =
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        public ApplicationDbContext Database { get; }
        public ApplicationUser User { get; } = new()
            { Id = Guid.NewGuid(), IsActive = true, SecurityStamp = "security", ConcurrencyStamp = "concurrency" };
        public Person Person { get; } = new() { Id = Guid.NewGuid(), Status = PersonStatus.Active };
        public ProviderSubjectDirectoryBinding DirectoryBinding { get; }
        public ProviderLifecycleBinding Tuple { get; } = new() { ProviderNamespace = "issuer", StableSubject = "Subject" };
        public ProviderLifecycleResponse Response { get; set; }
        public ProviderLifecycleOptions Options { get; }
        public TestTimeProvider Time { get; } = new();
        public TestClient Client { get; }
        public CurrentUserLifecycleEligibility Service { get; }

        public Fixture(bool external = false)
        {
            Database = FreshDatabase();
            User.PersonId = Person.Id;
            DirectoryBinding = new ProviderSubjectDirectoryBinding(User.Id, "issuer", "Subject", Guid.NewGuid(), Now.UtcDateTime);
            Database.AddRange(User, Person);
            if (external) Database.UserLogins.Add(new IdentityUserLogin<Guid>
                { UserId = User.Id, LoginProvider = "ExternalAuth", ProviderKey = "Subject" });
            else Database.ProviderSubjectDirectoryBindings.Add(DirectoryBinding);
            Database.SaveChanges();
            Options = new ProviderLifecycleOptions
            {
                Enabled = true, Endpoint = "https://fixture.invalid/lifecycle", SharedSecret = "fixture-only",
                RequiredAccounts = [new ProviderLifecycleRequiredAccount
                {
                    LocalAccountId = User.Id, ProviderNamespace = "issuer", SourceAuthority = "authority",
                    MappingVersion = "mapping-1", BindingKind = external ? "ExternalLogin" : "DirectoryBinding",
                    ExternalLoginProvider = external ? "ExternalAuth" : null
                }]
            };
            Response = new ProviderLifecycleResponse
            {
                RequestId = Guid.NewGuid().ToString("D"), Binding = Tuple, Outcome = ProviderLifecycleOutcome.Found,
                AccountState = ProviderLifecycleAccountState.Enabled,
                Evidence = new ProviderLifecycleEvidence
                {
                    SourceAuthority = "authority", MappingVersion = "mapping-1", SnapshotVersion = "snapshot-1",
                    ObservedAt = Now.AddSeconds(-1), EffectiveFrom = Now.AddMinutes(-5), EffectiveUntil = Now.AddMinutes(5)
                }
            };
            Client = new TestClient(() => Response);
            Service = new CurrentUserLifecycleEligibility(Database, Microsoft.Extensions.Options.Options.Create(Options),
                Client, new ProviderLifecycleClock(Time));
        }

        public ApplicationDbContext FreshDatabase() => new(_dbOptions);
        public void Dispose() => Database.Dispose();
        public async Task ChangeLocalAsync(string change)
        {
            await using var fresh = FreshDatabase();
            var user = await fresh.Users.SingleAsync();
            var person = await fresh.Persons.SingleAsync();
            switch (change)
            {
                case "inactive": user.IsActive = false; break;
                case "deleted": user.IsDeleted = true; break;
                case "lockout": user.LockoutEnd = Now.AddMinutes(1); break;
                case "missing-person": user.PersonId = Guid.NewGuid(); break;
                case "person-deleted": person.IsDeleted = true; break;
                case "person-suspended": person.Status = PersonStatus.Suspended; break;
                case "person-future": person.StartDate = Now.AddDays(1).UtcDateTime; break;
                case "person-expired": person.EndDate = Now.AddDays(-1).UtcDateTime; break;
                case "person-link": user.PersonId = null; break;
                case "security-stamp": user.SecurityStamp = "changed"; break;
                case "concurrency-stamp": user.ConcurrencyStamp = "changed"; break;
            }
            await fresh.SaveChangesAsync();
        }
    }

    private sealed class TestClient(Func<ProviderLifecycleResponse> response) : IProviderLifecycleClient
    {
        public int Calls { get; private set; }
        public ProviderLifecycleBinding? LastBinding { get; private set; }
        public Func<Task>? BeforeReturn { get; set; }
        public ProviderLifecycleFailure Failure { get; set; }
        public bool SuppressResponse { get; set; }
        public async Task<ProviderLifecycleLookupResult> LookupAsync(ProviderLifecycleBinding binding,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastBinding = binding;
            if (BeforeReturn is not null) await BeforeReturn();
            return new ProviderLifecycleLookupResult { Failure = Failure, Response = SuppressResponse ? null : response() };
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = Fixture.Now;
        public bool Unreadable { get; set; }
        public override DateTimeOffset GetUtcNow() => Unreadable ? throw new InvalidOperationException("clock uncertain") : Now;
    }
}
