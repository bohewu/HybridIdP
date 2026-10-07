using System.Threading;
using System.Threading.Tasks;
using System.Net.Security;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Core.Domain.Models;
using Infrastructure.Services;
using Core.Application;
using Core.Domain.Constants;
using Infrastructure.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Core.Application.Options;
using FluentAssertions;

namespace Tests.Infrastructure.UnitTests;

public class SmtpDispatcherTests
{
    private readonly Mock<IOptionsSnapshot<EmailOptions>> _mockOptions;
    private readonly Mock<ILogger<SmtpDispatcher>> _mockLogger;

    public SmtpDispatcherTests()
    {
        _mockOptions = new Mock<IOptionsSnapshot<EmailOptions>>();
        _mockLogger = new Mock<ILogger<SmtpDispatcher>>();
    }

    [Fact]
    public async Task SendAsync_ShouldRetrieveSettings_WhenSmtpNotConfigured()
    {
        _mockOptions.Setup(o => o.Value).Returns(new EmailOptions());
        var dispatcher = new SmtpDispatcher(_mockOptions.Object, _mockLogger.Object);
        var message = new EmailMessage("to@test.com", "Subject", "Body");

        await dispatcher.SendAsync(message);

        _mockOptions.Verify(o => o.Value, Times.Once);
    }

    [Fact]
    public async Task SendTestAsync_ShouldRetrieveDeploymentPolicy_WhenSmtpNotConfigured()
    {
        _mockOptions.Setup(o => o.Value).Returns(new EmailOptions { SmtpRequireTls = true });
        var dispatcher = new SmtpDispatcher(_mockOptions.Object, _mockLogger.Object);
        var message = new EmailMessage("to@test.com", "Subject", "Body");

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(() =>
            dispatcher.SendTestAsync(message, new MailSettingsDto { EnableSsl = false }));

        exception.Code.Should().Be("smtp_not_configured");
        _mockOptions.Verify(o => o.Value, Times.Once);
    }

    [Fact]
    public void EmailOptions_ShouldDefaultToOptionalTlsAndCertificateValidation()
    {
        var options = new EmailOptions();

        options.SmtpRequireTls.Should().BeFalse();
        options.SmtpValidateServerCertificate.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false, SecureSocketOptions.StartTlsWhenAvailable)]
    [InlineData(false, true, SecureSocketOptions.StartTls)]
    [InlineData(true, false, SecureSocketOptions.SslOnConnect)]
    [InlineData(true, true, SecureSocketOptions.SslOnConnect)]
    public void ApplySecurityPolicy_ShouldSelectTransport_WhenTlsPolicyIsConfigured(
        bool enableSsl, bool requireTls, SecureSocketOptions expected)
    {
        using var client = new SmtpClient();
        var settings = new MailSettingsDto { EnableSsl = enableSsl };
        var policy = new EmailOptions { SmtpRequireTls = requireTls };

        var selected = SmtpDispatcher.ApplySecurityPolicy(client, settings, policy);

        selected.Should().Be(expected);
    }

    [Theory]
    [InlineData("localhost", false)]
    [InlineData("localhost", true)]
    [InlineData("smtp.example.test", false)]
    [InlineData("smtp.example.test", true)]
    public void ApplySecurityPolicy_ShouldRetainCertificateValidation_WhenNoExceptionIsSelected(
        string host, bool enableSsl)
    {
        using var client = new SmtpClient();
        var settings = new MailSettingsDto { Host = host, EnableSsl = enableSsl };

        SmtpDispatcher.ApplySecurityPolicy(client, settings, new EmailOptions());

        client.ServerCertificateValidationCallback.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplySecurityPolicy_ShouldAllowCertificateException_OnlyWhenExplicitlySelected(bool requireTls)
    {
        using var client = new SmtpClient();
        var policy = new EmailOptions
        {
            SmtpRequireTls = requireTls,
            SmtpValidateServerCertificate = false
        };

        var selected = SmtpDispatcher.ApplySecurityPolicy(
            client, new MailSettingsDto { EnableSsl = false }, policy);

        selected.Should().Be(requireTls ? SecureSocketOptions.StartTls : SecureSocketOptions.StartTlsWhenAvailable);
        client.ServerCertificateValidationCallback.Should().NotBeNull();
        client.ServerCertificateValidationCallback!(client, null, null,
            SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors)
            .Should().BeTrue();
    }

    [Fact]
    public void PostConfigure_ShouldRetainRequiredProtection_WhenPersistedSettingsDisableImplicitTls()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetValueAsync<string>(SettingKeys.Email.SmtpEnableSsl, It.IsAny<CancellationToken>()))
            .ReturnsAsync("false");
        var services = new ServiceCollection();
        services.AddSingleton(settings.Object);
        using var provider = services.BuildServiceProvider();
        var options = new EmailOptions
        {
            SmtpEnableSsl = true,
            SmtpRequireTls = true,
            SmtpValidateServerCertificate = true
        };
        var configure = new ConfigureEmailOptions(provider.GetRequiredService<IServiceScopeFactory>());

        configure.PostConfigure(null, options);

        options.SmtpEnableSsl.Should().BeFalse();
        options.SmtpRequireTls.Should().BeTrue();
        options.SmtpValidateServerCertificate.Should().BeTrue();
        using var client = new SmtpClient();
        SmtpDispatcher.ApplySecurityPolicy(client,
            new MailSettingsDto { EnableSsl = options.SmtpEnableSsl }, options)
            .Should().Be(SecureSocketOptions.StartTls);
        client.ServerCertificateValidationCallback.Should().BeNull();
    }
}
