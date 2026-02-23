using Core.Application;
using Moq;
using Web.IdP.Helpers;

namespace Tests.Web.IdP.UnitTests.Helpers;

public class DynamicLocalizedTextResolverTests
{
    [Fact]
    public async Task ResolveAsync_NullValue_ReturnsNull()
    {
        var localizationService = new Mock<ILocalizationService>();

        var result = await DynamicLocalizedTextResolver.ResolveAsync(null, "en-US", localizationService.Object);

        Assert.Null(result);
        localizationService.Verify(
            service => service.GetLocalizedStringAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_WhitespaceValue_ReturnsNull()
    {
        var localizationService = new Mock<ILocalizationService>();

        var result = await DynamicLocalizedTextResolver.ResolveAsync("   ", "en-US", localizationService.Object);

        Assert.Null(result);
        localizationService.Verify(
            service => service.GetLocalizedStringAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_PlainText_ReturnsAsIs()
    {
        var localizationService = new Mock<ILocalizationService>();

        var result = await DynamicLocalizedTextResolver.ResolveAsync("Email or username", "en-US", localizationService.Object);

        Assert.Equal("Email or username", result);
        localizationService.Verify(
            service => service.GetLocalizedStringAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_LocalizationKeyWithValue_ReturnsLocalizedValue()
    {
        var localizationService = new Mock<ILocalizationService>();
        localizationService
            .Setup(service => service.GetLocalizedStringAsync("LoginNotice.EmailOrUsername", "en-US"))
            .ReturnsAsync("Use your work email");

        var result = await DynamicLocalizedTextResolver.ResolveAsync("@LoginNotice.EmailOrUsername", "en-US", localizationService.Object);

        Assert.Equal("Use your work email", result);
    }

    [Fact]
    public async Task ResolveAsync_LocalizationKeyWithoutName_ReturnsNull()
    {
        var localizationService = new Mock<ILocalizationService>();

        var result = await DynamicLocalizedTextResolver.ResolveAsync("@   ", "en-US", localizationService.Object);

        Assert.Null(result);
        localizationService.Verify(
            service => service.GetLocalizedStringAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_LocalizationKeyNotFound_ReturnsNull()
    {
        var localizationService = new Mock<ILocalizationService>();
        localizationService
            .Setup(service => service.GetLocalizedStringAsync("MissingKey", "en-US"))
            .ReturnsAsync((string?)null);

        var result = await DynamicLocalizedTextResolver.ResolveAsync("@MissingKey", "en-US", localizationService.Object);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_LocalizationKeyWithWhitespaceResult_ReturnsNull()
    {
        var localizationService = new Mock<ILocalizationService>();
        localizationService
            .Setup(service => service.GetLocalizedStringAsync("DisabledKey", "en-US"))
            .ReturnsAsync(" ");

        var result = await DynamicLocalizedTextResolver.ResolveAsync("@DisabledKey", "en-US", localizationService.Object);

        Assert.Null(result);
    }
}
