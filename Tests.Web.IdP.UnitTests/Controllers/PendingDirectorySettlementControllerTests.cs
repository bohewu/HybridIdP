using Core.Application.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Text.Json;
using Web.IdP.Controllers.Account;

namespace Tests.Web.IdP.UnitTests.Controllers;

public sealed class PendingDirectorySettlementControllerTests
{
    [Fact]
    public async Task ClaimThenVerify_UsesServerSessionContextAndDoesNotReturnCredential()
    {
        var preparationId = Guid.NewGuid();
        const string credential = "user-only-current-credential";
        NativeRecoveryContext? claimedContext = null;
        var service = new Mock<IDirectoryCredentialOperatorResolutionService>();
        service.Setup(candidate => candidate.ClaimAsync(
                It.IsAny<DirectorySettlementClaimRequest>(), It.IsAny<CancellationToken>()))
            .Callback((DirectorySettlementClaimRequest request, CancellationToken _) => claimedContext = request.Context)
            .ReturnsAsync(new DirectorySettlementClaimResult(
                DirectorySettlementVerificationOutcome.ChallengeIssued, preparationId));
        service.Setup(candidate => candidate.VerifyAndFinalizeAsync(
                It.IsAny<DirectorySettlementVerificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DirectorySettlementVerificationRequest request, CancellationToken _) =>
            {
                Assert.Equal(preparationId, request.PreparationId);
                Assert.Equal(claimedContext, request.Context);
                Assert.Equal(credential, request.CurrentCredential);
                return DirectorySettlementVerificationOutcome.Resolved;
            });
        var controller = CreateController(service.Object);

        var claim = await controller.ClaimAsync(new("opaque-continuation"));
        var verify = await controller.VerifyAsync(new("123456", credential));

        var claimed = Assert.IsType<OkObjectResult>(claim);
        var claimBody = JsonSerializer.SerializeToElement(claimed.Value);
        Assert.Equal("verificationRequired", claimBody.GetProperty("outcome").GetString());
        Assert.Equal(["outcome"], claimBody.EnumerateObject().Select(property => property.Name));
        var resolved = Assert.IsType<OkObjectResult>(verify);
        Assert.DoesNotContain(credential, System.Text.Json.JsonSerializer.Serialize(resolved.Value));
        Assert.NotNull(claimedContext);
        Assert.NotEqual("opaque-continuation", claimedContext!.ContextHash);
    }

    [Fact]
    public async Task UnavailableClaim_ClearsPriorSessionContextAndReturnsOnlySanitizedOutcome()
    {
        var service = new Mock<IDirectoryCredentialOperatorResolutionService>();
        service.SetupSequence(candidate => candidate.ClaimAsync(
                It.IsAny<DirectorySettlementClaimRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectorySettlementClaimResult(
                DirectorySettlementVerificationOutcome.ChallengeIssued, Guid.NewGuid()))
            .ReturnsAsync(new DirectorySettlementClaimResult(
                DirectorySettlementVerificationOutcome.Unavailable));
        var controller = CreateController(service.Object);
        await controller.ClaimAsync(new("first-continuation"));

        var result = await controller.ClaimAsync(new("invalid-or-expired"));

        var unavailable = Assert.IsType<BadRequestObjectResult>(result);
        var body = JsonSerializer.SerializeToElement(unavailable.Value);
        Assert.Equal("unavailable", body.GetProperty("outcome").GetString());
        Assert.Equal(["outcome"], body.EnumerateObject().Select(property => property.Name));
        Assert.Empty(controller.HttpContext.Session.Keys);
    }

    [Fact]
    public void AnonymousMutations_UseStandardAntiforgery()
    {
        var controllerType = typeof(PendingDirectorySettlementController);
        Assert.NotNull(controllerType.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).SingleOrDefault());
        foreach (var methodName in new[]
                 {
                     nameof(PendingDirectorySettlementController.ClaimAsync),
                     nameof(PendingDirectorySettlementController.VerifyAsync),
                     nameof(PendingDirectorySettlementController.CancelAsync)
                 })
        {
            var method = controllerType.GetMethod(methodName)!;
            Assert.NotNull(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true).SingleOrDefault());
        }
    }

    [Fact]
    public async Task CancelAsync_UsesBoundSessionAndClearsItOnSuccess()
    {
        var preparationId = Guid.NewGuid();
        var service = new Mock<IDirectoryCredentialOperatorResolutionService>();
        service.Setup(candidate => candidate.ClaimAsync(
                It.IsAny<DirectorySettlementClaimRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectorySettlementClaimResult(
                DirectorySettlementVerificationOutcome.ChallengeIssued, preparationId));
        service.Setup(candidate => candidate.CancelUserAsync(preparationId,
                It.IsAny<NativeRecoveryContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DirectorySettlementVerificationOutcome.Resolved);
        var controller = CreateController(service.Object);
        await controller.ClaimAsync(new("opaque-continuation"));

        var result = await controller.CancelAsync();

        Assert.IsType<OkObjectResult>(result);
        service.VerifyAll();
    }

    private static PendingDirectorySettlementController CreateController(
        IDirectoryCredentialOperatorResolutionService service)
    {
        var context = new DefaultHttpContext { Session = new TestSession() };
        return new PendingDirectorySettlementController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);
        public bool IsAvailable => true;
        public string Id { get; } = Guid.NewGuid().ToString();
        public IEnumerable<string> Keys => _values.Keys;
        public void Clear() => _values.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _values.Remove(key);
        public void Set(string key, byte[] value) => _values[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _values.TryGetValue(key, out value!);
    }
}
