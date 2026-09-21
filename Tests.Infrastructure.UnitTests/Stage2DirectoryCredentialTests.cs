using Core.Application.Ports;
using Infrastructure.Directory;
using Infrastructure.Options;
using Microsoft.Extensions.Options;
using System.DirectoryServices.Protocols;
using System.Text;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class Stage2DirectoryCredentialTests
{
    [Fact]
    public async Task ResetCredentialAsync_Success_InvokesOneProtectedWriteWithoutRetry()
    {
        var transport = new RecordingTransport
        {
            ResetResult = new ProtectedDirectoryCredentialOperationTransportResult(
                ProtectedDirectoryCredentialOperationTransportOutcome.Succeeded)
        };
        var service = CreateService(transport);

        var result = await service.ResetCredentialAsync(Guid.NewGuid(), "new-password");

        Assert.Equal(DirectoryCredentialOperationOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, transport.ResetCalls);
        Assert.Equal(DirectoryTransport.StartTls, transport.Transport);
    }

    [Fact]
    public async Task VerifyCredentialAsync_IndependentTransportIdentity_ReturnsImmutableStatusEvidence()
    {
        var objectId = Guid.NewGuid();
        var identity = new ManagedDirectoryIdentity(objectId, "account", true, true, false);
        var transport = new RecordingTransport
        {
            VerifyResult = new ProtectedDirectoryCredentialTransportResult(
                ProtectedDirectoryCredentialTransportOutcome.Authenticated,
                identity)
        };
        var service = CreateService(transport);

        var result = await service.VerifyCredentialAsync(objectId, "new-password");

        Assert.Equal(DirectoryCredentialOutcome.Authenticated, result.Outcome);
        Assert.Same(identity, result.Identity);
        Assert.Equal(1, transport.VerifyCalls);
    }

    [Fact]
    public async Task AuthenticateAsync_DisabledIntegration_ContactsNoTransport()
    {
        var transport = new RecordingTransport();
        var service = new ProtectedDirectoryCredentialService(
            transport,
            Options.Create(new DirectoryIntegrationOptions { Enabled = false }),
            Options.Create(new DirectoryLookupOptions { Timeout = TimeSpan.FromSeconds(1) }));

        var result = await service.AuthenticateAsync(Guid.NewGuid(), "password");

        Assert.Equal(DirectoryCredentialOutcome.Unavailable, result.Outcome);
        Assert.Equal(0, transport.AuthenticationCalls);
    }

    [Fact]
    public async Task AuthenticateAsync_InFlightCancellation_AbortsOperationWithoutRetry()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transportCancellationObserved = false;
        var transport = new RecordingTransport
        {
            AuthenticateOperation = async cancellationToken =>
            {
                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return ProtectedDirectoryCredentialTransportResult.Unavailable();
                }
                finally
                {
                    transportCancellationObserved = cancellationToken.IsCancellationRequested;
                }
            }
        };
        var service = CreateService(transport);
        using var cancellation = new CancellationTokenSource();

        var operation = service.AuthenticateAsync(Guid.NewGuid(), "password", cancellation.Token);
        await started.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.True(transportCancellationObserved);
        Assert.Equal(1, transport.AuthenticationCalls);
    }

    [Fact]
    public void TemporaryCredentialRequests_UseSingleAtomicModifyWithRequiredOperations()
    {
        var objectId = Guid.NewGuid();
        var issue = LdapProtectedDirectoryTransport.CreateTemporaryCredentialRequest(
            objectId,
            "temporary-value");
        var change = LdapProtectedDirectoryTransport.CreateRequiredCredentialChangeRequest(
            objectId,
            "temporary-value",
            "replacement-value");

        Assert.Equal($"<GUID={objectId:D}>", issue.DistinguishedName);
        Assert.Equal($"<GUID={objectId:D}>", change.DistinguishedName);
        Assert.Collection(
            issue.Modifications.Cast<DirectoryAttributeModification>(),
            modification => AssertPasswordModification(
                modification, DirectoryAttributeOperation.Replace, "temporary-value"),
            modification =>
            {
                Assert.Equal("pwdLastSet", modification.Name);
                Assert.Equal(DirectoryAttributeOperation.Replace, modification.Operation);
                Assert.Equal("0", modification[0]);
            });
        Assert.Collection(
            change.Modifications.Cast<DirectoryAttributeModification>(),
            modification => AssertPasswordModification(
                modification, DirectoryAttributeOperation.Delete, "temporary-value"),
            modification => AssertPasswordModification(
                modification, DirectoryAttributeOperation.Add, "replacement-value"));
    }

    [Fact]
    public async Task TemporaryCredentialCapability_DefaultOffContactsNoTransport()
    {
        var transport = new RecordingTransport();
        var service = CreateService(transport);

        var result = await service.IssueTemporaryCredentialAsync(Guid.NewGuid(), "temporary-value");

        Assert.Equal(DirectoryCredentialOperationOutcome.Unsupported, result.Outcome);
        Assert.Equal(0, transport.TemporaryIssueCalls);
    }

    [Fact]
    public async Task TemporaryCredentialCapability_EnabledDelegatesEachOperationOnce()
    {
        var transport = new RecordingTransport
        {
            TemporaryResult = new(ProtectedDirectoryCredentialOperationTransportOutcome.Succeeded),
            ChangeRequiredResult = new(ProtectedDirectoryCredentialOperationTransportOutcome.Succeeded)
        };
        var service = CreateService(transport, temporaryCapabilityEnabled: true);
        var objectId = Guid.NewGuid();

        Assert.Equal(
            DirectoryCredentialOperationOutcome.Succeeded,
            (await service.IssueTemporaryCredentialAsync(objectId, "temporary-value")).Outcome);
        Assert.Equal(
            DirectoryCredentialOperationOutcome.Succeeded,
            (await service.ChangeRequiredCredentialAsync(objectId, "temporary-value", "replacement-value")).Outcome);
        Assert.Equal(1, transport.TemporaryIssueCalls);
        Assert.Equal(1, transport.ChangeRequiredCalls);
        Assert.Equal(DirectoryTransport.StartTls, transport.Transport);
    }

    [Theory]
    [InlineData("80090308: LdapErr: comment, data 773, v4563", true)]
    [InlineData("80090308: LdapErr: comment, data 52e, v4563", false)]
    [InlineData("unrelated 1773 value", false)]
    public void PasswordChangeRequiredClassifier_RequiresExactDirectoryDataCode(
        string message,
        bool expected)
    {
        Assert.Equal(expected, LdapProtectedDirectoryTransport.IsPasswordChangeRequired(message));
    }

    private static void AssertPasswordModification(
        DirectoryAttributeModification modification,
        DirectoryAttributeOperation operation,
        string password)
    {
        Assert.Equal("unicodePwd", modification.Name);
        Assert.Equal(operation, modification.Operation);
        Assert.Equal($"\"{password}\"", Encoding.Unicode.GetString(Assert.IsType<byte[]>(modification[0])));
    }

    private static ProtectedDirectoryCredentialService CreateService(
        RecordingTransport transport,
        bool temporaryCapabilityEnabled = false) =>
        new(
            transport,
            Options.Create(new DirectoryIntegrationOptions
            {
                Enabled = true,
                AuthenticationEnabled = true,
                TemporaryCredentialCapabilityEnabled = temporaryCapabilityEnabled,
                Transport = DirectoryTransport.StartTls
            }),
            Options.Create(new DirectoryLookupOptions { Timeout = TimeSpan.FromSeconds(1) }));

    private sealed class RecordingTransport : IProtectedDirectoryCredentialTransport
    {
        public int AuthenticationCalls { get; private set; }
        public int ResetCalls { get; private set; }
        public int VerifyCalls { get; private set; }
        public int TemporaryIssueCalls { get; private set; }
        public int ChangeRequiredCalls { get; private set; }
        public DirectoryTransport? Transport { get; private set; }
        public Func<CancellationToken, Task<ProtectedDirectoryCredentialTransportResult>>? AuthenticateOperation { get; init; }
        public ProtectedDirectoryCredentialTransportResult VerifyResult { get; set; } =
            ProtectedDirectoryCredentialTransportResult.Unavailable();
        public ProtectedDirectoryCredentialOperationTransportResult ResetResult { get; set; } =
            ProtectedDirectoryCredentialOperationTransportResult.Unavailable();
        public ProtectedDirectoryCredentialOperationTransportResult TemporaryResult { get; set; } =
            ProtectedDirectoryCredentialOperationTransportResult.Unavailable();
        public ProtectedDirectoryCredentialOperationTransportResult ChangeRequiredResult { get; set; } =
            ProtectedDirectoryCredentialOperationTransportResult.Unavailable();

        public Task<ProtectedDirectoryCredentialTransportResult> AuthenticateAsync(
            Guid directoryObjectId,
            string password,
            DirectoryTransport transport,
            CancellationToken cancellationToken = default)
        {
            AuthenticationCalls++;
            Transport = transport;
            if (AuthenticateOperation is not null)
            {
                return AuthenticateOperation(cancellationToken);
            }
            return Task.FromResult(ProtectedDirectoryCredentialTransportResult.Unavailable());
        }

        public Task<ProtectedDirectoryCredentialOperationTransportResult> ResetAsync(
            Guid directoryObjectId,
            string newPassword,
            DirectoryTransport transport,
            CancellationToken cancellationToken = default)
        {
            ResetCalls++;
            Transport = transport;
            return Task.FromResult(ResetResult);
        }

        public Task<ProtectedDirectoryCredentialTransportResult> VerifyAsync(
            Guid directoryObjectId,
            string password,
            DirectoryTransport transport,
            CancellationToken cancellationToken = default)
        {
            VerifyCalls++;
            Transport = transport;
            return Task.FromResult(VerifyResult);
        }

        public Task<ProtectedDirectoryCredentialOperationTransportResult> IssueTemporaryAsync(
            Guid directoryObjectId,
            string temporaryPassword,
            DirectoryTransport transport,
            CancellationToken cancellationToken = default)
        {
            TemporaryIssueCalls++;
            Transport = transport;
            return Task.FromResult(TemporaryResult);
        }

        public Task<ProtectedDirectoryCredentialOperationTransportResult> ChangeRequiredAsync(
            Guid directoryObjectId,
            string currentPassword,
            string newPassword,
            DirectoryTransport transport,
            CancellationToken cancellationToken = default)
        {
            ChangeRequiredCalls++;
            Transport = transport;
            return Task.FromResult(ChangeRequiredResult);
        }
    }
}
