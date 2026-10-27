using System.Net;
using System.Net.Http.Json;
using Core.Application;
using Core.Application.Options;
using Core.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Web.IdP.Controllers.Admin;
using Web.IdP.Services.Saml.Idp;

namespace Tests.Web.IdP.UnitTests.Controllers;

public sealed class SamlAttributesControllerTests
{
    [Theory]
    [InlineData("preview", null, false, false, HttpStatusCode.Unauthorized)]
    [InlineData("preview", "settings.update", false, true, HttpStatusCode.Forbidden)]
    [InlineData("preview", "settings.read", false, true, HttpStatusCode.Forbidden)]
    [InlineData("preview", "claims.preview", true, true, HttpStatusCode.Unauthorized)]
    [InlineData("preview", "claims.preview", false, false, HttpStatusCode.BadRequest)]
    [InlineData("preview", "claims.preview", false, true, HttpStatusCode.OK)]
    [InlineData("accounts", "settings.read", false, false, HttpStatusCode.Forbidden)]
    [InlineData("accounts", "claims.preview", false, false, HttpStatusCode.OK)]
    [InlineData("policy", "claims.preview", false, true, HttpStatusCode.Forbidden)]
    [InlineData("policy", "settings.update", true, true, HttpStatusCode.Unauthorized)]
    [InlineData("policy", "settings.update", false, false, HttpStatusCode.BadRequest)]
    [InlineData("policy", "settings.update", false, true, HttpStatusCode.OK)]
    [InlineData("read-policy", "claims.preview", false, false, HttpStatusCode.Forbidden)]
    [InlineData("read-policy", "settings.read", false, false, HttpStatusCode.OK)]
    public async Task Endpoints_ShouldEnforceIndependentPermissionsCookieCsrfAndValueFreeAudit(
        string operation, string? permission, bool bearer, bool csrf, HttpStatusCode expected)
    {
        using var federation = new SamlIdpFixture();
        await using var db = SamlAttributeTests.CreateDb();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = SamlAttributeTests.CreateService(federation, db, cache);
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "fixture", Email = "private-email@example.test" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await service.SavePolicyAsync(new(SamlIdpFixture.Partner, [new("Email", "mail")]), "actor", default);
        var before = await db.Settings.Select(s => s.Value).SingleAsync();
        await using var app = ClaimsPreviewControllerTests.CreateApp(db, Mock.Of<IClaimsPreviewService>(), service);
        await app.StartAsync();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(app.Urls.Single()) };
            if (permission is not null) client.DefaultRequestHeaders.Add("X-Fixture-Permission", permission);
            if (bearer) client.DefaultRequestHeaders.Add("Authorization", "Bearer fixture-only");
            if (csrf) client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await client.GetStringAsync("/fixture-antiforgery"));
            using var response = operation switch
            {
                "preview" => await client.PostAsJsonAsync("/api/admin/saml-attributes/preview", new { entityId = SamlIdpFixture.Partner, userId = user.Id }),
                "policy" => await client.PutAsJsonAsync("/api/admin/saml-attributes/policy", new SamlAttributePolicyDto(SamlIdpFixture.Partner, [])),
                "read-policy" => await client.GetAsync("/api/admin/saml-attributes/policy?entityId=" + Uri.EscapeDataString(SamlIdpFixture.Partner)),
                _ => await client.GetAsync("/api/admin/saml-attributes/accounts?search=fixture")
            };
            Assert.True(response.StatusCode == expected, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            if (expected == HttpStatusCode.OK)
            {
                Assert.True(response.Headers.CacheControl?.NoStore);
                if (operation is "preview" or "policy")
                {
                    var audit = Assert.Single(await db.AuditEvents.ToListAsync());
                    Assert.DoesNotContain(user.Email, audit.Details);
                    Assert.DoesNotContain("\"mail\"", audit.Details);
                    Assert.Equal(operation == "preview" ? "SamlAttributesPreviewed" : "SamlAttributePolicyUpdated", audit.EventType);
                }
                if (operation == "preview") Assert.Contains(user.Email, await response.Content.ReadAsStringAsync());
            }
            else
            {
                Assert.Empty(await db.AuditEvents.ToListAsync());
                Assert.Equal(before, await db.Settings.Select(s => s.Value).SingleAsync());
                Assert.DoesNotContain(user.Email, await response.Content.ReadAsStringAsync());
            }
        }
        finally { await app.StopAsync(); }
    }

    [Theory]
    [InlineData("saml.attributes.v1.")]
    [InlineData("\uFF33aml.Attributes.v1.")]
    public async Task GenericSettingsWrite_ShouldRejectReservedPrefixBeforeCallingSettingsService(string prefix)
    {
        var settings = new Mock<ISettingsService>(MockBehavior.Strict);
        var controller = new SettingsController(settings.Object, Mock.Of<IEmailService>(), new ConfigurationBuilder().Build(),
            Mock.Of<IOptionsSnapshot<EmailOptions>>(), Mock.Of<IAuditService>());
        Assert.IsType<BadRequestObjectResult>(await controller.UpdateSetting(
            prefix + new string('A', 64), new UpdateSettingRequest("{}")));
        settings.VerifyNoOtherCalls();
    }
}
