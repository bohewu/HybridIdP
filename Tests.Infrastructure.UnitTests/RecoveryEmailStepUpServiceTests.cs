using Core.Application.Ports;
using Core.Domain.Entities;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class RecoveryEmailStepUpServiceTests
{
    [Theory]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("locked")]
    [InlineData("requiredChange")]
    [InlineData("person")]
    [InlineData("directoryBarrier")]
    [InlineData("migration")]
    public async Task ChangedEligibility_RejectsFreshGrant(string state)
    {
        await using var f = await RecoveryEmailPreferenceServiceTests.Fixture.CreateAsync();
        var context = await f.GrantAsync();
        switch (state)
        {
            case "inactive": f.User.IsActive = false; break;
            case "deleted": f.User.IsDeleted = true; break;
            case "locked": f.User.LockoutEnabled = true; f.User.LockoutEnd = f.Time.Now.AddMinutes(10); break;
            case "requiredChange": f.User.RequiresPasswordChange = true; break;
            case "person":
                var person = new Person { Id = Guid.NewGuid(), Status = Core.Domain.Enums.PersonStatus.Suspended };
                f.Db.Persons.Add(person); f.User.PersonId = person.Id; break;
            case "directoryBarrier":
                f.Db.NativeDirectoryRecoveryAttempts.Add(new NativeDirectoryRecoveryAttempt(f.User.Id, Guid.NewGuid(), NativeDirectoryCredentialOperationKind.RequiredChange, f.Time.Now)); break;
            case "migration":
                var binding = new ProviderSubjectDirectoryBinding(f.User.Id, "p", "s", Guid.NewGuid(), f.Time.Now.UtcDateTime);
                f.Db.ProviderSubjectDirectoryBindings.Add(binding);
                f.Db.CredentialMigrationStateRecords.Add(new CredentialMigrationStateRecord(f.User.Id, binding.Id, f.Time.Now)); break;
        }
        await f.Db.SaveChangesAsync();
        Assert.Null(await f.StepUp(f.Db).ValidateAsync(f.User.Id, context, false));
    }

    [Fact]
    public async Task Completion_WrongAuthorityOrStaleTimestampCannotIssue()
    {
        await using var f = await RecoveryEmailPreferenceServiceTests.Fixture.CreateAsync();
        var service = f.StepUp(f.Db); var state = await service.ResolveAsync(f.User.Id); Assert.NotNull(state);
        Assert.Null(await service.IssueAsync(f.User.Id, "stamp", "wrong", "browser", "csrf", f.Time.Now));
        Assert.Null(await service.IssueAsync(f.User.Id, "stamp", state.Binding, "browser", "csrf", f.Time.Now.AddMinutes(-6)));
        Assert.Null(await service.IssueAsync(f.User.Id, "stamp", state.Binding, "browser", "csrf", f.Time.Now.AddMinutes(1)));
    }
}
