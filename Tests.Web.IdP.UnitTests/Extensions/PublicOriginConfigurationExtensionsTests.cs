using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Web.IdP.Extensions;

namespace Tests.Web.IdP.UnitTests.Extensions;

public sealed class PublicOriginConfigurationExtensionsTests
{
    [Fact]
    public void ConfigurePublicOrigin_ShouldRequireIssuerInProduction()
    {
        var builder = CreateBuilder(Environments.Production);

        var exception = Assert.Throws<InvalidOperationException>(
            builder.ConfigurePublicOrigin);

        Assert.Contains("OpenIddict:Issuer", exception.Message);
    }

    [Theory]
    [InlineData("http://idp.example.test/")]
    [InlineData("https://user@idp.example.test/")]
    [InlineData("https://idp.example.test/tenant")]
    [InlineData("https://idp.example.test/?mode=test")]
    [InlineData("https://idp.example.test/#fragment")]
    [InlineData("not-a-uri")]
    public void ConfigurePublicOrigin_ShouldRejectValuesThatAreNotHttpsOrigins(
        string issuer)
    {
        var builder = CreateBuilder(
            Environments.Production,
            new Dictionary<string, string?>
            {
                ["OpenIddict:Issuer"] = issuer
            });

        Assert.Throws<InvalidOperationException>(builder.ConfigurePublicOrigin);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("other.example.test")]
    public void ConfigurePublicOrigin_ShouldRejectInvalidProxyAuthority(
        string publicAuthority)
    {
        var builder = CreateBuilder(
            Environments.Production,
            new Dictionary<string, string?>
            {
                ["OpenIddict:Issuer"] = "https://idp.example.test/",
                ["PUBLIC_AUTHORITY"] = publicAuthority
            });

        var exception = Assert.Throws<InvalidOperationException>(
            builder.ConfigurePublicOrigin);

        Assert.Contains("PUBLIC_AUTHORITY", exception.Message);
    }

    [Fact]
    public void ConfigurePublicOrigin_ShouldNormalizeIssuerAndDeriveAllowedHost()
    {
        var builder = CreateBuilder(
            Environments.Production,
            new Dictionary<string, string?>
            {
                ["OpenIddict:Issuer"] = "https://idp.example.test:8443",
                ["PUBLIC_AUTHORITY"] = "idp.example.test:8443"
            });

        builder.ConfigurePublicOrigin();

        Assert.Equal(
            "https://idp.example.test:8443/",
            builder.Configuration["OpenIddict:Issuer"]);
        Assert.Equal("idp.example.test", builder.Configuration["AllowedHosts"]);
    }

    [Fact]
    public void ConfigurePublicOrigin_ShouldAllowNonProductionRequestDerivedIssuer()
    {
        var builder = CreateBuilder(Environments.Development);

        builder.ConfigurePublicOrigin();

        Assert.Null(builder.Configuration["OpenIddict:Issuer"]);
    }

    private static WebApplicationBuilder CreateBuilder(
        string environmentName,
        IReadOnlyDictionary<string, string?>? configuration = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName
        });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(configuration);
        return builder;
    }
}
