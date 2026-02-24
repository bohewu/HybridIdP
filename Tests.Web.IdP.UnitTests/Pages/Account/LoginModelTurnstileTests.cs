using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Core.Application;
using Core.Application.Interfaces;
using Core.Application.Options;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Core.Domain.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using Web.IdP;
using Web.IdP.Options;
using Web.IdP.Pages.Account;

namespace Tests.Web.IdP.UnitTests.Pages.Account;

public class LoginModelTurnstileTests
{
    [Fact]
    public async Task LoadTurnstileStateAsync_GlobalOnAndClientOn_ShouldEnableTurnstile()
    {
        var application = new object();
        var properties = ImmutableDictionary<string, JsonElement>.Empty
            .Add(AuthConstants.Properties.EnableTurnstile, JsonSerializer.SerializeToElement(true));

        var (model, settingsMock, applicationManagerMock, turnstileStateMock) = CreateModel();
        SetupTurnstileSettings(settingsMock, globalEnabled: true);
        turnstileStateMock.SetupGet(x => x.IsAvailable).Returns(true);
        applicationManagerMock.Setup(x => x.FindByClientIdAsync("client-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);
        applicationManagerMock.Setup(x => x.GetPropertiesAsync(application, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ImmutableDictionary<string, JsonElement>>(properties));

        await InvokeLoadTurnstileStateAsync(model, "/connect/authorize?client_id=client-a&response_type=code");

        Assert.True(model.TurnstileEnabled);
    }

    [Fact]
    public async Task LoadTurnstileStateAsync_GlobalOnAndClientUnset_ShouldDisableTurnstile()
    {
        var application = new object();
        var properties = ImmutableDictionary<string, JsonElement>.Empty;

        var (model, settingsMock, applicationManagerMock, turnstileStateMock) = CreateModel();
        SetupTurnstileSettings(settingsMock, globalEnabled: true);
        turnstileStateMock.SetupGet(x => x.IsAvailable).Returns(true);
        applicationManagerMock.Setup(x => x.FindByClientIdAsync("client-b", It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);
        applicationManagerMock.Setup(x => x.GetPropertiesAsync(application, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ImmutableDictionary<string, JsonElement>>(properties));

        await InvokeLoadTurnstileStateAsync(model, "/connect/authorize?client_id=client-b&response_type=code");

        Assert.False(model.TurnstileEnabled);
    }

    [Fact]
    public async Task LoadTurnstileStateAsync_GlobalOffAndClientOn_ShouldDisableTurnstile()
    {
        var application = new object();
        var properties = ImmutableDictionary<string, JsonElement>.Empty
            .Add(AuthConstants.Properties.EnableTurnstile, JsonSerializer.SerializeToElement(true));

        var (model, settingsMock, applicationManagerMock, turnstileStateMock) = CreateModel();
        SetupTurnstileSettings(settingsMock, globalEnabled: false);
        turnstileStateMock.SetupGet(x => x.IsAvailable).Returns(true);
        applicationManagerMock.Setup(x => x.FindByClientIdAsync("client-c", It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);
        applicationManagerMock.Setup(x => x.GetPropertiesAsync(application, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ImmutableDictionary<string, JsonElement>>(properties));

        await InvokeLoadTurnstileStateAsync(model, "/connect/authorize?client_id=client-c&response_type=code");

        Assert.False(model.TurnstileEnabled);
    }

    private static async Task InvokeLoadTurnstileStateAsync(LoginModel model, string? returnUrl)
    {
        var method = typeof(LoginModel).GetMethod("LoadTurnstileStateAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task)method.Invoke(model, new object?[] { returnUrl })!;
        await task;
    }

    private static void SetupTurnstileSettings(Mock<ISettingsService> settingsMock, bool globalEnabled)
    {
        settingsMock.Setup(x => x.GetValueAsync<bool?>(SettingKeys.Turnstile.Enabled, It.IsAny<CancellationToken>()))
            .ReturnsAsync(globalEnabled);
        settingsMock.Setup(x => x.GetValueAsync<string?>(SettingKeys.Turnstile.SiteKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync("site-key");
        settingsMock.Setup(x => x.GetValueAsync<string?>(SettingKeys.Turnstile.SecretKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync("secret-key");
    }

    private static (LoginModel model, Mock<ISettingsService> settingsMock, Mock<IOpenIddictApplicationManager> applicationManagerMock, Mock<ITurnstileStateService> turnstileStateMock) CreateModel()
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

        var settingsMock = new Mock<ISettingsService>();
        var applicationManagerMock = new Mock<IOpenIddictApplicationManager>();
        var turnstileStateMock = new Mock<ITurnstileStateService>();

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
            Mock.Of<ILocalizationService>(),
            Microsoft.Extensions.Options.Options.Create(new LoginNoticesOptions()),
            turnstileStateMock.Object,
            settingsMock.Object,
            Mock.Of<IPasskeyService>(),
            Mock.Of<IUserManagementService>(),
            applicationManagerMock.Object);

        var httpContext = new DefaultHttpContext();
        model.PageContext = new PageContext { HttpContext = httpContext };

        var urlHelperMock = new Mock<IUrlHelper>();
        urlHelperMock
            .Setup(helper => helper.IsLocalUrl(It.IsAny<string>()))
            .Returns((string url) =>
                !string.IsNullOrWhiteSpace(url) &&
                ((url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))) ||
                 (url[0] == '~' && url.Length > 1 && url[1] == '/')));
        model.Url = urlHelperMock.Object;

        return (model, settingsMock, applicationManagerMock, turnstileStateMock);
    }
}
