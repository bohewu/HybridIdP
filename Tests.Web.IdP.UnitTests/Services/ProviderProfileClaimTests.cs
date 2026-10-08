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
using OpenIddict.Server;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Web.IdP.Services;
using Xunit;

namespace Tests.Web.IdP.UnitTests.Services;

public sealed class ProviderProfileClaimTests
{
    [Theory]
    [InlineData("[]", false)]
    [InlineData("[\"one\"]", false)]
    [InlineData("[\"two\",\"one\",\"one\"]", false)]
    [InlineData(null, false)]
    [InlineData("\"one\"", false)]
    [InlineData("[]", true)]
    [InlineData("[\"one\"]", true)]
    [InlineData("[\"two\",\"one\"]", true)]
    [InlineData(null, true)]
    [InlineData("\"one\"", true)]
    public async Task ArrayClaims_ShouldRoundTripSignedOrEncryptedTokensThroughValidationAndUserInfo(string? json, bool encrypted)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.ScopeClaims.Add(new ScopeClaim
        {
            ScopeId = "scope-profile", ScopeName = "profile", AlwaysInclude = true,
            ClaimDefinition = new ClaimDefinition
            {
                Name = "categories", DisplayName = "Categories", ClaimType = "categories",
                DataType = "StringArray", ProviderProfileSource = "source", UserPropertyPath = "categories"
            }
        });
        await db.SaveChangesAsync();
        var options = Microsoft.Extensions.Options.Options.Create(new ProviderProfileOptions
        {
            Enabled = true, Sources = new() { ["source"] = new() { AllowedProperties = new() { ["categories"] = "StringArray" } } }
        });
        var user = new ApplicationUser { Id = Guid.NewGuid() };
        var port = new Mock<IProviderProfileService>();
        var input = json is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(json);
        port.Setup(p => p.GetPropertiesAsync(user, "source", It.IsAny<CancellationToken>())).ReturnsAsync(
            input is { ValueKind: JsonValueKind.Array } array ? new Dictionary<string, JsonElement> { ["categories"] = array }
                : new Dictionary<string, JsonElement>());
        var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        var roles = new Mock<RoleManager<ApplicationRole>>(Mock.Of<IRoleStore<ApplicationRole>>(), null, null, null, null);
        var identity = new ClaimsIdentity("test");
        var enrichment = new ClaimsEnrichmentService(users.Object, roles.Object, db, NullLogger<ClaimsEnrichmentService>.Instance, port.Object, options);
        await enrichment.AddScopeMappedClaimsAsync(identity, user, []);
        Assert.Null(identity.FindFirst("categories"));
        await enrichment.AddScopeMappedClaimsAsync(identity, user, ["profile"]);
        if (input is { ValueKind: JsonValueKind.Array }) Assert.Equal(JsonClaimValueTypes.JsonArray, identity.FindFirst("categories")!.ValueType);
        // Simulate a historical scalar token: it must not be coerced by the new array mapping.
        if (input is { ValueKind: JsonValueKind.String }) identity.AddClaim(new Claim("categories", input.Value.GetString()!));
        identity.AddClaim(new Claim("sub", "fixture"));
        identity.AddClaim(new Claim("scope", "profile"));
        foreach (var marker in identity.FindAll(ClaimsEnrichmentService.ProfileClaimMarker).ToList()) identity.RemoveClaim(marker);
        var signingKey = new SymmetricSecurityKey(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var encryptionKey = new SymmetricSecurityKey(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var handler = new JsonWebTokenHandler { MapInboundClaims = false };
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = identity, Issuer = "https://issuer.example.test", Audience = "client", TokenType = "at+jwt",
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256),
            EncryptingCredentials = encrypted ? new EncryptingCredentials(encryptionKey,
                SecurityAlgorithms.Aes256KW, SecurityAlgorithms.Aes128CbcHmacSha256) : null,
            Claims = new Dictionary<string, object> { [OpenIddictConstants.Claims.Private.ClaimDestinationsMap] =
                new Dictionary<string, string[]> { ["categories"] = ["access_token", "identity_token"] } }
        });
        var validation = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "https://issuer.example.test", ValidAudience = "client", IssuerSigningKey = signingKey,
            TokenDecryptionKey = encryptionKey, ValidTypes = ["at+jwt"]
        });
        Assert.True(validation.IsValid, validation.Exception?.Message);
        var validatedToken = Assert.IsType<JsonWebToken>(validation.SecurityToken);
        var payload = validatedToken.InnerToken ?? validatedToken;
        if (input is { ValueKind: JsonValueKind.Array })
        {
            Assert.True(payload.TryGetPayloadValue("categories", out JsonElement encoded));
            Assert.Equal(JsonValueKind.Array, encoded.ValueKind);
        }
        var context = new OpenIddictServerEvents.ValidateTokenContext(new OpenIddictServerTransaction())
        {
            Principal = new ClaimsPrincipal(validation.ClaimsIdentity), TokenValidationResult = validation
        };
        await new RestoreProviderProfileArrayClaims(db, options).HandleAsync(context);
        var info = await new UserInfoService(db).GetUserInfoAsync(context.Principal!);
        if (input is { ValueKind: JsonValueKind.Array } expected)
        {
            var claim = Assert.Single(context.Principal!.FindAll("categories"));
            Assert.Equal(JsonClaimValueTypes.JsonArray, claim.ValueType);
            Assert.Contains("identity_token", claim.GetDestinations());
            Assert.Equal(ProviderProfileContract.NormalizeValue(expected).EnumerateArray().Select(item => item.GetString()),
                Assert.IsType<string[]>(info["categories"]));
            Assert.Equal(JsonValueKind.Array, JsonSerializer.SerializeToElement(info).GetProperty("categories").ValueKind);
        }
        else Assert.False(info.ContainsKey("categories"));
        context.Principal = new ClaimsPrincipal(new ClaimsIdentity());
        context.TokenValidationResult = new TokenValidationResult { IsValid = false };
        await new RestoreProviderProfileArrayClaims(db, options).HandleAsync(context);
        Assert.Empty(context.Principal.Claims);
    }

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
            Sources = new() { ["source"] = new() { AllowedProperties = new() { ["code"] = "String", ["categories"] = "StringArray" } } }
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
        var direct = await service.CreateClaimAsync(request with
        {
            Name = "categories", ClaimType = "categories", DataType = "StringArray", UserPropertyPath = "categories", Condition = null
        });
        Assert.Equal("StringArray", direct.DataType);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateClaimAsync(request with
        {
            Name = "local-array", ClaimType = "local-array", ProviderProfileSource = null,
            DataType = "StringArray", UserPropertyPath = "Department", Condition = null
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateClaimAsync(request with
        {
            Name = "wrong-array", ClaimType = "wrong-array", DataType = "String", UserPropertyPath = "categories", Condition = null
        }));
    }

    [Fact]
    public async Task SchemaEndpoint_ShouldExposeOnlyApprovedMetadataWithClaimsReadPermission()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new ClaimsService(db, profileOptions: Microsoft.Extensions.Options.Options.Create(new ProviderProfileOptions
        {
            Sources = new() { ["source"] = new()
            {
                Endpoint = "https://private.example.test/profile", SharedSecret = "synthetic-private-secret", ProviderNamespace = "private-provider",
                AllowedProperties = new() { ["categories"] = "StringArray", ["flag"] = "Boolean", ["code"] = "String" }
            } }
        }));
        var controller = new global::Web.IdP.Controllers.Admin.ClaimsController(service);
        var response = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(controller.GetProfileSources().Result);
        var dto = Assert.IsType<ProviderProfileSchemaDto>(response.Value);
        Assert.False(dto.Enabled);
        Assert.Equal("StringArray", Assert.Single(dto.Sources).Properties["categories"]);
        var json = JsonSerializer.Serialize(dto);
        Assert.DoesNotContain("private.example", json);
        Assert.DoesNotContain("synthetic-private-secret", json);
        Assert.DoesNotContain("private-provider", json);
        var method = typeof(global::Web.IdP.Controllers.Admin.ClaimsController).GetMethod("GetProfileSources")!;
        var permission = Assert.Single(method.GetCustomAttributes(typeof(Infrastructure.Authorization.HasPermissionAttribute), false)
            .Cast<Infrastructure.Authorization.HasPermissionAttribute>());
        Assert.Contains(Core.Domain.Constants.Permissions.Claims.Read, permission.Policy);
        Assert.NotEmpty(typeof(global::Web.IdP.Controllers.Admin.ClaimsController)
            .GetCustomAttributes(typeof(global::Web.IdP.Attributes.ApiAuthorizeAttribute), true));
    }

    private static ClaimCondition Rule(string prefix) => new()
    {
        Operator = "StartsWith", Property = "code", Value = JsonSerializer.SerializeToElement(prefix)
    };
}
