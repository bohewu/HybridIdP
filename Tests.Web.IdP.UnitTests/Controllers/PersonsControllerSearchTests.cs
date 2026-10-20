using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Core.Application;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Web.IdP.Controllers.Admin;

namespace Tests.Web.IdP.UnitTests.Controllers;

public sealed class PersonsControllerSearchTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("%20")]
    [InlineData("%20%20%20")]
    [InlineData("%09")]
    [InlineData("%E3%80%80")]
    [InlineData("%20Example%20")]
    public async Task Search_ShouldNormalizeInputBeforeSelectingPagedQuery(string? encodedTerm)
    {
        var service = new Mock<IPersonService>(MockBehavior.Strict);
        service.Setup(value => value.GetAllPersonsAsync(5, 2, It.IsAny<CancellationToken>(), "desc"))
            .ReturnsAsync(new List<Person>());
        service.Setup(value => value.SearchPersonsAsync("Example", 5, 2, It.IsAny<CancellationToken>(), "desc"))
            .ReturnsAsync(new List<Person>());
        service.Setup(value => value.GetPersonsCountAsync(It.IsAny<CancellationToken>(), It.IsAny<string?>())).ReturnsAsync(0);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = "127.0.0.1" });
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(PersonsController).Assembly);
        builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>(IdentityConstants.ApplicationScheme, _ => { })
            .AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>(AdministrativeAuthorizationBoundary.BearerScheme, _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy(Permissions.Persons.Read,
            policy => policy.RequireAuthenticatedUser()));
        builder.Services.AddSingleton(service.Object);
        builder.Services.AddSingleton(Mock.Of<IAuditService>());
        builder.Services.AddSingleton(Mock.Of<IUserManagementService>());
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            var term = encodedTerm is null ? "" : $"&term={encodedTerm}";
            using var response = await client.GetAsync($"/api/admin/people/search?skip=5&take=2&sortDirection=desc{term}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            if (encodedTerm == "%20Example%20")
            {
                service.Verify(value => value.SearchPersonsAsync("Example", 5, 2, It.IsAny<CancellationToken>(), "desc"), Times.Once);
                service.Verify(value => value.GetPersonsCountAsync(It.IsAny<CancellationToken>(), "Example"), Times.Once);
                service.Verify(value => value.GetAllPersonsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
            }
            else
            {
                service.Verify(value => value.GetAllPersonsAsync(5, 2, It.IsAny<CancellationToken>(), "desc"), Times.Once);
                service.Verify(value => value.SearchPersonsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
            }
        }
        finally { await app.StopAsync(); }
    }

    private sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,
                "11111111-1111-1111-1111-111111111111")], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
