using System.Globalization;
using Core.Application;
using Infrastructure.Resources;
using Infrastructure.Services;
using Moq;
using Web.IdP.Services.Localization;

namespace Tests.Web.IdP.UnitTests.Services.Localization;

public sealed class EmailTemplateLocalizationTests
{
    private static readonly string ResourcesPath = Path.Combine(AppContext.BaseDirectory, "Resources");

    [Theory]
    [InlineData("en-US", "Your verification code", "Enter this code to complete your sign-in.")]
    [InlineData("zh-TW", "您的驗證碼", "請輸入以下驗證碼以完成登入。")]
    public async Task RenderMfaCodeEmailAsync_ShouldUseLocalizedHtmlTemplate(
        string culture,
        string expectedSubject,
        string expectedInstruction)
    {
        var brandingService = new Mock<IBrandingService>();
        brandingService.Setup(service => service.GetProductNameAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("NCUT IdP");
        brandingService.Setup(service => service.GetCopyrightAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("Copyright NCUT");

        var localizer = new JsonStringLocalizer<EmailTemplateResource>([ResourcesPath]);
        var service = new EmailTemplateService(brandingService.Object, localizer);
        var originalCulture = CultureInfo.CurrentUICulture;

        var (subject, body) = await service.RenderMfaCodeEmailAsync("372507", 10, culture);

        Assert.Equal(originalCulture, CultureInfo.CurrentUICulture);
        Assert.Contains(expectedSubject, subject, StringComparison.Ordinal);
        Assert.Contains(expectedInstruction, body, StringComparison.Ordinal);
        Assert.Contains("NCUT IdP", body, StringComparison.Ordinal);
        Assert.Contains("372507", body, StringComparison.Ordinal);
        Assert.Contains("<table", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{ProductName}", body, StringComparison.Ordinal);
        Assert.DoesNotContain("{Code}", body, StringComparison.Ordinal);
        Assert.DoesNotContain("{ExpiryMinutes}", body, StringComparison.Ordinal);
        Assert.DoesNotContain("{Footer}", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en-US", "Use email verification code")]
    [InlineData("zh-TW", "改用電子郵件驗證碼")]
    public void SharedResource_ShouldTranslateUseEmailCode(string culture, string expected)
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var localizer = new JsonStringLocalizer<global::Web.IdP.SharedResource>([ResourcesPath]);

            var localized = localizer["UseEmailCode"];

            Assert.False(localized.ResourceNotFound);
            Assert.Equal(expected, localized.Value);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public async Task RenderMfaCodeEmailAsync_ShouldEncodeHtmlPlaceholderValues()
    {
        const string productName = "<img src=x onerror=\"alert(1)\"> & NCUT";
        const string copyright = "<script>alert('copyright')</script>";
        const string code = "12<3&45";
        var brandingService = new Mock<IBrandingService>();
        brandingService.Setup(service => service.GetProductNameAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(productName);
        brandingService.Setup(service => service.GetCopyrightAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(copyright);

        var localizer = new JsonStringLocalizer<EmailTemplateResource>([ResourcesPath]);
        var service = new EmailTemplateService(brandingService.Object, localizer);

        var (subject, body) = await service.RenderMfaCodeEmailAsync(code, 10, "en-US");

        Assert.Contains(productName, subject, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(code, body, StringComparison.Ordinal);
        Assert.Contains("&lt;img", body, StringComparison.Ordinal);
        Assert.Contains("&lt;script", body, StringComparison.Ordinal);
        Assert.Contains("12&lt;3&amp;45", body, StringComparison.Ordinal);
    }
}
