using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Directory;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class Stage1DirectoryLookupAndBindingTests
{
    [Fact]
    public async Task FindManagedIdentityAsync_ExactlyOneEligibleMatch_ReturnsFound()
    {
        var identity = ManagedIdentity("account", profile: null);
        var transport = new StaticTransport(new ProtectedDirectoryTransportResult(
            ProtectedDirectoryTransportOutcome.Succeeded,
            [identity]));
        var lookup = CreateLookup(transport, DirectoryTransport.StartTls);

        var result = await lookup.FindManagedIdentityAsync("account");

        Assert.Equal(DirectoryLookupOutcome.Found, result.Outcome);
        Assert.Same(identity, result.Identity);
        Assert.Equal(DirectoryTransport.StartTls, transport.Transport);
    }

    [Fact]
    public async Task FindManagedIdentityAsync_MultipleMatches_ReturnsAmbiguous()
    {
        var transport = new StaticTransport(new ProtectedDirectoryTransportResult(
            ProtectedDirectoryTransportOutcome.Succeeded,
            [ManagedIdentity("account"), ManagedIdentity("account")]));
        var lookup = CreateLookup(transport, DirectoryTransport.Ldaps);

        var result = await lookup.FindManagedIdentityAsync("account");

        Assert.Equal(DirectoryLookupOutcome.Ambiguous, result.Outcome);
        Assert.Null(result.Identity);
    }

    [Fact]
    public async Task BindAndRefreshAsync_EligibleIdentity_StoresImmutableBindingAndAllowlistedProfile()
    {
        await using var context = CreateContext();
        var person = new Person { Id = Guid.NewGuid(), Email = "old@example.test", FirstName = "Old" };
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "local",
            PersonId = person.Id,
            Email = "old@example.test",
            FirstName = "Old"
        };
        context.Persons.Add(person);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var directoryObjectId = Guid.NewGuid();
        var profile = new AssuredProfile
        {
            DisplayName = "Display Name",
            GivenName = "Given",
            Surname = "Surname",
            Email = "new@example.test",
            Department = "Department",
            Title = "Title",
            EmployeeId = "Employee",
            AssuredFields =
            [
                AssuredProfileField.DisplayName,
                AssuredProfileField.GivenName,
                AssuredProfileField.Surname,
                AssuredProfileField.Email,
                AssuredProfileField.Department,
                AssuredProfileField.Title,
                AssuredProfileField.EmployeeId
            ]
        };
        var service = new Stage1BindingRefreshService(context);

        var outcome = await service.BindAndRefreshAsync(new Stage1BindingRefreshRequest(
            user.Id,
            "example.provider",
            "opaque-subject",
            ManagedIdentity("account", directoryObjectId, profile)));

        Assert.Equal(Stage1BindingRefreshOutcome.BoundAndRefreshed, outcome);
        var binding = await context.ProviderSubjectDirectoryBindings.SingleAsync();
        Assert.Equal(user.Id, binding.LocalAccountId);
        Assert.Equal("example.provider", binding.ProviderNamespace);
        Assert.Equal("opaque-subject", binding.StableSubject);
        Assert.Equal(directoryObjectId, binding.DirectoryObjectId);
        Assert.Equal("ACCOUNT", binding.NormalizedCanonicalAccountAlias);
        var refreshedUser = await context.Users.SingleAsync();
        var refreshedPerson = await context.Persons.SingleAsync();
        Assert.Equal("Display Name", refreshedUser.Nickname);
        Assert.Equal("Given", refreshedUser.FirstName);
        Assert.Equal("Surname", refreshedUser.LastName);
        Assert.Equal("new@example.test", refreshedUser.Email);
        Assert.Equal("Department", refreshedUser.Department);
        Assert.Equal("Title", refreshedUser.JobTitle);
        Assert.Equal("Employee", refreshedUser.EmployeeId);
        Assert.Equal("Display Name", refreshedPerson.Nickname);
        Assert.Equal("new@example.test", refreshedPerson.Email);
    }

    [Fact]
    public async Task BindAndRefreshAsync_UnassuredProfile_CreatesNoPartialBindingOrProfileRefresh()
    {
        await using var context = CreateContext();
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "local", Email = "old@example.test" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var service = new Stage1BindingRefreshService(context);
        var profile = new AssuredProfile { Email = "new@example.test" };

        var outcome = await service.BindAndRefreshAsync(new Stage1BindingRefreshRequest(
            user.Id,
            "example.provider",
            "opaque-subject",
            ManagedIdentity("account", profile: profile)));

        Assert.Equal(Stage1BindingRefreshOutcome.Invalid, outcome);
        Assert.Empty(context.ProviderSubjectDirectoryBindings);
        Assert.Equal("old@example.test", (await context.Users.SingleAsync()).Email);
    }

    [Fact]
    public async Task BindAndRefreshAsync_BlankValues_PreserveExistingProfileValues()
    {
        await using var context = CreateContext();
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "local", FirstName = "Existing" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var service = new Stage1BindingRefreshService(context);
        var profile = new AssuredProfile
        {
            GivenName = " ",
            Title = "Updated title",
            AssuredFields = [AssuredProfileField.Title]
        };

        var outcome = await service.BindAndRefreshAsync(new Stage1BindingRefreshRequest(
            user.Id,
            "example.provider",
            "opaque-subject",
            ManagedIdentity("account", profile: profile)));

        Assert.Equal(Stage1BindingRefreshOutcome.BoundAndRefreshed, outcome);
        var refreshedUser = await context.Users.SingleAsync();
        Assert.Equal("Existing", refreshedUser.FirstName);
        Assert.Equal("Updated title", refreshedUser.JobTitle);
    }

    [Fact]
    public async Task BindAndRefreshAsync_CanonicalAliasOwnedByDifferentBinding_ReturnsConflictWithoutMutation()
    {
        await using var context = CreateContext();
        var firstUser = new ApplicationUser { Id = Guid.NewGuid(), UserName = "first" };
        var secondUser = new ApplicationUser { Id = Guid.NewGuid(), UserName = "second" };
        var firstDirectoryObjectId = Guid.NewGuid();
        context.Users.AddRange(firstUser, secondUser);
        context.ProviderSubjectDirectoryBindings.Add(new ProviderSubjectDirectoryBinding(
            firstUser.Id,
            "example.provider",
            "first-subject",
            firstDirectoryObjectId,
            DateTime.UtcNow,
            "canonical-account"));
        await context.SaveChangesAsync();
        var service = new Stage1BindingRefreshService(context);

        var outcome = await service.BindAndRefreshAsync(new Stage1BindingRefreshRequest(
            secondUser.Id,
            "example.provider",
            "second-subject",
            ManagedIdentity("CANONICAL-ACCOUNT")));

        Assert.Equal(Stage1BindingRefreshOutcome.Conflict, outcome);
        var persisted = await context.ProviderSubjectDirectoryBindings.SingleAsync();
        Assert.Equal(firstUser.Id, persisted.LocalAccountId);
        Assert.Equal("first-subject", persisted.StableSubject);
        Assert.Equal(firstDirectoryObjectId, persisted.DirectoryObjectId);
        Assert.Equal("CANONICAL-ACCOUNT", persisted.NormalizedCanonicalAccountAlias);
    }

    private static ProtectedDirectoryIdentityLookup CreateLookup(
        IProtectedDirectoryIdentityTransport transport,
        DirectoryTransport directoryTransport) =>
        new(
            transport,
            Options.Create(new DirectoryIntegrationOptions { Enabled = true, Transport = directoryTransport }),
            Options.Create(new DirectoryLookupOptions { Timeout = TimeSpan.FromSeconds(1) }));

    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ManagedDirectoryIdentity ManagedIdentity(
        string account,
        Guid? objectId = null,
        AssuredProfile? profile = null) =>
        new(objectId ?? Guid.NewGuid(), account, true, true, false, profile);

    private sealed class StaticTransport : IProtectedDirectoryIdentityTransport
    {
        private readonly ProtectedDirectoryTransportResult _result;

        public StaticTransport(ProtectedDirectoryTransportResult result)
        {
            _result = result;
        }

        public DirectoryTransport? Transport { get; private set; }

        public Task<ProtectedDirectoryTransportResult> FindExactAsync(
            string canonicalAccount,
            DirectoryTransport transport,
            CancellationToken cancellationToken = default)
        {
            Transport = transport;
            return Task.FromResult(_result);
        }
    }
}
