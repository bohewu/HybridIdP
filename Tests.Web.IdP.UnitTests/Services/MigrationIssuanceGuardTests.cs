using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Identity;
using Moq;
using Web.IdP.Services;

namespace Tests.Web.IdP.UnitTests.Services;

public sealed class MigrationIssuanceGuardTests
{
    [Fact]
    public async Task CanIssueAsync_WhenMigrationIsDisabledAndNoRecordExists_PreservesBaseline()
    {
        var accountId = Guid.NewGuid();
        var stateStore = new Mock<ICredentialMigrationStateStore>();
        stateStore
            .Setup(store => store.FindAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CredentialMigrationRecord?)null);
        var guard = CreateGuard(stateStore, enabled: false);

        var allowed = await guard.CanIssueAsync(accountId);

        Assert.True(allowed);
        stateStore.Verify(store => store.FindAsync(accountId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CanIssueAsync_WhenStateIsLocalFinalized_AllowsIssuance()
    {
        var accountId = Guid.NewGuid();
        var stateStore = new Mock<ICredentialMigrationStateStore>();
        stateStore
            .Setup(store => store.FindAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRecord(accountId, CredentialMigrationState.LocalFinalized));
        var guard = CreateGuard(stateStore, enabled: true);

        var allowed = await guard.CanIssueAsync(accountId);

        Assert.True(allowed);
    }

    [Fact]
    public async Task CanIssueAsync_WhenMigrationIsDisabledAndStateIsLocalFinalized_AllowsIssuance()
    {
        var accountId = Guid.NewGuid();
        var stateStore = new Mock<ICredentialMigrationStateStore>();
        stateStore
            .Setup(store => store.FindAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRecord(accountId, CredentialMigrationState.LocalFinalized));
        var guard = CreateGuard(stateStore, enabled: false);

        var allowed = await guard.CanIssueAsync(accountId);

        Assert.True(allowed);
    }

    [Theory]
    [InlineData(CredentialMigrationState.Required)]
    [InlineData(CredentialMigrationState.ProofValidated)]
    [InlineData(CredentialMigrationState.DirectoryCredentialCommitted)]
    public async Task CanIssueAsync_WhenStateIsIncomplete_DeniesIssuance(CredentialMigrationState state)
    {
        var accountId = Guid.NewGuid();
        var stateStore = new Mock<ICredentialMigrationStateStore>();
        stateStore
            .Setup(store => store.FindAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRecord(accountId, state));
        var guard = CreateGuard(stateStore, enabled: true);

        var allowed = await guard.CanIssueAsync(accountId);

        Assert.False(allowed);
    }

    [Fact]
    public async Task CanIssueAsync_WhenMigrationIsDisabledAndStateIsIncomplete_DeniesIssuance()
    {
        var accountId = Guid.NewGuid();
        var stateStore = new Mock<ICredentialMigrationStateStore>();
        stateStore
            .Setup(store => store.FindAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRecord(accountId, CredentialMigrationState.Required));
        var guard = CreateGuard(stateStore, enabled: false);

        var allowed = await guard.CanIssueAsync(accountId);

        Assert.False(allowed);
    }

    [Fact]
    public async Task CanIssueAsync_WhenStateCannotBeRead_DeniesIssuance()
    {
        var accountId = Guid.NewGuid();
        var stateStore = new Mock<ICredentialMigrationStateStore>();
        stateStore
            .Setup(store => store.FindAsync(accountId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        var guard = CreateGuard(stateStore, enabled: true);

        var allowed = await guard.CanIssueAsync(accountId);

        Assert.False(allowed);
    }

    [Fact]
    public async Task CanIssueAsync_WhenDirectoryRecoveryBarrierExists_DeniesBeforeCompletedMigration()
    {
        var accountId = Guid.NewGuid();
        var stateStore = new Mock<ICredentialMigrationStateStore>();
        stateStore.Setup(store => store.FindAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRecord(accountId, CredentialMigrationState.LocalFinalized));
        var barrier = new Mock<INativeDirectoryRecoveryBarrier>();
        barrier.Setup(candidate => candidate.HasIssuanceBarrierAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var guard = new MigrationIssuanceGuard(
            stateStore.Object,
            barrier.Object,
            CreateUserManager(accountId).Object,
            Microsoft.Extensions.Options.Options.Create(new CredentialMigrationOptions { Enabled = true }));

        Assert.False(await guard.CanIssueAsync(accountId));
        stateStore.Verify(
            store => store.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CanIssueAsync_WhenPasswordChangeIsRequired_DeniesBeforeOtherIssuanceState()
    {
        var accountId = Guid.NewGuid();
        var stateStore = new Mock<ICredentialMigrationStateStore>();
        var userManager = CreateUserManager(accountId, requiresPasswordChange: true);
        var barrier = new Mock<INativeDirectoryRecoveryBarrier>();
        var guard = new MigrationIssuanceGuard(
            stateStore.Object,
            barrier.Object,
            userManager.Object,
            Microsoft.Extensions.Options.Options.Create(new CredentialMigrationOptions()));

        Assert.False(await guard.CanIssueAsync(accountId));
        barrier.VerifyNoOtherCalls();
        stateStore.VerifyNoOtherCalls();
    }

    private static MigrationIssuanceGuard CreateGuard(
        Mock<ICredentialMigrationStateStore> stateStore,
        bool enabled)
    {
        var barrier = new Mock<INativeDirectoryRecoveryBarrier>();
        barrier.Setup(candidate => candidate.HasIssuanceBarrierAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        return new(
            stateStore.Object,
            barrier.Object,
            CreateUserManager().Object,
            Microsoft.Extensions.Options.Options.Create(new CredentialMigrationOptions { Enabled = enabled }));
    }

    private static Mock<UserManager<ApplicationUser>> CreateUserManager(
        Guid? accountId = null,
        bool requiresPasswordChange = false)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var manager = new Mock<UserManager<ApplicationUser>>(store.Object, null, null, null, null, null, null, null, null);
        manager.Setup(candidate => candidate.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => new ApplicationUser
            {
                Id = accountId ?? Guid.Parse(id),
                RequiresPasswordChange = requiresPasswordChange
            });
        return manager;
    }

    private static CredentialMigrationRecord CreateRecord(
        Guid accountId,
        CredentialMigrationState state) =>
        new(
            accountId,
            new DirectoryObjectBinding("test", "subject", Guid.NewGuid()),
            state);
}
