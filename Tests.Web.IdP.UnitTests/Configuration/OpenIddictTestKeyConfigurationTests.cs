using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Web.IdP.Extensions;

namespace Tests.Web.IdP.UnitTests.Configuration;

public sealed class OpenIddictTestKeyConfigurationTests
{
    private const string TestConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=OpenIddictTestKeyConfigurationTests;Trusted_Connection=True";

    [Fact]
    public void AddCustomIdentityAndAccess_ShouldRejectEphemeralKeysOutsideTestEnvironments()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();
        var environment = CreateEnvironment("Production");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddCustomIdentityAndAccess(
                configuration,
                environment,
                "SqlServer",
                TestConnectionString));

        Assert.Contains("restricted to Development and Test", exception.Message);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void AddCustomIdentityAndAccess_ShouldAllowEphemeralKeysInTestEnvironments(
        string environmentName)
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();
        var environment = CreateEnvironment(environmentName);

        var exception = Record.Exception(() =>
            services.AddCustomIdentityAndAccess(
                configuration,
                environment,
                "SqlServer",
                TestConnectionString));

        Assert.Null(exception);
    }

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenIddict:UseEphemeralKeysForTesting"] = "true"
            })
            .Build();

    private static IWebHostEnvironment CreateEnvironment(string environmentName)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns(environmentName);
        return environment.Object;
    }
}
