using System.Security.Claims;
using System.Text.Json;
using Core.Application.Ports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Tests.Web.IdP.UnitTests.TestSupport;
using Moq;
using Web.IdP.Controllers.Account;

namespace Tests.Web.IdP.UnitTests.Controllers;

public sealed class RecoveryEmailControllerTests
{
    [Theory]
    [InlineData("UseDefault")]
    [InlineData("Disabled")]
    [InlineData("Legacy")]
    public async Task GetStatus_ReadOnlySelectionReturnsCurrentShapeWithoutLegacyLookup(string mode)
    {
        var id = Guid.NewGuid();
        var legacy = new Mock<IRecoveryEmailService>(MockBehavior.Strict);
        var authorizer = new Mock<IRecoveryProofAuthorizer>(MockBehavior.Strict);
        var preferences = new Mock<IRecoveryEmailPreferenceService>(MockBehavior.Strict);
        var status = new RecoveryPreferenceStatus(false, mode, null, null, "p***@example.test", null, null);
        preferences.Setup(p => p.GetStatusAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(status);
        var controller = CreateController(id, legacy.Object, authorizer.Object, preferences.Object);

        var response = Assert.IsType<OkObjectResult>(await controller.GetStatus(default));

        Assert.Same(status, response.Value);
        legacy.VerifyNoOtherCalls();
        authorizer.VerifyNoOtherCalls();
        preferences.Verify(p => p.GetStatusAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetStatus_NoSelectionStatePreservesLegacyShapeAndAuthorization(bool authorized)
    {
        var id = Guid.NewGuid();
        var legacy = new Mock<IRecoveryEmailService>(MockBehavior.Strict);
        var authorizer = new Mock<IRecoveryProofAuthorizer>(MockBehavior.Strict);
        authorizer.Setup(a => a.IsSelfServiceAuthorizedAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(authorized);
        if (authorized) legacy.Setup(s => s.GetStatusAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecoveryEmailStatus(true, true, "o***@example.test"));
        var preferences = new Mock<IRecoveryEmailPreferenceService>(MockBehavior.Strict);
        preferences.Setup(p => p.GetStatusAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecoveryPreferenceStatus(false, "unavailable", null, null, null, null, null));
        var controller = CreateController(id, legacy.Object, authorizer.Object, preferences.Object);

        var response = await controller.GetStatus(default);

        if (authorized)
            Assert.Equal(new RecoveryEmailStatusResponse(true, true, "o***@example.test"), Assert.IsType<OkObjectResult>(response).Value);
        else
        {
            Assert.Equal(403, Assert.IsType<ObjectResult>(response).StatusCode);
            legacy.VerifyNoOtherCalls();
        }
        authorizer.Verify(a => a.IsSelfServiceAuthorizedAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true, "POST", false)]
    [InlineData(true, "GET", true)]
    [InlineData(false, "POST", true)]
    public async Task RecoveryCsrf_PreservesReadAndDisabledCompatibility(bool enabled, string method, bool expectedNext)
    {
        var antiforgery = new Mock<IAntiforgery>();
        antiforgery.Setup(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()))
            .ThrowsAsync(new AntiforgeryValidationException("synthetic"));
        using var provider = new ServiceCollection().AddSingleton(antiforgery.Object)
            .AddSingleton<Microsoft.Extensions.Options.IOptions<Infrastructure.Options.RecoveryEmailSelectionOptions>>(
                Microsoft.Extensions.Options.Options.Create(new Infrastructure.Options.RecoveryEmailSelectionOptions { Enabled = enabled, SelfServiceEnabled = true }))
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity([], enabled ? "Identity.Application" : "Bearer")) };
        http.Request.Method = method;
        http.Request.Headers.Authorization = "Bearer synthetic";
        var attribute = typeof(RecoveryEmailController).GetCustomAttribute<global::Web.IdP.Attributes.RecoveryEmailCsrfAttribute>();
        Assert.NotNull(attribute);
        var action = new ActionContext(http, new(), new(), new());
        var context = new ActionExecutingContext(action, [], new Dictionary<string, object?>(), new object());
        var called = false;
        await attribute.OnActionExecutionAsync(context, () => { called = true; return Task.FromResult(new ActionExecutedContext(action, [], new object())); });
        Assert.Equal(expectedNext, called);
        if (!expectedNext) Assert.IsType<BadRequestObjectResult>(context.Result);
        antiforgery.Verify(a => a.ValidateRequestAsync(http), expectedNext ? Times.Never() : Times.Once());
    }

    [Fact]
    public async Task PreferenceChange_UsesOnlyCurrentAccountAndServerSessionBinding()
    {
        var id = Guid.NewGuid();
        var preference = new Mock<IRecoveryEmailPreferenceService>();
        preference.SetupGet(p => p.Enabled).Returns(true);
        preference.Setup(p => p.BeginAsync(id, "candidate@example.test", It.IsAny<RecoveryPreferenceContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecoveryProofOutcome.Unauthorized);
        var controller = new RecoveryEmailController(Mock.Of<IRecoveryEmailService>(), Mock.Of<IRecoveryProofAuthorizer>(), preference.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            { Session = new MemorySession(), User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Identity.Application")) } }
        };
        var result = await controller.BeginChange(new("candidate@example.test"), default);
        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
        preference.Verify(p => p.BeginAsync(id, "candidate@example.test", It.Is<RecoveryPreferenceContext>(c => c.StepUpGrantId == Guid.Empty && c.ContextHash == ""), It.IsAny<CancellationToken>()), Times.Once);
    }

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
        IRecoveryProofAuthorizer authorizer,
        IRecoveryEmailPreferenceService? preferences = null)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, accountId.ToString())],
            "Identity.Application"));
        return new RecoveryEmailController(service, authorizer, preferences)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };
    }
}
