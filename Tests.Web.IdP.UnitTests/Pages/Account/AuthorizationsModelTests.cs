using System.Collections.Immutable;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Moq;
using OpenIddict.Abstractions;
using Web.IdP.Pages.Account;

namespace Tests.Web.IdP.UnitTests.Pages.Account;

public sealed class AuthorizationsModelTests
{
    private const string ApplicationId = "00112233-4455-6677-8899-aabbccddeeff";
    private readonly Mock<IOpenIddictAuthorizationManager> _authorizations = new();
    private readonly Mock<ILogger<AuthorizationsModel>> _logger = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-id")]
    [InlineData("\r\n" + ApplicationId + "\n")]
    [InlineData("\t" + ApplicationId)]
    [InlineData(ApplicationId + "\u2028")]
    [InlineData(ApplicationId + "\u2029")]
    public async Task Revoke_ShouldRejectInvalidOrControlBearingIdentifiers_BeforeLookupOrLogging(string? applicationId)
    {
        var model = CreateModel();

        Assert.IsType<BadRequestResult>(await model.OnPostRevokeAsync(applicationId!));

        _authorizations.VerifyNoOtherCalls();
        _logger.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(ApplicationId)]
    [InlineData("00112233445566778899AABBCCDDEEFF")]
    [InlineData("{00112233-4455-6677-8899-AABBCCDDEEFF}")]
    [InlineData(" " + ApplicationId + " ")]
    public async Task Revoke_ShouldPreserveCurrentUserScope_AndCanonicalizeLookupAndLog(string suppliedId)
    {
        var model = CreateModel();

        Assert.IsType<JsonResult>(await model.OnPostRevokeAsync(suppliedId));

        _authorizations.Verify(a => a.FindAsync("current-user", ApplicationId,
            OpenIddictConstants.Statuses.Valid, OpenIddictConstants.AuthorizationTypes.Permanent,
            It.IsAny<ImmutableArray<string>?>(), It.IsAny<CancellationToken>()), Times.Once);
        var log = Assert.Single(_logger.Invocations);
        var properties = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(log.Arguments[2]);
        Assert.Equal(ApplicationId, properties.Single(p => p.Key == "ApplicationId").Value?.ToString());
        Assert.DoesNotContain('\r', log.Arguments[2].ToString()!);
        Assert.DoesNotContain('\n', log.Arguments[2].ToString()!);
    }

    private AuthorizationsModel CreateModel()
    {
        _authorizations.Setup(a => a.FindAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ImmutableArray<string>?>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyAuthorizations());
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "current-user")], "Identity.Application"))
        };
        return new AuthorizationsModel(_logger.Object, Mock.Of<IOpenIddictApplicationManager>(), _authorizations.Object)
        {
            PageContext = new PageContext { HttpContext = context }
        };
    }

    private static async IAsyncEnumerable<object> EmptyAuthorizations()
    {
        await Task.CompletedTask;
        yield break;
    }
}
