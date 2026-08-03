using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Web.IdP.Extensions;

namespace Tests.Web.IdP.UnitTests.Extensions;

public class ExternalLoginProviderConfigurationTests
{
    [Fact]
    public async Task AddCustomIdentityAndAccess_ShouldIssueProviderControlledEmailAssurance()
    {
        var providerClientSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExternalLogin:Google:Enabled"] = "true",
                ["ExternalLogin:Google:ClientId"] = "google-client",
                ["ExternalLogin:Google:ClientSecret"] = providerClientSecret,
                ["ExternalLogin:Microsoft:Enabled"] = "true",
                ["ExternalLogin:Microsoft:ClientId"] = "microsoft-client",
                ["ExternalLogin:Microsoft:ClientSecret"] = providerClientSecret
            })
            .Build();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns(Environments.Development);
        environment.SetupGet(value => value.ApplicationName).Returns("Web.IdP");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCustomIdentityAndAccess(
            configuration,
            environment.Object,
            "SqlServer",
            "Server=(localdb)\\mssqllocaldb;Database=ExternalProviderOptions;Trusted_Connection=True");

        using var serviceProvider = services.BuildServiceProvider();
        var googleOptions = serviceProvider
            .GetRequiredService<IOptionsMonitor<GoogleOptions>>()
            .Get(GoogleDefaults.AuthenticationScheme);
        var googleAssuranceAction = Assert.Single(
            googleOptions.ClaimActions,
            action => action.ClaimType == AuthConstants.Claims.ExternalEmailVerified);

        using var googleUser = JsonDocument.Parse("""{"verified_email":true}""");
        var googleIdentity = new ClaimsIdentity();
        googleAssuranceAction.Run(
            googleUser.RootElement,
            googleIdentity,
            GoogleDefaults.AuthenticationScheme);

        Assert.Equal(
            "true",
            googleIdentity.FindFirst(AuthConstants.Claims.ExternalEmailVerified)?.Value,
            ignoreCase: true);

        var microsoftOptions = serviceProvider
            .GetRequiredService<IOptionsMonitor<MicrosoftAccountOptions>>()
            .Get(MicrosoftAccountDefaults.AuthenticationScheme);
        var microsoftIdentity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, "user@example.com")],
            MicrosoftAccountDefaults.AuthenticationScheme);
        using var microsoftUser = JsonDocument.Parse(
            """{"mail":"user@example.com","userPrincipalName":"user@example.com"}""");
        using var tokenDocument = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            access_token = Guid.NewGuid().ToString("N")
        }));
        using var tokenResponse = OAuthTokenResponse.Success(tokenDocument);
        using var backchannel = new HttpClient();
        var ticketContext = new OAuthCreatingTicketContext(
            new ClaimsPrincipal(microsoftIdentity),
            new AuthenticationProperties(),
            new DefaultHttpContext(),
            new AuthenticationScheme(
                MicrosoftAccountDefaults.AuthenticationScheme,
                MicrosoftAccountDefaults.DisplayName,
                typeof(MicrosoftAccountHandler)),
            microsoftOptions,
            backchannel,
            tokenResponse,
            microsoftUser.RootElement);

        await microsoftOptions.Events.OnCreatingTicket(ticketContext);

        Assert.Equal(
            bool.TrueString,
            microsoftIdentity.FindFirst(AuthConstants.Claims.ExternalEmailVerified)?.Value,
            ignoreCase: true);

        var aliasIdentity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, "alias@example.com")],
            MicrosoftAccountDefaults.AuthenticationScheme);
        using var aliasUser = JsonDocument.Parse(
            """{"mail":"alias@example.com","userPrincipalName":"user@example.com"}""");
        var aliasContext = new OAuthCreatingTicketContext(
            new ClaimsPrincipal(aliasIdentity),
            new AuthenticationProperties(),
            new DefaultHttpContext(),
            ticketContext.Scheme,
            microsoftOptions,
            backchannel,
            tokenResponse,
            aliasUser.RootElement);

        await microsoftOptions.Events.OnCreatingTicket(aliasContext);

        Assert.Null(aliasIdentity.FindFirst(AuthConstants.Claims.ExternalEmailVerified));
    }
}
