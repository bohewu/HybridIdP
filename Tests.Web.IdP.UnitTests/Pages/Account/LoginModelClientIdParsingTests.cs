using System.Reflection;
using Core.Application;
using Core.Application.Interfaces;
using Core.Application.Options;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using Web.IdP;
using Web.IdP.Pages.Account;

namespace Tests.Web.IdP.UnitTests.Pages.Account;

public class LoginModelClientIdParsingTests
{
    private readonly LoginModel _model;
    private readonly MethodInfo _method;

    public LoginModelClientIdParsingTests()
    {
        _model = CreateModel();
        _method = typeof(LoginModel).GetMethod("TryGetClientIdFromReturnUrl", BindingFlags.Instance | BindingFlags.NonPublic)!;
    }

    [Fact]
    public void TryGetClientIdFromReturnUrl_LocalAuthorizeUrl_ReturnsClientId()
    {
        var clientId = InvokeTryGetClientIdFromReturnUrl("/connect/authorize?client_id=testclient-public&response_type=code");

        Assert.Equal("testclient-public", clientId);
    }

    [Fact]
    public void TryGetClientIdFromReturnUrl_LocalUrlWithoutClientId_ReturnsNull()
    {
        var clientId = InvokeTryGetClientIdFromReturnUrl("/connect/authorize?response_type=code");

        Assert.Null(clientId);
    }

    [Fact]
    public void TryGetClientIdFromReturnUrl_NonLocalUrl_ReturnsNull()
    {
        var clientId = InvokeTryGetClientIdFromReturnUrl("https://example.com/connect/authorize?client_id=testclient-public");

        Assert.Null(clientId);
    }

    [Fact]
    public void TryGetClientIdFromReturnUrl_LocalLoginUrlWithEncodedReturnUrl_DoesNotThrowAndReturnsNull()
    {
        string? result = null;
        var exception = Record.Exception(() =>
            result = InvokeTryGetClientIdFromReturnUrl("/Account/Login?ReturnUrl=%2Fconnect%2Fauthorize%3Fclient_id%3Dtestclient-public"));

        Assert.Null(exception);
        Assert.Null(result);
    }

    private string? InvokeTryGetClientIdFromReturnUrl(string? returnUrl)
    {
        return (string?)_method.Invoke(_model, new object?[] { returnUrl });
    }

    private static LoginModel CreateModel()
    {
        var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStoreMock.Object,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

        var contextAccessorMock = new Mock<IHttpContextAccessor>();
        var claimsFactoryMock = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
        var signInManagerMock = new Mock<SignInManager<ApplicationUser>>(
            userManagerMock.Object,
            contextAccessorMock.Object,
            claimsFactoryMock.Object,
            null,
            null,
            null,
            null);

        var model = new LoginModel(
            signInManagerMock.Object,
            userManagerMock.Object,
            Mock.Of<ILoginService>(),
            Mock.Of<ITurnstileService>(),
            Mock.Of<ILoginHistoryService>(),
            Mock.Of<INotificationService>(),
            Mock.Of<ISecurityPolicyService>(),
            Mock.Of<IDomainEventPublisher>(),
            Microsoft.Extensions.Options.Options.Create(new TurnstileOptions()),
            Mock.Of<ILogger<LoginModel>>(),
            Mock.Of<IStringLocalizer<SharedResource>>(),
            Mock.Of<ITurnstileStateService>(),
            Mock.Of<ISettingsService>(),
            Mock.Of<IPasskeyService>(),
            Mock.Of<IUserManagementService>(),
            Mock.Of<IOpenIddictApplicationManager>());

        var urlHelperMock = new Mock<IUrlHelper>();
        urlHelperMock
            .Setup(helper => helper.IsLocalUrl(It.IsAny<string>()))
            .Returns((string url) =>
                !string.IsNullOrWhiteSpace(url) &&
                ((url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))) ||
                 (url[0] == '~' && url.Length > 1 && url[1] == '/')));

        model.Url = urlHelperMock.Object;
        return model;
    }
}
