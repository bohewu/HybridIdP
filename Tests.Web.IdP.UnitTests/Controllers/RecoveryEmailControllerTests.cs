using System.Security.Claims;
using System.Text.Json;
using Core.Application.Ports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Web.IdP.Controllers.Account;

namespace Tests.Web.IdP.UnitTests.Controllers;

public sealed class RecoveryEmailControllerTests
{
    [Fact]
    public async Task GetStatus_HighAssuranceMissing_ReturnsLocalizedOutcomeCodeWithoutCallingService()
    {
        var accountId = Guid.NewGuid();
        var service = new Mock<IRecoveryEmailService>(MockBehavior.Strict);
        var authorizer = new Mock<IRecoveryProofAuthorizer>(MockBehavior.Strict);
        authorizer.Setup(candidate => candidate.IsSelfServiceAuthorizedAsync(
                accountId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var controller = CreateController(accountId, service.Object, authorizer.Object);

        var result = await controller.GetStatus(CancellationToken.None);

        var denied = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, denied.StatusCode);
        Assert.Contains("highAssuranceRequired", JsonSerializer.Serialize(denied.Value));
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BeginChange_UsesCurrentAccountAndReturnsSanitizedFailure()
    {
        var accountId = Guid.NewGuid();
        var service = new Mock<IRecoveryEmailService>(MockBehavior.Strict);
        service.Setup(candidate => candidate.BeginAuthenticatedChangeAsync(
                It.Is<RecoveryEmailChangeRequest>(request =>
                    request.LocalAccountId == accountId && request.CandidateAddress == "candidate@example.test"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecoveryEmailChangeResult(RecoveryProofOutcome.Invalid));
        var controller = CreateController(accountId, service.Object, Mock.Of<IRecoveryProofAuthorizer>());

        var result = await controller.BeginChange(
            new RecoveryEmailChangeApiRequest("candidate@example.test"),
            CancellationToken.None);

        var rejected = Assert.IsType<BadRequestObjectResult>(result);
        var response = JsonSerializer.Serialize(rejected.Value);
        Assert.Contains("invalid", response);
        Assert.DoesNotContain("candidate@example.test", response);
    }

    private static RecoveryEmailController CreateController(
        Guid accountId,
        IRecoveryEmailService service,
        IRecoveryProofAuthorizer authorizer)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, accountId.ToString())],
            "Identity.Application"));
        return new RecoveryEmailController(service, authorizer)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };
    }
}
