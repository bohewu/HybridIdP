using Core.Application;
using Core.Application.Ports;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Web.IdP.Extensions;

namespace Tests.Web.IdP.UnitTests.Configuration;

public sealed class ProviderProofConfigurationTests
{
    [Fact]
    public void Registration_ShouldBindProviderProofAndIndependentPasswordSyncOptions()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ProviderProof:Endpoint"] = "https://provider.example.org/api/authenticate/login",
            ["ProviderProof:SharedSecret"] = "synthetic-proof-secret",
            ["ProviderProof:Timeout"] = "00:00:07",
            ["LegacyPasswordSync:Enabled"] = "false",
            ["LegacyPasswordSync:Endpoint"] = "https://provider.example.org/api/password-sync",
            ["LegacyPasswordSync:SharedSecret"] = "synthetic-sync-secret"
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton(configuration as IConfiguration);
        services.AddSingleton(Mock.Of<IApplicationDbContext>());
        services.AddCustomApplicationServices(configuration);
        using var provider = services.BuildServiceProvider();

        var proof = provider.GetRequiredService<IOptions<ProviderProofOptions>>().Value;
        Assert.Equal("ProviderProof", ProviderProofOptions.Section);
        Assert.Equal("https://provider.example.org/api/authenticate/login", proof.Endpoint);
        Assert.Equal("synthetic-proof-secret", proof.SharedSecret);
        Assert.Equal(TimeSpan.FromSeconds(7), proof.Timeout);
        Assert.IsType<ProviderProofProvider>(provider.GetRequiredService<IProofProvider>());

        var sync = provider.GetRequiredService<IOptions<LegacyPasswordSyncOptions>>().Value;
        Assert.False(sync.Enabled);
        Assert.Equal("https://provider.example.org/api/password-sync", sync.Endpoint);
        Assert.Equal("synthetic-sync-secret", sync.SharedSecret);
        Assert.IsType<LegacyPasswordSyncRequestFactory>(
            provider.GetRequiredService<ILegacyPasswordSyncRequestFactory>());
        Assert.IsType<LegacyPasswordSyncHttpTransport>(
            provider.GetRequiredService<ILegacyPasswordSyncTransport>());
    }

    [Theory]
    [InlineData("https://provider.example.org/api/authenticate/login", "synthetic-secret", false, 5, true)]
    [InlineData("http://provider.example.org/api/authenticate/login", "synthetic-secret", false, 5, false)]
    [InlineData("http://provider.example.org/api/authenticate/login", "synthetic-secret", true, 5, true)]
    [InlineData(null, "synthetic-secret", false, 5, false)]
    [InlineData("https://provider.example.org/api/authenticate/login", null, false, 5, false)]
    [InlineData("https://provider.example.org/api/authenticate/login", "synthetic-secret", false, 0, false)]
    [InlineData("https://provider.example.org/api/authenticate/login", "synthetic-secret", false, 31, false)]
    public void Options_ShouldPreserveProtectedEndpointSecretAndTimeoutValidation(
        string? endpoint, string? secret, bool privateHttp, int seconds, bool expected)
    {
        var validator = new ProviderProofOptionsValidator(Microsoft.Extensions.Options.Options.Create(new DirectoryIntegrationOptions { Enabled = true }));
        var result = validator.Validate(null, new ProviderProofOptions
        {
            Endpoint = endpoint,
            SharedSecret = secret,
            AllowPrivateNetworkHttp = privateHttp,
            Timeout = TimeSpan.FromSeconds(seconds)
        });
        Assert.Equal(expected, result.Succeeded);
    }
}
