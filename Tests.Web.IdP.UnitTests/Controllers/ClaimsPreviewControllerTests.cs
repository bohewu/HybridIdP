using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Options;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Events;
using Infrastructure;
using Infrastructure.Authorization;
using Infrastructure.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using Web.IdP.Controllers.Admin;

namespace Tests.Web.IdP.UnitTests.Controllers;

public sealed class ClaimsPreviewControllerTests
{
    private static readonly Guid ActorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TargetId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData(null, false, false, HttpStatusCode.Unauthorized)]
    [InlineData("claims.read", false, true, HttpStatusCode.Forbidden)]
    [InlineData("claims.preview", true, true, HttpStatusCode.Unauthorized)]
    [InlineData("claims.preview", false, false, HttpStatusCode.BadRequest)]
    [InlineData("claims.preview", false, true, HttpStatusCode.OK)]
    public async Task Preview_ShouldEnforceCookiePermissionAndCsrfBeforeMetadataAudit(string? permission, bool bearer, bool csrf, HttpStatusCode expected)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var preview = new Mock<IClaimsPreviewService>(MockBehavior.Strict);
        var payload = new Dictionary<string, JsonElement> { ["private_value"] = JsonSerializer.SerializeToElement("never-log-this-value") };
        preview.Setup(p => p.PreviewAsync(It.IsAny<ClaimsPreviewRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClaimsPreviewResponse("fixture-client", TargetId, ["openid"], true, payload, payload, payload, [], []));
        await using var app = CreateApp(db, preview.Object);
        await app.StartAsync();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(app.Urls.Single()) };
            if (permission is not null) client.DefaultRequestHeaders.Add("X-Fixture-Permission", permission);
            if (bearer) client.DefaultRequestHeaders.Add("Authorization", "Bearer fixture-only");
            if (csrf)
            {
                var token = await client.GetStringAsync("/fixture-antiforgery");
                client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
            }
            using var response = await client.PostAsJsonAsync("/api/admin/claims-preview",
                new ClaimsPreviewRequest { ClientId = "fixture-client", UserId = TargetId, Scopes = ["openid"] });
            Assert.True(response.StatusCode == expected,
                $"Expected {expected}, received {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            if (expected == HttpStatusCode.OK)
            {
                Assert.True(response.Headers.CacheControl?.NoStore);
                Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
                Assert.Contains("never-log-this-value", await response.Content.ReadAsStringAsync());
                var audit = Assert.Single(await db.AuditEvents.AsNoTracking().ToListAsync());
                Assert.Equal(AuditEventTypes.ClaimsPreviewed, audit.EventType);
                Assert.Equal(ActorId.ToString(), audit.UserId);
                Assert.Contains(TargetId.ToString(), audit.Details);
                Assert.Contains("fixture-client", audit.Details);
                Assert.DoesNotContain("never-log-this-value", audit.Details);
                Assert.DoesNotContain("private_value", audit.Details);
            }
            else
            {
                preview.Verify(p => p.PreviewAsync(It.IsAny<ClaimsPreviewRequest>(), It.IsAny<CancellationToken>()), Times.Never());
                Assert.Empty(await db.AuditEvents.ToListAsync());
            }
        }
        finally { await app.StopAsync(); }
    }

    private static WebApplication CreateApp(ApplicationDbContext db, IClaimsPreviewService preview)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = "127.0.0.1" });
        builder.Logging.ClearProviders();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(ClaimsPreviewController).Assembly);
        builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>(IdentityConstants.ApplicationScheme, _ => { })
            .AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>(AdministrativeAuthorizationBoundary.BearerScheme, _ => { });
        builder.Services.AddAuthorization(o => o.AddPolicy(Permissions.Claims.Preview,
            p => p.AddRequirements(new PermissionRequirement(Permissions.Claims.Preview))));
        builder.Services.AddScoped<IAdministrativeAuthorizationBoundary, AdministrativeAuthorizationBoundary>();
        builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddSingleton(new Mock<RoleManager<ApplicationRole>>(Mock.Of<IRoleStore<ApplicationRole>>(), null, null, null, null).Object);
        builder.Services.AddSingleton(Mock.Of<IOpenIddictApplicationManager>());
        builder.Services.AddSingleton(preview);
        builder.Services.AddSingleton(db);
        builder.Services.AddSingleton<IApplicationDbContext>(db);
        builder.Services.AddSingleton(Mock.Of<IDomainEventPublisher>());
        builder.Services.AddSingleton(Mock.Of<ISettingsService>());
        builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new AuditOptions()));
        builder.Services.AddScoped<IAuditService, AuditService>();
        var app = builder.Build();
        app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
        app.MapGet("/fixture-antiforgery", (HttpContext context, IAntiforgery antiforgery) => antiforgery.GetAndStoreTokens(context).RequestToken!);
        app.MapControllers();
        return app;
    }

    private sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Fixture-Permission")) return Task.FromResult(AuthenticateResult.NoResult());
            var bearer = Request.Headers.Authorization.Count > 0;
            if (bearer != (Scheme.Name == AdministrativeAuthorizationBoundary.BearerScheme)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, ActorId.ToString()),
                new Claim("permission", Request.Headers["X-Fixture-Permission"].ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
