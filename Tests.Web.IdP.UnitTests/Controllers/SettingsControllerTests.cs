using System.Security.Claims;
using System.Text.Json;
using Core.Application;
using Core.Application.Options;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Web.IdP.Controllers.Admin;

namespace Tests.Web.IdP.UnitTests.Controllers;

public class SettingsControllerTests
{
    private readonly Mock<ISettingsService> _settings = new();
    private readonly SettingsController _controller;

    public SettingsControllerTests()
    {
        var emailOptions = new Mock<IOptionsSnapshot<EmailOptions>>();
        emailOptions.Setup(options => options.Value).Returns(new EmailOptions());

        _controller = new SettingsController(
            _settings.Object,
            Mock.Of<IEmailService>(),
            new ConfigurationBuilder().Build(),
            emailOptions.Object);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.Name, "TestUser") },
                    "test"))
            }
        };
    }

    [Fact]
    public async Task GetByKey_SystemOwnedSetting_ReturnsExistingNonSensitiveValue()
    {
        _settings
            .Setup(service => service.GetValueAsync(
                SettingKeys.OperationalAdminBootstrapCompleted,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("completed");

        var result = await _controller.GetByKey(
            SettingKeys.OperationalAdminBootstrapCompleted);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var payload = JsonSerializer.SerializeToElement(okResult.Value);
        Assert.Equal(
            SettingKeys.OperationalAdminBootstrapCompleted,
            payload.GetProperty("key").GetString());
        Assert.Equal("completed", payload.GetProperty("value").GetString());
    }

    [Fact]
    public async Task GetByPrefix_SystemOwnedSetting_RemainsVisibleAsNonSensitiveValue()
    {
        _settings
            .Setup(service => service.GetByPrefixAsync(
                "system.",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                [SettingKeys.OperationalAdminBootstrapCompleted] = "completed"
            });

        var result = await _controller.GetByPrefix("system.");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var payload = JsonSerializer.SerializeToElement(okResult.Value);
        var setting = Assert.Single(payload.EnumerateArray());
        Assert.Equal(
            SettingKeys.OperationalAdminBootstrapCompleted,
            setting.GetProperty("key").GetString());
        Assert.Equal("completed", setting.GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("tampered")]
    [InlineData("")]
    public async Task UpdateSetting_SystemOwnedKey_ReturnsBadRequestWithoutWriting(
        string requestedValue)
    {
        var result = await _controller.UpdateSetting(
            SettingKeys.OperationalAdminBootstrapCompleted,
            new UpdateSettingRequest(requestedValue));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var payload = JsonSerializer.SerializeToElement(badRequest.Value);
        Assert.Equal(
            "System-managed settings cannot be modified",
            payload.GetProperty("error").GetString());
        _settings.Verify(
            service => service.SetValueAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateSetting_ProviderEquivalentSystemOwnedKey_ReturnsBadRequest()
    {
        var providerEquivalentKey =
            SettingKeys.OperationalAdminBootstrapCompleted.ToUpperInvariant();
        _settings
            .Setup(service => service.SetValueAsync(
                providerEquivalentKey,
                "tampered",
                "TestUser",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SystemManagedSettingException());

        var result = await _controller.UpdateSetting(
            providerEquivalentKey,
            new UpdateSettingRequest("tampered"));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var payload = JsonSerializer.SerializeToElement(badRequest.Value);
        Assert.Equal(
            "System-managed settings cannot be modified",
            payload.GetProperty("error").GetString());
        _settings.Verify(
            service => service.SetValueAsync(
                providerEquivalentKey,
                "tampered",
                "TestUser",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateSetting_UnrelatedInvalidOperationException_IsNotTranslated()
    {
        var serviceFailure = new InvalidOperationException("Unrelated settings failure");
        _settings
            .Setup(service => service.SetValueAsync(
                SettingKeys.Branding.AppName,
                "HybridAuth",
                "TestUser",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(serviceFailure);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _controller.UpdateSetting(
                SettingKeys.Branding.AppName,
                new UpdateSettingRequest("HybridAuth")));

        Assert.Same(serviceFailure, thrown);
    }

    [Fact]
    public async Task UpdateSetting_OrdinaryKey_WritesValue()
    {
        var result = await _controller.UpdateSetting(
            SettingKeys.Branding.AppName,
            new UpdateSettingRequest("HybridAuth"));

        Assert.IsType<OkObjectResult>(result);
        _settings.Verify(
            service => service.SetValueAsync(
                SettingKeys.Branding.AppName,
                "HybridAuth",
                "TestUser",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
