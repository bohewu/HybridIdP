using System.Net;
using Core.Application;
using Core.Application.Options;
using Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public class TurnstileServiceTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    public async Task ValidateToken_ShouldKeepConfiguredEnforcementDuringProbeOutage(bool enabled, bool available, bool expected)
    {
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var state = Mock.Of<ITurnstileStateService>(s => s.IsAvailable == available);
        var service = new TurnstileService(factory.Object, Options.Create(new TurnstileOptions { Enabled = enabled }),
            state, Mock.Of<ISettingsService>(), NullLogger<TurnstileService>.Instance);

        Assert.Equal(expected, await service.ValidateTokenAsync("fixture"));
        factory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidateToken_ShouldHonorActualSiteVerification(bool succeeds)
    {
        using var client = new HttpClient(new VerificationHandler(succeeds));
        var factory = Mock.Of<IHttpClientFactory>(f => f.CreateClient(It.IsAny<string>()) == client);
        var service = new TurnstileService(factory, Options.Create(new TurnstileOptions { Enabled = true, SecretKey = "dummy" }),
            Mock.Of<ITurnstileStateService>(s => s.IsAvailable == true), Mock.Of<ISettingsService>(), NullLogger<TurnstileService>.Instance);

        Assert.Equal(succeeds, await service.ValidateTokenAsync("fixture"));
    }

    private sealed class VerificationHandler(bool succeeds) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(succeeds ? "{\"success\":true}" : "{\"success\":false}") });
    }
}
