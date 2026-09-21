using Core.Application;
using Core.Application.Ports;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Web.IdP.Extensions;
using Xunit;

namespace Tests.Web.IdP.UnitTests.Configuration;

public sealed class ProviderMetadataConfigurationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Registration_ShouldBindNeutralOptionsAndResolveConsumer(bool enabled)
    {
        var values = new Dictionary<string, string?>
        {
            ["ProviderMetadataRefresh:Enabled"] = enabled.ToString(),
            ["ProviderMetadataRefresh:Endpoint"] = "https://provider.example.org/api/authenticate/metadata",
            ["ProviderMetadataRefresh:SharedSecret"] = "synthetic-service-secret",
            ["ProviderMetadataRefresh:Timeout"] = "00:00:07"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton(configuration as IConfiguration);
        services.AddSingleton(Mock.Of<IApplicationDbContext>());
        services.AddCustomApplicationServices(configuration);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ProviderMetadataRefreshOptions>>().Value;
        Assert.Equal(enabled, options.Enabled);
        Assert.Equal(values["ProviderMetadataRefresh:Endpoint"], options.Endpoint);
        Assert.Equal(values["ProviderMetadataRefresh:SharedSecret"], options.SharedSecret);
        Assert.Equal(TimeSpan.FromSeconds(7), options.Timeout);
        Assert.IsType<ProviderMetadataRefreshService>(provider.GetRequiredService<IProviderMetadataRefreshService>());
    }

    [Theory]
    [InlineData("https://provider.example.org/api/authenticate/metadata", "synthetic-secret", false, 5, true)]
    [InlineData("http://provider.example.org/api/authenticate/metadata", "synthetic-secret", false, 5, false)]
    [InlineData("http://provider.example.org/api/authenticate/metadata", "synthetic-secret", true, 5, true)]
    [InlineData(null, "synthetic-secret", false, 5, false)]
    [InlineData("https://provider.example.org/api/authenticate/metadata", null, false, 5, false)]
    [InlineData("https://provider.example.org/api/authenticate/metadata", "synthetic-secret", false, 0, false)]
    [InlineData("https://provider.example.org/api/authenticate/metadata", "synthetic-secret", false, 31, false)]
    public void Options_ShouldValidateEndpointSecretAndBoundedTimeout(
        string? endpoint, string? secret, bool privateHttp, int seconds, bool expected)
    {
        var options = new ProviderMetadataRefreshOptions
        {
            Enabled = true,
            Endpoint = endpoint,
            SharedSecret = secret,
            AllowPrivateNetworkHttp = privateHttp,
            Timeout = TimeSpan.FromSeconds(seconds)
        };
        Assert.Equal(expected, new ProviderMetadataRefreshOptionsValidator().Validate(null, options).Succeeded);
    }
}
