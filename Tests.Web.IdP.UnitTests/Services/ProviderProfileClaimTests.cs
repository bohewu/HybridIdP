using System.Security.Claims;
using System.Text.Json;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using Web.IdP.Services;
using Xunit;

namespace Tests.Web.IdP.UnitTests.Services;

public sealed class ProviderProfileClaimTests
{
    [Fact]
    public async Task Enrichment_ShouldReevaluateRulesRemoveStaleClaimsAndPreserveBooleanUserInfo()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var schema = new Dictionary<string, string> { ["code"] = "String" };
        var definition = new ClaimDefinition
        {
            Name = "classification", DisplayName = "Classification", ClaimType = "classification",
            DataType = "Boolean", UserPropertyPath = "", ProviderProfileSource = "source",
            ConditionJson = JsonSerializer.Serialize(Rule("4"))
        };
        db.ScopeClaims.Add(new ScopeClaim { ScopeId = "scope-profile", ScopeName = "profile", ClaimDefinition = definition, AlwaysInclude = true });
        db.ScopeClaims.Add(new ScopeClaim
        {
            ScopeId = "scope-profile", ScopeName = "profile", AlwaysInclude = true,
            ClaimDefinition = new ClaimDefinition
            {
                Name = "direct", DisplayName = "Direct", ClaimType = "direct", DataType = "String",
                ProviderProfileSource = "source", UserPropertyPath = "code"
            }
        });
        await db.SaveChangesAsync();
        var user = new ApplicationUser { Id = Guid.NewGuid() };
        var port = new Mock<IProviderProfileService>();
        port.Setup(p => p.GetPropertiesAsync(user, "source", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, JsonElement> { ["code"] = JsonSerializer.SerializeToElement("4123") });
        var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        var roles = new Mock<RoleManager<ApplicationRole>>(Mock.Of<IRoleStore<ApplicationRole>>(), null, null, null, null);
        var service = new ClaimsEnrichmentService(users.Object, roles.Object, db,
            NullLogger<ClaimsEnrichmentService>.Instance, port.Object,
            Microsoft.Extensions.Options.Options.Create(new ProviderProfileOptions
            {
                Enabled = true, Sources = new() { ["source"] = new() { AllowedProperties = schema } }
            }));
        var identity = new ClaimsIdentity("test");
        await service.AddScopeMappedClaimsAsync(identity, user, ["profile"]);
        Assert.Equal("true", identity.FindFirst("classification")!.Value);
        Assert.Equal(ClaimValueTypes.Boolean, identity.FindFirst("classification")!.ValueType);
        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes("profile");
        var userInfo = await new UserInfoService(db).GetUserInfoAsync(principal);
        Assert.True(Assert.IsType<bool>(userInfo["classification"]));

        // Same properties, new rule: a cached hash must not freeze the old derived result.
        definition.ConditionJson = JsonSerializer.Serialize(Rule("3"));
        await db.SaveChangesAsync();
        await service.AddScopeMappedClaimsAsync(identity, user, ["profile"]);
        Assert.Equal("false", identity.FindFirst("classification")!.Value);
        Assert.Single(identity.FindAll("classification"));
        port.Setup(p => p.GetPropertiesAsync(user, "source", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, JsonElement>());
        await service.AddScopeMappedClaimsAsync(identity, user, ["profile"]);
        Assert.Null(identity.FindFirst("classification"));
        var unavailableInfo = await new UserInfoService(db).GetUserInfoAsync(principal);
        Assert.False(unavailableInfo.ContainsKey("classification"));
        Assert.False(unavailableInfo.ContainsKey("direct"));

        // A private marker removes an old source claim even after its definition is deleted.
        identity.AddClaim(new Claim("classification", "true", ClaimValueTypes.Boolean));
        identity.AddClaim(new Claim(ClaimsEnrichmentService.ProfileClaimMarker, "[\"classification\"]"));
        await service.AddScopeMappedClaimsAsync(identity, user, []);
        Assert.Null(identity.FindFirst("classification"));
        Assert.Null(identity.FindFirst(ClaimsEnrichmentService.ProfileClaimMarker));
    }

    [Fact]
    public async Task Save_ShouldRejectUnapprovedInputsProtectedClaimsAndWrongTypes()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new ClaimsService(db, profileOptions: Microsoft.Extensions.Options.Options.Create(new ProviderProfileOptions
        {
            Sources = new() { ["source"] = new() { AllowedProperties = new() { ["code"] = "String" } } }
        }));
        var request = new CreateClaimRequest("classification", null, null, "classification", null, "Boolean", false, "source", Rule("4"));
        var saved = await service.CreateClaimAsync(request);
        Assert.NotNull(saved.Condition);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateClaimAsync(request with { Name = "role", ClaimType = "role" }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateClaimAsync(request with { Name = "wrong", DataType = "String" }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateClaimAsync(request with
        {
            Name = "unapproved", Condition = Rule("4") with { Property = "PasswordHash" }
        }));
    }

    private static ClaimCondition Rule(string prefix) => new()
    {
        Operator = "StartsWith", Property = "code", Value = JsonSerializer.SerializeToElement(prefix)
    };
}
