using System.Security.Claims;
using System.Security.Cryptography;
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
    private readonly Mock<IEmailService> _emailService = new();
    private readonly SettingsController _controller;

    public SettingsControllerTests()
    {
        var emailOptions = new Mock<IOptionsSnapshot<EmailOptions>>();
        emailOptions.Setup(options => options.Value).Returns(new EmailOptions());

        _controller = new SettingsController(
            _settings.Object,
            _emailService.Object,
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

    [Theory]
    [InlineData(SettingKeys.Email.SmtpPassword)]
    [InlineData(SettingKeys.Turnstile.SecretKey)]
    [InlineData("custom.lowercase.password")]
    [InlineData("custom.lowercase.secret")]
    public async Task GetByKey_SensitiveSetting_ReturnsOnlyMaskedPresenceMetadata(
        string key)
    {
        _settings
            .Setup(service => service.GetValueAsync(
                key,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("decrypted-sensitive-value");

        var result = await _controller.GetByKey(key);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var payload = JsonSerializer.SerializeToElement(okResult.Value);
        Assert.Equal(key, payload.GetProperty("key").GetString());
        Assert.True(
            string.Equals(
                payload.GetProperty("value").GetString(),
                "(set)",
                StringComparison.Ordinal),
            "Sensitive exact-key responses must expose only presence metadata.");
    }

    [Fact]
    public async Task GetByKey_EmptySensitiveSetting_PreservesUnsetState()
    {
        _settings
            .Setup(service => service.GetValueAsync(
                SettingKeys.Turnstile.SecretKey,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var result = await _controller.GetByKey(SettingKeys.Turnstile.SecretKey);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var payload = JsonSerializer.SerializeToElement(okResult.Value);
        Assert.Equal(string.Empty, payload.GetProperty("value").GetString());
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

    [Fact]
    public async Task GetByPrefix_ConfigurationBackedSmtpPassword_ReturnsOnlyMaskedPresenceMetadata()
    {
        const string smtpHost = "smtp.example.test";
        var configurationSensitiveValue = Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(32));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{EmailOptions.SectionName}:SmtpHost"] = smtpHost,
                [$"{EmailOptions.SectionName}:SmtpPassword"] = configurationSensitiveValue
            })
            .Build();
        var emailOptions = new Mock<IOptionsSnapshot<EmailOptions>>();
        emailOptions.Setup(options => options.Value).Returns(new EmailOptions());
        var controller = new SettingsController(
            _settings.Object,
            Mock.Of<IEmailService>(),
            configuration,
            emailOptions.Object);

        _settings
            .Setup(service => service.GetByPrefixAsync(
                "Mail.",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        var result = await controller.GetByPrefix("Mail.");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var payload = JsonSerializer.SerializeToElement(okResult.Value);
        var passwordSetting = payload.EnumerateArray().Single(setting =>
            setting.GetProperty("key").GetString() == SettingKeys.Email.SmtpPassword);
        Assert.True(
            string.Equals(
                passwordSetting.GetProperty("value").GetString(),
                "(set)",
                StringComparison.Ordinal),
            "The effective SMTP password must expose only presence metadata.");
        Assert.True(
            string.Equals(
                passwordSetting.GetProperty("defaultValue").GetString(),
                "(set)",
                StringComparison.Ordinal),
            "The configured SMTP password default must expose only presence metadata.");

        var hostSetting = payload.EnumerateArray().Single(setting =>
            setting.GetProperty("key").GetString() == SettingKeys.Email.SmtpHost);
        Assert.Equal(smtpHost, hostSetting.GetProperty("value").GetString());
        Assert.Equal(smtpHost, hostSetting.GetProperty("defaultValue").GetString());
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

    [Fact]
    public async Task TestEmail_WaitsForSmtpAcceptanceBeforeReturningSuccess()
    {
        var settings = new MailSettingsDto
        {
            Host = "smtp.example.test",
            Port = 25,
            FromAddress = "sender@example.test",
            FromName = "Test Sender"
        };
        var request = new TestMailSettingsRequest
        {
            Settings = settings,
            To = "recipient@example.test"
        };
        var smtpCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _emailService
            .Setup(service => service.SendTestEmailAsync(
                settings,
                request.To,
                It.IsAny<CancellationToken>()))
            .Returns(smtpCompletion.Task);

        var actionTask = _controller.TestEmail(request);

        Assert.False(actionTask.IsCompleted);
        smtpCompletion.SetResult();
        var result = await actionTask;

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task TestEmail_SmtpRejection_ReturnsBadGatewayWithSafeStatus()
    {
        var request = new TestMailSettingsRequest
        {
            Settings = new MailSettingsDto
            {
                Host = "smtp.example.test",
                Port = 25,
                FromAddress = "sender@example.test",
                FromName = "Test Sender"
            },
            To = "recipient@example.test"
        };
        _emailService
            .Setup(service => service.SendTestEmailAsync(
                request.Settings,
                request.To,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EmailDeliveryException(
                "smtp_rejected",
                "The SMTP server rejected the test email.",
                553));

        var result = await _controller.TestEmail(request);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, objectResult.StatusCode);
        var payload = JsonSerializer.SerializeToElement(objectResult.Value);
        Assert.Equal("smtp_rejected", payload.GetProperty("code").GetString());
        Assert.Equal(553, payload.GetProperty("smtpStatusCode").GetInt32());
        Assert.DoesNotContain("sender@example.test", payload.ToString());
    }
}
