using System.Globalization;
using Core.Application;
using Core.Application.Interfaces;
using Infrastructure.Services;
using Microsoft.Extensions.Localization;
using Moq;
using Xunit;
using Infrastructure.Resources;

namespace Tests.Application.UnitTests;

/// <summary>
/// Integration tests for EmailTemplateService with localization.
/// Uses mocked localizer to verify template rendering logic.
/// </summary>
public class EmailTemplateLocalizationTests : IDisposable
{
    private readonly Mock<IBrandingService> _brandingServiceMock;
    private readonly Mock<IStringLocalizer<EmailTemplateResource>> _localizerMock;
    private readonly EmailTemplateService _emailTemplateService;
    private readonly CultureInfo _originalCulture;

    public EmailTemplateLocalizationTests()
    {
        _originalCulture = CultureInfo.CurrentUICulture;

        // Setup branding service mock
        _brandingServiceMock = new Mock<IBrandingService>();
        _brandingServiceMock.Setup(x => x.GetProductNameAsync())
            .ReturnsAsync("HybridIdP");
        _brandingServiceMock.Setup(x => x.GetCopyrightAsync())
            .ReturnsAsync("© 2025 Test Company");

        // Setup localizer mock
        _localizerMock = new Mock<IStringLocalizer<EmailTemplateResource>>();

        _emailTemplateService = new EmailTemplateService(
            _brandingServiceMock.Object,
            _localizerMock.Object);
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalCulture;
    }

    [Fact]
    public async Task RenderMfaCodeEmail_EnglishTemplate_ReturnsCorrectContent()
    {
        // Arrange
        _localizerMock.Setup(x => x["MfaCode_Subject"])
            .Returns(new LocalizedString("MfaCode_Subject", "Your verification code - {ProductName}", false));
        _localizerMock.Setup(x => x["MfaCode_Body"])
            .Returns(new LocalizedString("MfaCode_Body", "<p>Your code is: {Code}. Expires in {ExpiryMinutes} minutes.</p>{Footer}", false));
        _localizerMock.Setup(x => x["Email_Footer"])
            .Returns(new LocalizedString("Email_Footer", "<footer>{ProductName} - {Copyright}</footer>", false));

        var code = "123456";
        var expiryMinutes = 5;

        // Act
        var (subject, body) = await _emailTemplateService.RenderMfaCodeEmailAsync(code, expiryMinutes);

        // Assert
        Assert.Equal("Your verification code - HybridIdP", subject);
        Assert.Contains(code, body);
        Assert.Contains("5", body);
        Assert.Contains("HybridIdP", body);
        Assert.Contains("© 2025 Test Company", body);
    }

    [Fact]
    public async Task RenderMfaCodeEmail_ChineseTemplate_ReturnsCorrectContent()
    {
        // Arrange
        _localizerMock.Setup(x => x["MfaCode_Subject"])
            .Returns(new LocalizedString("MfaCode_Subject", "您的驗證碼 - {ProductName}", false));
        _localizerMock.Setup(x => x["MfaCode_Body"])
            .Returns(new LocalizedString("MfaCode_Body", "<p>您的驗證碼：{Code}。此驗證碼將於 {ExpiryMinutes} 分鐘後失效。</p>{Footer}", false));
        _localizerMock.Setup(x => x["Email_Footer"])
            .Returns(new LocalizedString("Email_Footer", "<footer>{ProductName} - {Copyright}</footer>", false));

        var code = "654321";
        var expiryMinutes = 10;

        // Act
        var (subject, body) = await _emailTemplateService.RenderMfaCodeEmailAsync(code, expiryMinutes);

        // Assert
        Assert.Equal("您的驗證碼 - HybridIdP", subject);
        Assert.Contains(code, body);
        Assert.Contains("10", body);
        Assert.Contains("HybridIdP", body);
    }

    [Fact]
    public async Task RenderMfaCodeEmail_FallbackWhenResourceNotFound_UsesDefaultText()
    {
        // Arrange
        _localizerMock.Setup(x => x["MfaCode_Subject"])
            .Returns(new LocalizedString("MfaCode_Subject", "MfaCode_Subject", true)); // ResourceNotFound
        _localizerMock.Setup(x => x["MfaCode_Body"])
            .Returns(new LocalizedString("MfaCode_Body", "MfaCode_Body", true)); // ResourceNotFound
        _localizerMock.Setup(x => x["Email_Footer"])
            .Returns(new LocalizedString("Email_Footer", "Email_Footer", true)); // ResourceNotFound

        var code = "999888";
        var expiryMinutes = 3;

        // Act
        var (subject, body) = await _emailTemplateService.RenderMfaCodeEmailAsync(code, expiryMinutes);

        // Assert - should use fallback from EmailTemplateService
        Assert.Equal("Your verification code - HybridIdP", subject);
        Assert.Contains(code, body);
        Assert.Contains("<strong>", body); // Fallback HTML
    }
}
