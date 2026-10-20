using Core.Application.Options;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class NativeFieldPolicyServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("Given")]
    public async Task FirstNameFallback_ShouldPreferGivenNameAndFillOnlyMissingTargets(string? givenName)
    {
        using var f = new Fixture();
        await using var db = f.Open();
        var (user, person, identity) = await SeedAsync(db);
        var service = Service(db);
        await service.SetFirstNameFallbackAsync(NativeFieldSource.Proof, new(identity.ProviderNamespace, true, Guid.Empty));
        var values = Values("Whole Display Name");
        values[NativeField.FirstName] = givenName;
        await service.ApplySourceAsync(user, person, identity, values);
        await db.SaveChangesAsync();
        var expected = string.IsNullOrWhiteSpace(givenName) ? "Whole Display Name" : givenName;
        Assert.Equal(expected, user.FirstName);
        Assert.Equal(expected, person.FirstName);
        Assert.Equal("Whole Display Name", user.Nickname);
        Assert.Equal("Whole Display Name", person.Nickname);
        Assert.Null(user.LastName);
        Assert.Null(person.LastName);
        var state = await service.GetTargetAsync(NativeFieldTarget.User, user.Id);
        Assert.Equal(identity, state.Fields.Single(x => x.Field == NativeField.FirstName).Owner);
    }

    [Theory]
    [InlineData(NativeFieldTarget.User, false)]
    [InlineData(NativeFieldTarget.Person, false)]
    [InlineData(NativeFieldTarget.User, true)]
    [InlineData(NativeFieldTarget.Person, true)]
    public async Task FirstNameFallback_ShouldPreservePopulatedOrFixedBlankIndependentlyEvenWithOverwrite(
        NativeFieldTarget protectedTarget, bool fixedBlank)
    {
        using var f = new Fixture();
        await using var db = f.Open();
        var (user, person, identity) = await SeedAsync(db);
        var service = Service(db);
        await service.SetFirstNameFallbackAsync(NativeFieldSource.Proof, new(identity.ProviderNamespace, true, Guid.Empty));
        var protectedId = protectedTarget == NativeFieldTarget.User ? user.Id : person.Id;
        if (fixedBlank)
            await service.UpdateAsync(protectedTarget, protectedId, NativeField.FirstName, new()
            {
                Operation = NativeFieldOperation.Manual, ManualValue = "", ExpectedRevision = Guid.Empty
            });
        else
        {
            if (protectedTarget == NativeFieldTarget.User) user.FirstName = "Existing";
            else person.FirstName = "Existing";
            await db.SaveChangesAsync();
        }
        await AssignFirstNameAsync(service, protectedTarget, protectedId, identity);
        await service.ApplySourceAsync(user, person, identity, Values("Whole Name"));
        await db.SaveChangesAsync();
        Assert.Equal(fixedBlank ? "" : "Existing", protectedTarget == NativeFieldTarget.User ? user.FirstName : person.FirstName);
        Assert.Equal("Whole Name", protectedTarget == NativeFieldTarget.User ? person.FirstName : user.FirstName);
        var values = Values("Another Whole Name");
        values[NativeField.FirstName] = "Assured Given";
        await service.ApplySourceAsync(user, person, identity, values);
        Assert.Equal(fixedBlank ? "" : "Assured Given", protectedTarget == NativeFieldTarget.User ? user.FirstName : person.FirstName);
    }

    [Fact]
    public async Task FirstNameFallback_ShouldRequireExactNamespaceRevisionTrustBindingAndResume()
    {
        using var f = new Fixture();
        await using var db = f.Open();
        var (user, person, identity) = await SeedAsync(db);
        var service = Service(db);
        Assert.False((await service.GetSourcesAsync()).Single(x => x.Source == NativeFieldSource.Proof).FirstNameDisplayNameFallback);
        await service.ApplySourceAsync(user, person, identity, Values("Default Off"));
        Assert.Null(user.FirstName);
        Assert.Null(person.FirstName);
        await db.SaveChangesAsync();
        await service.SetFirstNameFallbackAsync(NativeFieldSource.Proof, new(identity.ProviderNamespace, true, Guid.Empty));
        Assert.Equal("conflict", (await Assert.ThrowsAsync<NativeFieldPolicyException>(() =>
            service.SetFirstNameFallbackAsync(NativeFieldSource.Proof, new(identity.ProviderNamespace, false, Guid.Empty)))).Code);
        var status = (await service.GetSourcesAsync()).Single(x => x.Source == NativeFieldSource.Proof);
        Assert.True(status.FirstNameDisplayNameFallback);
        Assert.Equal(identity.ProviderNamespace, status.ProviderNamespace);
        Assert.Equal("conflict", (await Assert.ThrowsAsync<NativeFieldPolicyException>(() =>
            service.SetFirstNameFallbackAsync(NativeFieldSource.Proof, new("EXAMPLE.PROVIDER", true, status.Revision)))).Code);
        Assert.Equal("source_unavailable", (await Assert.ThrowsAsync<NativeFieldPolicyException>(() =>
            Service(db, proofEnabled: false).SetFirstNameFallbackAsync(NativeFieldSource.Proof,
                new(identity.ProviderNamespace, true, status.Revision)))).Code);
        Assert.Equal("unsupported_source", (await Assert.ThrowsAsync<NativeFieldPolicyException>(() =>
            service.SetFirstNameFallbackAsync(NativeFieldSource.Stage1, new(identity.ProviderNamespace, true, status.Revision)))).Code);
        foreach (var disabled in new[] { Service(db, proofEnabled: false), Service(db, nativeEnabled: false) })
            Assert.False(await disabled.ApplySourceAsync(user, person, identity, Values("Denied")));
        Assert.False(await service.ApplySourceAsync(user, person, identity with { StableSubject = "unbound" }, Values("Denied")));
        Assert.False(await service.ApplySourceAsync(user, person, identity with { ProviderNamespace = "EXAMPLE.PROVIDER" }, Values("Denied")));
        await service.SetPausedAsync(NativeFieldSource.Proof, new(true, status.Revision));
        Assert.False(await service.ApplySourceAsync(user, person, identity, Values("Paused")));
        status = (await service.GetSourcesAsync()).Single(x => x.Source == NativeFieldSource.Proof);
        Assert.True(status.FirstNameDisplayNameFallback);
        await service.SetPausedAsync(NativeFieldSource.Proof, new(false, status.Revision));
        Assert.Null(user.FirstName);
        // A new deployment namespace and even its exact binding cannot inherit the old selection.
        var newIdentity = identity with { ProviderNamespace = "other.provider" };
        db.UserLogins.Add(new() { UserId = user.Id, LoginProvider = newIdentity.ProviderNamespace, ProviderKey = identity.StableSubject });
        await db.SaveChangesAsync();
        var newService = Service(db, providerNamespace: newIdentity.ProviderNamespace);
        Assert.False((await newService.GetSourcesAsync()).Single(x => x.Source == NativeFieldSource.Proof).FirstNameDisplayNameFallback);
        await newService.ApplySourceAsync(user, person, newIdentity, Values("Different Source"));
        Assert.Null(user.FirstName);
        Assert.Null(person.FirstName);
        await service.ApplySourceAsync(user, person, identity, Values("Resumed"));
        Assert.Equal("Resumed", user.FirstName);
        Assert.Equal("Resumed", person.FirstName);
    }

    [Fact]
    public async Task FirstNameFallback_ShouldRespectSharedPersonOwnership()
    {
        using var f = new Fixture();
        await using var db = f.Open();
        var (user, person, identity) = await SeedAsync(db);
        var other = new ApplicationUser { Id = Guid.NewGuid(), PersonId = person.Id };
        db.Users.Add(other);
        db.UserLogins.Add(new() { UserId = other.Id, LoginProvider = identity.ProviderNamespace, ProviderKey = "other" });
        await db.SaveChangesAsync();
        var service = Service(db);
        await service.SetFirstNameFallbackAsync(NativeFieldSource.Proof, new(identity.ProviderNamespace, true, Guid.Empty));
        await service.ApplySourceAsync(user, person, identity, Values("First Account"));
        Assert.Null(person.FirstName);
        await db.SaveChangesAsync();
        await AssignFirstNameAsync(service, NativeFieldTarget.Person, person.Id, identity);
        var otherIdentity = identity with { LocalAccountId = other.Id, StableSubject = "other" };
        await service.ApplySourceAsync(other, person, otherIdentity, Values("Other Account"));
        Assert.Equal("Other Account", other.FirstName);
        Assert.Null(person.FirstName);
        await service.ApplySourceAsync(user, person, identity, Values("Owner Name"));
        Assert.Equal("Owner Name", person.FirstName);
    }

    [Fact]
    public async Task FirstNameFallback_ShouldSkipMissingOrOversizedDisplayWithoutUsingStoredNickname()
    {
        using var f = new Fixture();
        await using var db = f.Open();
        var (user, person, identity) = await SeedAsync(db);
        var service = Service(db);
        await service.SetFirstNameFallbackAsync(NativeFieldSource.Proof, new(identity.ProviderNamespace, true, Guid.Empty));
        user.Nickname = "Stored Name";
        person.Nickname = "Stored Name";
        foreach (var value in new string?[] { null, "", " \t", new('x', 101) })
        {
            await service.ApplySourceAsync(user, person, identity, Values(value));
            Assert.Null(user.FirstName);
            Assert.Null(person.FirstName);
        }
    }

    private static async Task AssignFirstNameAsync(NativeFieldPolicyService service, NativeFieldTarget target, Guid id,
        NativeFieldSourceIdentity identity) => await service.UpdateAsync(target, id, NativeField.FirstName, new()
        {
            Operation = NativeFieldOperation.AssignSource, SourceIdentity = identity, AllowOverwrite = true,
            ExpectedRevision = (await service.GetTargetAsync(target, id)).Revision
        });

    [Theory]
    [InlineData(100)]
    [InlineData(101)]
    public async Task SourceNickname_ShouldRespectEachTargetLimitWithoutTruncation(int length)
    {
        using var f = new Fixture();
        await using var db = f.Open();
        var (user, person, identity) = await SeedAsync(db);
        var service = Service(db);
        var nickname = new string('x', length);
        Assert.True(await service.ApplySourceAsync(user, person, identity, Values(nickname)));
        await db.SaveChangesAsync();
        Assert.Equal(nickname, user.Nickname);
        Assert.Equal(length <= 100 ? nickname : null, person.Nickname);
        if (length > 100)
            Assert.Throws<NativeFieldPolicyException>(() => service.ApplyManual(person, Values(nickname)));
    }

    [Fact]
    public async Task Source_ShouldRequireDeploymentTrustAndExactPersistedBinding()
    {
        using var f = new Fixture();
        await using var db = f.Open();
        var (user, person, identity) = await SeedAsync(db);
        var service = Service(db);
        Assert.False(await Service(db, proofEnabled: false).ApplySourceAsync(user, person, identity, Values("source")));
        Assert.False(await Service(db, nativeEnabled: false).ApplySourceAsync(user, person, identity, Values("source")));
        Assert.False(await service.ApplySourceAsync(user, person, identity with { ProviderNamespace = "EXAMPLE.PROVIDER" }, Values("source")));
        Assert.False(await service.ApplySourceAsync(user, person, identity with { StableSubject = "other" }, Values("source")));
        Assert.False(await service.ApplySourceAsync(user, person, identity with { DirectoryObjectId = Guid.NewGuid() }, Values("source")));
        Assert.Null(user.Nickname);
        Assert.True(await service.ApplySourceAsync(user, person, identity, Values("source")));
        await db.SaveChangesAsync();
        Assert.Equal("source", person.Nickname);
        var target = await service.GetTargetAsync(NativeFieldTarget.User, user.Id);
        Assert.Equal(identity, target.Fields.Single(x => x.Field == NativeField.Nickname).Owner);
        Assert.True(new NativeFieldPolicyOptions().ProofEnabled);
        Assert.False(new NativeFieldPolicyOptions().Stage1Enabled);
        Assert.All(await Service(db, proofEnabled: false).GetSourcesAsync(), x => Assert.False(x.Enabled));
    }

    [Fact]
    public async Task Source_ShouldIgnoreOversizedValues_WhenExactBindingIsAbsent()
    {
        using var f = new Fixture();
        await using var db = f.Open();
        var (user, person, identity) = await SeedAsync(db);
        user.FirstName = "UserGiven";
        person.FirstName = "PersonGiven";
        await db.SaveChangesAsync();

        Assert.False(await Service(db).ApplySourceAsync(user, person, identity with { StableSubject = "other" },
            new Dictionary<NativeField, string?> { [NativeField.FirstName] = new string('x', 101) }));

        Assert.Equal("UserGiven", user.FirstName);
        Assert.Equal("PersonGiven", person.FirstName);
        Assert.Null(user.NativeFieldStateJson);
        Assert.Null(person.NativeFieldStateJson);
    }

    [Fact]
    public async Task Source_ShouldPreserveUnattributedAndMissingValuesUntilExplicitAdoption()
    {
        using var f = new Fixture();
        await using var db = f.Open();
        var (user, person, identity) = await SeedAsync(db);
        user.Nickname = "legacy";
        person.Nickname = "person legacy";
        await db.SaveChangesAsync();
        var service = Service(db);
        Assert.False(await service.ApplySourceAsync(user, person, identity, Values("new")));
        await AssignAsync(service, NativeFieldTarget.User, user.Id, identity);
        Assert.True(await service.ApplySourceAsync(user, person, identity, Values("new")));
        await db.SaveChangesAsync();
        Assert.Equal("new", user.Nickname);
        Assert.Equal("person legacy", person.Nickname);
        foreach (var missing in new string?[] { null, "", " \t" })
            Assert.False(await service.ApplySourceAsync(user, person, identity, Values(missing)));
        Assert.Equal("new", user.Nickname);
    }

    [Fact]
    public async Task ManualBlank_ShouldRemainFixedUntilUnlockAndKeepIndependentPersonState()
    {
        using var f = new Fixture();
        await using var db = f.Open();
        var (user, person, identity) = await SeedAsync(db);
        var service = Service(db);
        await AssignAsync(service, NativeFieldTarget.User, user.Id, identity);
        await service.UpdateAsync(NativeFieldTarget.User, user.Id, NativeField.Nickname, new()
        {
            Operation = NativeFieldOperation.Manual, ManualValue = "",
            ExpectedRevision = (await service.GetTargetAsync(NativeFieldTarget.User, user.Id)).Revision
        });
        Assert.True(await service.ApplySourceAsync(user, person, identity, Values("source")));
        await db.SaveChangesAsync();
        Assert.Equal("", user.Nickname);
        Assert.Equal("source", person.Nickname);
        Service(db, nativeEnabled: false).ApplyUnattributed(user, person, Values("browser"));
        Assert.Equal("", user.Nickname);
        await service.UpdateAsync(NativeFieldTarget.User, user.Id, NativeField.Nickname, new()
        {
            Operation = NativeFieldOperation.Unlock,
            ExpectedRevision = (await service.GetTargetAsync(NativeFieldTarget.User, user.Id)).Revision
        });
        Assert.True(await service.ApplySourceAsync(user, person, identity, Values("after unlock")));
        Assert.Equal("after unlock", user.Nickname);
    }

    [Fact]
    public async Task SharedPerson_ShouldRequireExplicitOwnerAndRejectOtherAccountsEvenOnAssignment()
    {
        using var f = new Fixture();
        await using var db = f.Open();
        var (first, person, identity) = await SeedAsync(db);
        var other = new ApplicationUser { Id = Guid.NewGuid(), PersonId = person.Id };
        db.Users.Add(other);
        db.UserLogins.Add(new() { UserId = other.Id, LoginProvider = identity.ProviderNamespace, ProviderKey = "second" });
        await db.SaveChangesAsync();
        var service = Service(db);
        Assert.True(await service.ApplySourceAsync(first, person, identity, Values("first")));
        Assert.Null(person.Nickname);
        await db.SaveChangesAsync();
        await AssignAsync(service, NativeFieldTarget.Person, person.Id, identity);
        Assert.True(await service.ApplySourceAsync(first, person, identity, Values("owner")));
        await db.SaveChangesAsync();
        var otherIdentity = identity with { LocalAccountId = other.Id, StableSubject = "second" };
        Assert.True(await service.ApplySourceAsync(other, person, otherIdentity, Values("other")));
        Assert.Equal("other", other.Nickname);
        Assert.Equal("owner", person.Nickname);
        var exception = await Assert.ThrowsAsync<NativeFieldPolicyException>(() => AssignAsync(service, NativeFieldTarget.Person, person.Id, otherIdentity));
        Assert.Equal("owner_mismatch", exception.Code);
    }

    [Theory]
    [InlineData(NativeFieldTarget.User)]
    [InlineData(NativeFieldTarget.Person)]
    public async Task LateSourceSave_ShouldFailAfterAtomicManualClear(NativeFieldTarget target)
    {
        using var f = new Fixture();
        await using var stale = f.Open();
        var (user, person, identity) = await SeedAsync(stale);
        var staleService = Service(stale);
        var id = target == NativeFieldTarget.User ? user.Id : person.Id;
        Assert.True(await staleService.ApplySourceAsync(user, person, identity, Values("late")));
        await using (var winner = f.Open())
        {
            await Service(winner).UpdateAsync(target, id, NativeField.Nickname, new()
            {
                Operation = NativeFieldOperation.Manual, ManualValue = "", ExpectedRevision = Guid.Empty
            });
        }
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        stale.ChangeTracker.Clear();
        await using var verify = f.Open();
        var saved = await Service(verify).GetTargetAsync(target, id);
        var field = saved.Fields.Single(x => x.Field == NativeField.Nickname);
        Assert.Equal("", field.Value);
        Assert.True(field.ManualFixed);
        Assert.False(field.Attributed);
        // The failed SaveChanges transaction also rolls back the other entity's source write.
        Assert.Null(target == NativeFieldTarget.User
            ? (await verify.Persons.SingleAsync()).Nickname : (await verify.Users.SingleAsync()).Nickname);
    }

    [Fact]
    public async Task PauseAndCapabilityRejection_ShouldRetainValuesAndConfiguration()
    {
        using var f = new Fixture();
        await using var db = f.Open();
        var (user, person, identity) = await SeedAsync(db);
        var service = Service(db);
        await AssignAsync(service, NativeFieldTarget.User, user.Id, identity);
        await service.SetPausedAsync(NativeFieldSource.Proof, new(true, Guid.Empty));
        Assert.False(await service.ApplySourceAsync(user, person, identity, Values("paused")));
        Assert.Equal("source_unavailable", (await Assert.ThrowsAsync<NativeFieldPolicyException>(() => AssignAsync(service, NativeFieldTarget.User, user.Id, identity))).Code);
        var status = (await service.GetSourcesAsync()).Single(x => x.Source == NativeFieldSource.Proof);
        Assert.True(status.Paused);
        Assert.False(status.SupportsClear);
        Assert.NotEmpty(status.ClearUnavailableReason);
        await service.SetPausedAsync(NativeFieldSource.Proof, new(false, status.Revision));
        Assert.True(await service.ApplySourceAsync(user, person, identity, Values("resumed")));
        Assert.Equal("source_clear_unsupported", (await Assert.ThrowsAsync<NativeFieldPolicyException>(() => service.UpdateAsync(
            NativeFieldTarget.User, user.Id, NativeField.Nickname, new() { AllowSourceClear = true }))).Code);
        Assert.Single(await db.UserLogins.ToListAsync());
    }

    [Fact]
    public async Task Stage1_ShouldRequireExactPersistedDirectoryObjectAndExplicitTrustedCapability()
    {
        using var f = new Fixture();
        await using var db = f.Open();
        var (user, person, proof) = await SeedAsync(db);
        var identity = proof with { Source = NativeFieldSource.Stage1, DirectoryObjectId = Guid.NewGuid() };
        var options = new NativeFieldPolicyOptions { Stage1Enabled = true, TrustedStage1ProviderNamespaces = [proof.ProviderNamespace] };
        var service = new NativeFieldPolicyService(db, Options.Create(options), Options.Create(new ProviderProofOptions()), Options.Create(new DirectoryIntegrationOptions { Enabled = true }));
        Assert.False(await service.ApplySourceAsync(user, person, identity, Values("unbound")));
        db.ProviderSubjectDirectoryBindings.Add(new(user.Id, identity.ProviderNamespace, identity.StableSubject, identity.DirectoryObjectId!.Value, DateTime.UtcNow));
        await db.SaveChangesAsync();
        Assert.False(await service.ApplySourceAsync(user, person, identity with { DirectoryObjectId = Guid.NewGuid() }, Values("wrong object")));
        Assert.True(await service.ApplySourceAsync(user, person, identity, Values("directory")));
        Assert.Equal("directory", person.Nickname);
    }

    [Fact]
    public async Task BoundsAndManualOmission_ShouldNotMutateUnspecifiedFields()
    {
        using var f = new Fixture();
        await using var db = f.Open();
        var (user, person, identity) = await SeedAsync(db);
        var service = Service(db);
        Assert.Equal("value_too_long", (await Assert.ThrowsAsync<NativeFieldPolicyException>(() => service.ApplySourceAsync(user, person, identity, Values(new string('x', 257))))).Code);
        var oversizedFirstName = new Dictionary<NativeField, string?> { [NativeField.FirstName] = new string('x', 101) };
        Assert.Equal("value_too_long", (await Assert.ThrowsAsync<NativeFieldPolicyException>(() => service.ApplySourceAsync(user, person, identity, oversizedFirstName))).Code);
        Assert.Equal("value_too_long", Assert.Throws<NativeFieldPolicyException>(() => service.ApplyManual(user, oversizedFirstName)).Code);
        Assert.Equal("value_too_long", Assert.Throws<NativeFieldPolicyException>(() => service.ApplyManual(person, oversizedFirstName)).Code);
        Assert.Equal("unsupported_field", (await Assert.ThrowsAsync<NativeFieldPolicyException>(() => service.ApplySourceAsync(user, person, identity, new Dictionary<NativeField, string?> { [(NativeField)999] = "no" }))).Code);
        service.ApplyManual(user, new Dictionary<NativeField, string?> { [NativeField.FirstName] = "fixed", [NativeField.LastName] = null });
        await db.SaveChangesAsync();
        var state = await service.GetTargetAsync(NativeFieldTarget.User, user.Id);
        Assert.True(state.Fields.Single(x => x.Field == NativeField.FirstName).ManualFixed);
        Assert.False(state.Fields.Single(x => x.Field == NativeField.LastName).ManualFixed);
        Assert.Null(person.NativeFieldStateJson);
        Assert.Equal("conflict", (await Assert.ThrowsAsync<NativeFieldPolicyException>(() => service.UpdateAsync(NativeFieldTarget.User, user.Id, NativeField.FirstName,
            new() { Operation = NativeFieldOperation.Manual, ManualValue = "stale", ExpectedRevision = Guid.Empty }))).Code);
    }

    private static Dictionary<NativeField, string?> Values(string? nickname) => new() { [NativeField.Nickname] = nickname };

    [Fact]
    public async Task ManualEmployeeClear_ShouldSupportMultiplePersonsAndRemainProtected()
    {
        using var f = new Fixture();
        await using var db = f.Open();
        var (user, first, identity) = await SeedAsync(db);
        first.EmployeeId = "first";
        var second = new Person { Id = Guid.NewGuid(), EmployeeId = "second" };
        db.Persons.Add(second);
        await db.SaveChangesAsync();
        var service = Service(db);
        service.ApplyManual(first, new Dictionary<NativeField, string?> { [NativeField.EmployeeId] = "" });
        service.ApplyManual(second, new Dictionary<NativeField, string?> { [NativeField.EmployeeId] = "" });
        await db.SaveChangesAsync();
        Assert.Null(first.EmployeeId);
        Assert.Null(second.EmployeeId);
        await service.ApplySourceAsync(user, first, identity, new Dictionary<NativeField, string?> { [NativeField.EmployeeId] = "source" });
        Assert.Null(first.EmployeeId);
        var state = await service.GetTargetAsync(NativeFieldTarget.Person, first.Id);
        Assert.True(state.Fields.Single(x => x.Field == NativeField.EmployeeId).ManualFixed);
    }
    private static NativeFieldPolicyService Service(ApplicationDbContext db, bool proofEnabled = true, bool nativeEnabled = true,
        string providerNamespace = "example.provider") => new(db,
        Options.Create(new NativeFieldPolicyOptions { ProofEnabled = nativeEnabled }),
        Options.Create(new ProviderProofOptions { Enabled = proofEnabled, TrustedProviderNamespace = providerNamespace }),
        Options.Create(new DirectoryIntegrationOptions()));
    private static async Task AssignAsync(NativeFieldPolicyService service, NativeFieldTarget target, Guid id, NativeFieldSourceIdentity identity) =>
        await service.UpdateAsync(target, id, NativeField.Nickname, new()
        {
            Operation = NativeFieldOperation.AssignSource, SourceIdentity = identity, AllowOverwrite = true,
            ExpectedRevision = (await service.GetTargetAsync(target, id)).Revision
        });
    private static async Task<(ApplicationUser, Person, NativeFieldSourceIdentity)> SeedAsync(ApplicationDbContext db)
    {
        var person = new Person { Id = Guid.NewGuid() };
        var user = new ApplicationUser { Id = Guid.NewGuid(), PersonId = person.Id };
        db.Persons.Add(person);
        db.Users.Add(user);
        db.UserLogins.Add(new() { UserId = user.Id, LoginProvider = "example.provider", ProviderKey = "subject" });
        await db.SaveChangesAsync();
        return (user, person, new(NativeFieldSource.Proof, user.Id, "example.provider", "subject"));
    }
    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public Fixture()
        {
            _connection.Open();
            using var db = Open();
            db.Database.EnsureCreated();
        }
        public ApplicationDbContext Open() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        public void Dispose() => _connection.Dispose();
    }
}
