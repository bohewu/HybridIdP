using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Directory;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class Stage1DirectoryLookupAndBindingTests
{
    [Theory]
    [InlineData("disabled")]
    [InlineData("untrusted")]
    [InlineData("paused")]
    public async Task BindAndRefreshAsync_ShouldPreserveNativeFieldsWithoutNativeAuthorityButRetainBindingAndContact(string mode)
    {
        await using var context = CreateContext();
        var person = new Person { Id = Guid.NewGuid() };
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "local", Person = person, PersonId = person.Id };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var policy = new NativeFieldPolicyService(context,
            Options.Create(new Core.Application.Options.NativeFieldPolicyOptions
            {
                Stage1Enabled = mode != "disabled",
                TrustedStage1ProviderNamespaces = mode == "untrusted" ? [] : ["example.provider"]
            }), Options.Create(new ProviderProofOptions()), Options.Create(new DirectoryIntegrationOptions { Enabled = true }));
        if (mode == "paused") await policy.SetPausedAsync(NativeFieldSource.Stage1, new(true, Guid.Empty));
        var result = await new Stage1BindingRefreshService(context, policy).BindAndRefreshAsync(new(
            user.Id, "example.provider", "subject", ManagedIdentity("account", profile: new AssuredProfile
            {
                DisplayName = "source", Email = "contact@example.test",
                AssuredFields = [AssuredProfileField.DisplayName, AssuredProfileField.Email]
            })));
        Assert.Equal(Stage1BindingRefreshOutcome.BoundAndRefreshed, result);
        Assert.Single(context.ProviderSubjectDirectoryBindings);
        Assert.Null(user.Nickname);
        Assert.Null(person.Nickname);
        Assert.Equal("contact@example.test", user.Email);
        Assert.Equal("contact@example.test", person.Email);
    }

    [Fact]
    public async Task BindAndRefreshAsync_ShouldKeepFixedBlanksAcrossRepeatRefreshAndApplyAfterUnlock()
    {
        await using var context = CreateContext();
        var person = new Person { Id = Guid.NewGuid() };
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "local", Person = person, PersonId = person.Id };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var policy = CreateNativeFields(context);
        var service = new Stage1BindingRefreshService(context, policy);
        var request = new Stage1BindingRefreshRequest(user.Id, "example.provider", "subject",
            ManagedIdentity("account", profile: new AssuredProfile
            { DisplayName = "source", AssuredFields = [AssuredProfileField.DisplayName] }));
        Assert.Equal(Stage1BindingRefreshOutcome.BoundAndRefreshed, await service.BindAndRefreshAsync(request));
        Assert.Equal("source", user.Nickname);
        Assert.Equal("source", person.Nickname);
        policy.ApplyManual(user, new Dictionary<NativeField, string?> { [NativeField.Nickname] = "" });
        policy.ApplyManual(person, new Dictionary<NativeField, string?> { [NativeField.Nickname] = "" });
        await context.SaveChangesAsync();
        for (var i = 0; i < 2; i++)
            Assert.Equal(Stage1BindingRefreshOutcome.ExistingBindingRefreshed, await service.BindAndRefreshAsync(request));
        Assert.Equal("", user.Nickname);
        Assert.Equal("", person.Nickname);
        await policy.UpdateAsync(NativeFieldTarget.User, user.Id, NativeField.Nickname, new()
        {
            Operation = NativeFieldOperation.Unlock,
            ExpectedRevision = (await policy.GetTargetAsync(NativeFieldTarget.User, user.Id)).Revision
        });
        await service.BindAndRefreshAsync(request);
        Assert.Equal("source", user.Nickname);
        Assert.Equal("", person.Nickname);
    }

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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task BindAndRefreshAsync_EligibleIdentity_StoresImmutableBindingAndAllowlistedProfile(
        bool emailMfaEnabled, bool emailConfirmed)
    {
        await using var context = CreateContext();
        var person = new Person { Id = Guid.NewGuid(), Email = "old@example.test", FirstName = "Old" };
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "local",
            PersonId = person.Id,
            Email = "old@example.test",
            EmailMfaEnabled = emailMfaEnabled,
            EmailConfirmed = emailConfirmed,
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
        var service = new Stage1BindingRefreshService(context,
            CreateNativeFields(context));

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
        context.ChangeTracker.Clear();
        var refreshedUser = await context.Users.SingleAsync();
        var refreshedPerson = await context.Persons.SingleAsync();
        Assert.Equal("Display Name", refreshedUser.Nickname);
        Assert.Equal("Old", refreshedUser.FirstName); // Nonempty unattributed values are preserved.
        Assert.Equal("Surname", refreshedUser.LastName);
        Assert.Equal(emailMfaEnabled ? "old@example.test" : "new@example.test", refreshedUser.Email);
        Assert.Equal(emailMfaEnabled, refreshedUser.EmailMfaEnabled);
        Assert.Equal(emailConfirmed, refreshedUser.EmailConfirmed);
        Assert.Equal("Department", refreshedUser.Department);
        Assert.Equal("Title", refreshedUser.JobTitle);
        Assert.Equal("Employee", refreshedUser.EmployeeId);
        Assert.Equal("Display Name", refreshedPerson.Nickname);
        Assert.Equal("new@example.test", refreshedPerson.Email);
    }

    [Fact]
    public async Task BindAndRefreshAsync_ShouldReturnConflictWithoutPartialWrites_WhenEnrollmentChangedAfterRead()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var person = new Person { Id = Guid.NewGuid(), Email = "contact@example.test" };
        var user = new ApplicationUser
        {
            UserName = "local", Email = "factor@example.test", EmailConfirmed = true,
            EmailMfaCode = "original-pending-hash", EmailMfaCodeExpiry = DateTime.UtcNow.AddMinutes(5),
            EmailMfaVerificationAttempts = 2,
            Person = person, PersonId = person.Id, ConcurrencyStamp = "before-enrollment"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        await using (var enrollmentContext = new ApplicationDbContext(options))
        {
            var enrolled = await enrollmentContext.Users.SingleAsync();
            enrolled.EmailMfaEnabled = true;
            enrolled.ConcurrencyStamp = "after-enrollment";
            await enrollmentContext.SaveChangesAsync();
        }

        var outcome = await new Stage1BindingRefreshService(context,
            CreateNativeFields(context)).BindAndRefreshAsync(new Stage1BindingRefreshRequest(
            user.Id, "example.provider", "subject", ManagedIdentity("account", profile: new AssuredProfile
            {
                Email = "upstream@example.test", AssuredFields = [AssuredProfileField.Email]
            })));

        Assert.Equal(Stage1BindingRefreshOutcome.Conflict, outcome);
        await context.SaveChangesAsync(); // Failed tracked writes must not leak through a later save.
        context.ChangeTracker.Clear();
        Assert.Empty(await context.ProviderSubjectDirectoryBindings.ToListAsync());
        Assert.Equal("contact@example.test", (await context.Persons.SingleAsync()).Email);
        var persisted = await context.Users.SingleAsync();
        Assert.Equal("factor@example.test", persisted.Email);
        Assert.Equal("original-pending-hash", persisted.EmailMfaCode);
        Assert.NotNull(persisted.EmailMfaCodeExpiry);
        Assert.Equal(2, persisted.EmailMfaVerificationAttempts);
        Assert.True(persisted.EmailMfaEnabled);
    }

    [Fact]
    public async Task BindAndRefreshAsync_UnassuredProfile_CreatesNoPartialBindingOrProfileRefresh()
    {
        await using var context = CreateContext();
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "local", Email = "old@example.test" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var service = new Stage1BindingRefreshService(context,
            CreateNativeFields(context));
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
        var service = new Stage1BindingRefreshService(context,
            CreateNativeFields(context));
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
        var service = new Stage1BindingRefreshService(context,
            CreateNativeFields(context));

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
    private static global::Infrastructure.Services.NativeFieldPolicyService CreateNativeFields(Core.Application.IApplicationDbContext context) =>
        new(context,
            Microsoft.Extensions.Options.Options.Create(new Core.Application.Options.NativeFieldPolicyOptions { Stage1Enabled = true, TrustedStage1ProviderNamespaces = ["example.provider"] }),
            Microsoft.Extensions.Options.Options.Create(new global::Infrastructure.Options.ProviderProofOptions()),
            Microsoft.Extensions.Options.Options.Create(new global::Infrastructure.Options.DirectoryIntegrationOptions { Enabled = true }));
}
