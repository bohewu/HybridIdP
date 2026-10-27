using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Web.IdP.Services.Saml.Idp;

namespace Tests.Web.IdP.UnitTests;

public sealed class SamlAttributeTests
{
    [Fact]
    public async Task Policy_ShouldPersistSeparatelyForExactPartners_AndRetainSubjectOnlyDefault()
    {
        using var federation = new SamlIdpFixture();
        var second = SamlIdpFixture.Partner + "/other";
        federation.Runtime.Options.Idp.ServiceProviders.Add(new() { EntityId = second });
        await using var db = CreateDb();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(federation, db, cache);
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "fixture", Email = "account@example.test" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        Assert.Single((await service.ResolveAsync(SamlIdpFixture.Partner, user.Id, default)).Attributes);
        await service.SavePolicyAsync(new(SamlIdpFixture.Partner, [new("Email", "email")]), "actor", default);
        Assert.Equal("Email", Assert.Single((await service.GetPolicyAsync(SamlIdpFixture.Partner, default)).Mappings).Source);
        await service.SavePolicyAsync(new(second, [new("FirstName", "given_name")]), "actor", default);
        using var reopenedCache = new MemoryCache(new MemoryCacheOptions());
        var reopened = CreateService(federation, db, reopenedCache);
        Assert.Equal("Email", Assert.Single((await reopened.GetPolicyAsync(SamlIdpFixture.Partner, default)).Mappings).Source);
        Assert.Equal("FirstName", Assert.Single((await reopened.GetPolicyAsync(second, default)).Mappings).Source);
        Assert.Equal(2, await db.Settings.CountAsync());
        Assert.All(await db.Settings.ToListAsync(), setting => Assert.Equal("actor", setting.UpdatedBy));
        Assert.Single((await service.GetPolicyAsync(SamlIdpFixture.Partner, default)).Mappings);
        await service.SavePolicyAsync(new(SamlIdpFixture.Partner, []), "actor", default);
        Assert.Single((await service.ResolveAsync(SamlIdpFixture.Partner, user.Id, default)).Attributes);
        Assert.Equal("unknown_partner", (await Assert.ThrowsAsync<SamlAttributeException>(() =>
            service.GetPolicyAsync(SamlIdpFixture.Partner.ToUpperInvariant(), default))).Code);
    }

    [Theory]
    [InlineData("Person.NationalId", "national_id", "invalid_source")]
    [InlineData("PasswordHash", "password", "invalid_source")]
    [InlineData("PersonId", "person_id", "invalid_source")]
    [InlineData("Id", "account_id", "invalid_source")]
    [InlineData("EmailConfirmed", "verified", "invalid_source")]
    [InlineData("Person.ProviderProfilesJson", "data", "invalid_source")]
    [InlineData(" Email ", "email", "invalid_source")]
    [InlineData("Email", "urn:hybrididp:claims:subject", "invalid_name")]
    [InlineData("Email", "NameID", "invalid_name")]
    [InlineData("Email", "Role", "invalid_name")]
    [InlineData("Email", "email_verified", "invalid_name")]
    [InlineData("Email", "bad name", "invalid_name")]
    public void Policy_ShouldRejectSensitiveSourcesAndProtectedNames(string source, string name, string code)
    {
        var error = Assert.Throws<SamlAttributeException>(() => SamlAttributePolicy.Validate(new("partner", [new(source, name)])));
        Assert.Equal(code, error.Code);
    }

    [Fact]
    public void Policy_ShouldRejectCollisionsAndOversizeXmlValues()
    {
        Assert.Equal("duplicate_name", Assert.Throws<SamlAttributeException>(() => SamlAttributePolicy.Validate(
            new("partner", [new("Email", "email"), new("Person.Email", "EMAIL")]))).Code);
        Assert.Equal("duplicate_source", Assert.Throws<SamlAttributeException>(() => SamlAttributePolicy.Validate(
            new("partner", [new("Email", "email"), new("Email", "mail")]))).Code);
        foreach (var value in new[] { new string('a', 2049), "a\u0001b" })
            Assert.Equal("value_invalid", Assert.Throws<SamlAttributeException>(() => SamlAttributePolicy.Resolve(
                new("partner", [new("Email", "email")]), new ApplicationUser { Email = value })).Code);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"EntityId\":\"https://partner.example/sp\",\"Mappings\":[{\"Source\":\"Person.NationalId\",\"Name\":\"secret\"}]}")]
    [InlineData("{\"EntityId\":\"https://other.example/sp\",\"Mappings\":[]}")]
    public async Task StoredInvalidPolicy_ShouldDenyWithoutFallback(string raw)
    {
        using var federation = new SamlIdpFixture();
        await using var db = CreateDb();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        db.Settings.Add(new Setting { Id = Guid.NewGuid(), Key = SamlAttributePolicy.SettingKey(SamlIdpFixture.Partner), Value = raw });
        await db.SaveChangesAsync();
        Assert.Equal("policy_invalid", (await Assert.ThrowsAsync<SamlAttributeException>(() =>
            CreateService(federation, db, cache).GetPolicyAsync(SamlIdpFixture.Partner, default))).Code);
    }

    [Fact]
    public async Task Preview_ShouldEqualSignedRelease_KeepStableIdentity_AndNeverWriteProfile()
    {
        using var federation = new SamlIdpFixture();
        await using var db = CreateDb();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(federation, db, cache);
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "fixture", Email = "account@example.test",
            FirstName = "<Alice & Co>", Person = new Person { Id = Guid.NewGuid(), Email = "person@example.test", EmployeeId = "ORG-7" } };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await service.SavePolicyAsync(new(SamlIdpFixture.Partner, [new("Email", "mail"), new("Person.Email", "person_mail"),
            new("FirstName", "given_name"), new("Person.EmployeeId", "employee_id"), new("MiddleName", "middle_name")]), "actor", default);
        db.ChangeTracker.Clear();
        var before = await db.Settings.Select(s => s.Value).SingleAsync();
        var preview = await service.ResolveAsync(SamlIdpFixture.Partner, user.Id, default);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(before, await db.Settings.Select(s => s.Value).SingleAsync());
        Assert.Equal("missing_value", Assert.Single(preview.Omitted).Reason);
        Assert.Contains(preview.Attributes, a => a.Name == "mail" && a.Value == "account@example.test");
        Assert.Contains(preview.Attributes, a => a.Name == "person_mail" && a.Value == "person@example.test");
        var request = federation.Protocol.ReadRequest(federation.Request());
        var xml = Encoding.UTF8.GetString(Convert.FromBase64String(federation.Protocol.CreateResponse(request,
            user.Id, DateTimeOffset.UtcNow, preview.Attributes)));
        XNamespace saml = "urn:oasis:names:tc:SAML:2.0:assertion";
        var parsed = XDocument.Parse(xml);
        var emitted = parsed.Descendants(saml + "Attribute").Select(a => new SamlAttributeValueDto(
            a.Attribute("Name")!.Value, a.Element(saml + "AttributeValue")!.Value)).OrderBy(a => a.Name).ToArray();
        Assert.Equal(preview.Attributes.OrderBy(a => a.Name).ToArray(), emitted);
        var name = parsed.Descendants(saml + "NameID").Single();
        Assert.Equal(preview.NameId, name.Value);
        Assert.Equal(user.Id.ToString("N"), name.Value);
        Assert.Equal(preview.NameIdFormat, name.Attribute("Format")!.Value);
        Assert.Equal(preview.NameQualifier, name.Attribute("NameQualifier")!.Value);
        Assert.Equal(preview.SpNameQualifier, name.Attribute("SPNameQualifier")!.Value);
        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        document.LoadXml(xml);
        var signatures = document.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl).Cast<XmlElement>().ToArray();
        Assert.Equal(2, signatures.Length);
        foreach (var signature in signatures)
        {
            var verifier = new SignedXml((XmlElement)signature.ParentNode!);
            verifier.LoadXml(signature);
            Assert.True(verifier.CheckSignature(federation.Certificate, true));
        }
        Assert.Throws<SamlAttributeException>(() => federation.Protocol.CreateResponse(request, user.Id,
            DateTimeOffset.UtcNow, [new("NameID", "other")]));
        Assert.Throws<InvalidOperationException>(() => federation.Protocol.CreateResponse(request, user.Id,
            DateTimeOffset.UtcNow, [new(SamlAttributePolicy.SubjectName, "other")]));
    }

    internal static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    internal static SamlAttributeService CreateService(SamlIdpFixture federation, ApplicationDbContext db, IMemoryCache cache) =>
        new(federation.Runtime, new SettingsService(db, cache, new EphemeralDataProtectionProvider()), db);
}
