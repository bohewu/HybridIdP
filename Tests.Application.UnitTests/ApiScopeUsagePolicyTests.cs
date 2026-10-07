using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Json;
using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Events;
using Infrastructure;
using Infrastructure.Authorization;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using Oidc = OpenIddict.Abstractions.OpenIddictConstants;

namespace Tests.Application.UnitTests;

public class ApiScopeUsagePolicyTests
{
    [Theory]
    [InlineData("admin", true)]
    [InlineData("resource-owner", true)]
    [InlineData("scope-owner", true)]
    [InlineData("visible", true)]
    [InlineData("hidden", false)]
    [InlineData("mixed", false)]
    [InlineData("unknown", false)]
    public async Task ScopeClaims_ShouldApplyCatalogVisibilityBeforeReturningMapping(string state, bool visible)
    {
        using var f = new Fixture();
        if (state == "unknown") f.ResourceNames = ["unknown-api"];
        else
        {
            await f.AddResourceAsync(f.ResourceOwner, visible: state is "visible" or "mixed");
            if (state == "mixed") await f.AddResourceAsync(Guid.NewGuid(), visible: false);
        }
        if (state == "scope-owner")
            f.Db.ScopeOwnerships.Add(new ScopeOwnership { ScopeId = f.ScopeId, CreatedByPersonId = f.ClientOwner });
        f.SetActor(state == "resource-owner" ? f.ResourceOwner : f.ClientOwner, admin: state == "admin");
        var claim = new ClaimDefinition { Name = "internal-claim", DisplayName = "Internal claim", ClaimType = "internal", UserPropertyPath = "name", DataType = "String" };
        f.Db.Set<ClaimDefinition>().Add(claim);
        f.Db.ScopeClaims.Add(new ScopeClaim
        {
            ScopeId = f.ScopeId, ScopeName = f.ScopeName, ClaimDefinition = claim, CustomMappingLogic = "internal-source"
        });
        await f.Db.SaveChangesAsync();
        var service = new ScopeService(f.Scopes.Object, f.Apps.Object, f.Db, Mock.Of<IDomainEventPublisher>(), f.Policy);

        if (visible)
            Assert.Equal("internal-source", Assert.Single((await service.GetScopeClaimsAsync(f.ScopeId)).claims).CustomMappingLogic);
        else await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetScopeClaimsAsync(f.ScopeId));
    }

    [Theory]
    [InlineData("create", "foreign", false)]
    [InlineData("update", "foreign", false)]
    [InlineData("replace", "foreign", false)]
    [InlineData("create", "open", true)]
    [InlineData("update", "open", true)]
    [InlineData("replace", "open", true)]
    [InlineData("create", "owner", true)]
    [InlineData("update", "owner-approved", true)]
    [InlineData("replace", "owner-approved", true)]
    [InlineData("create", "admin", true)]
    [InlineData("update", "admin-approved", true)]
    [InlineData("replace", "admin-approved", true)]
    [InlineData("create", "mixed", false)]
    [InlineData("update", "mixed", false)]
    [InlineData("replace", "mixed", false)]
    public async Task ScopeWrites_ShouldRequireEveryResourceApproval(string path, string state, bool allowed)
    {
        using var f = new Fixture();
        var resource = await f.AddResourceAsync(f.ResourceOwner, state == "open");
        if (state == "mixed") await f.AddResourceAsync(f.ClientOwner, true);
        if (state is "owner-approved" or "admin-approved")
        {
            f.SetActor(f.ResourceOwner, state == "admin-approved");
            await f.Policy.ApproveAsync(f.ApplicationId, f.ScopeId, resource.Id);
        }
        f.SetActor(state == "owner" ? f.ResourceOwner : f.ClientOwner, state == "admin");
        f.Writes = 0;
        var permissions = new List<string> { Oidc.Permissions.GrantTypes.ClientCredentials, Oidc.Permissions.Prefixes.Scope + f.ScopeName };
        var clients = new ClientService(f.Apps.Object, Mock.Of<IDomainEventPublisher>(), f.Db, f.Scopes.Object,
            Options.Create(new RedirectUriSecurityPolicyOptions()), f.Policy);
        var replacements = new ClientAllowedScopesService(f.Apps.Object, f.Scopes.Object, f.Db, f.Policy);
        Task Write() => path switch
        {
            "create" => clients.CreateClientAsync(new CreateClientRequest("new-client", "test-secret", null, null,
                Oidc.ClientTypes.Confidential, null, null, null, permissions, null)),
            "update" => clients.UpdateClientAsync(f.ApplicationId, new UpdateClientRequest(null, null, null, null, null,
                null, null, permissions, null)),
            _ => replacements.SetAllowedScopesAsync(f.ApplicationId, [f.ScopeName])
        };
        if (allowed)
        {
            await Write();
            Assert.Equal(1, f.Writes);
            Assert.True(await f.Policy.CanUseScopesAsync(f.Application, [f.ScopeName]));
        }
        else
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(Write);
            Assert.Equal(0, f.Writes);
            Assert.False(f.Stored.Properties.ContainsKey(ApiScopeUsagePolicy.ApprovalsProperty));
            Assert.Contains(Oidc.Permissions.Prefixes.Scope + f.ScopeName, f.Stored.Permissions); // Legacy row preserved.
        }
    }

    [Fact]
    public async Task Approval_ShouldBePerResourceAndFailClosedAfterOwnershipOrMappingChanges()
    {
        using var f = new Fixture();
        var first = await f.AddResourceAsync(f.ResourceOwner);
        var second = await f.AddResourceAsync(f.ClientOwner);
        f.SetActor(f.ResourceOwner);
        await f.Policy.ApproveAsync(f.ApplicationId, f.ScopeId, first.Id);
        Assert.False(await f.Policy.CanUseScopesAsync(f.Application, [f.ScopeName]));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Policy.ApproveAsync(f.ApplicationId, f.ScopeId, second.Id));
        f.SetActor(f.ClientOwner);
        await f.Policy.ApproveAsync(f.ApplicationId, f.ScopeId, second.Id);
        Assert.True(await f.Policy.CanUseScopesAsync(f.Application, [f.ScopeName]));
        second.OwnerPersonId = Guid.NewGuid();
        await f.Db.SaveChangesAsync();
        Assert.False(await f.Policy.CanUseScopesAsync(f.Application, [f.ScopeName]));
        second.OwnerPersonId = f.ClientOwner;
        await f.Db.SaveChangesAsync();
        await f.AddResourceAsync(Guid.NewGuid());
        Assert.False(await f.Policy.CanUseScopesAsync(f.Application, [f.ScopeName]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatalogAndPublicClassification_ShouldNeverGrantUsage(bool isPublic)
    {
        using var f = new Fixture();
        await f.AddResourceAsync(f.ResourceOwner, visible: true);
        f.Stored.Permissions.Remove(Oidc.Permissions.GrantTypes.ClientCredentials);
        f.Db.ScopeExtensions.Add(new ScopeExtension { ScopeId = f.ScopeId, IsPublic = isPublic });
        await f.Db.SaveChangesAsync();
        Assert.True(await f.Policy.CanViewScopeAsync(f.ScopeId));
        Assert.False(await f.Policy.CanUseScopesAsync(f.Application, [f.ScopeName]));
    }

    [Fact]
    public async Task NamedAndJoinMappings_ShouldBothRequireApprovalEvenForIdentityScope()
    {
        using var f = new Fixture("profile");
        f.Stored.Permissions.Remove(Oidc.Permissions.GrantTypes.ClientCredentials);
        await f.AddResourceAsync(f.ClientOwner, true);
        var named = new ApiResource { Name = "named-api", OwnerPersonId = f.ResourceOwner };
        f.Db.ApiResources.Add(named);
        await f.Db.SaveChangesAsync();
        f.ResourceNames = [named.Name];
        Assert.False(await f.Policy.CanUseScopesAsync(f.Application, [f.ScopeName]));
        f.SetActor(f.ResourceOwner);
        await f.Policy.ApproveAsync(f.ApplicationId, f.ScopeId, named.Id);
        Assert.True(await f.Policy.CanUseScopesAsync(f.Application, [f.ScopeName]));
    }

    [Fact]
    public async Task AdministrativeCeiling_ShouldNotApproveAnAdditionalApiMapping()
    {
        using var f = new Fixture("users.read");
        f.Stored.Properties[AdministrativeClientGrant.PermissionsProperty] = JsonSerializer.SerializeToElement(new[] { f.ScopeName });
        Assert.True(await f.Policy.CanUseScopesAsync(f.Application, [f.ScopeName]));
        var resource = await f.AddResourceAsync(f.ResourceOwner);
        f.SetActor(f.ResourceOwner, admin: true, bearer: true);
        Assert.False(await f.Policy.CanUseScopesAsync(f.Application, [f.ScopeName]));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Policy.PrepareClientScopesAsync(f.Stored));
        f.SetActor(null, admin: true);
        await f.Policy.ApproveAsync(f.ApplicationId, f.ScopeId, resource.Id);
        Assert.True(await f.Policy.CanUseScopesAsync(f.Application, [f.ScopeName]));
        f.Stored.Properties.Remove(AdministrativeClientGrant.PermissionsProperty);
        Assert.False(await f.Policy.CanUseScopesAsync(f.Application, [f.ScopeName]));
    }

    [Fact]
    public async Task UnknownOrOwnerlessScope_ShouldRequireExplicitAdminApprovalWithoutChangingPermissions()
    {
        using var f = new Fixture();
        f.ResourceNames = ["legacy-audience"];
        Assert.False(await f.Policy.CanUseScopesAsync(f.Application, [f.ScopeName]));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Policy.ApproveAsync(f.ApplicationId, f.ScopeId, null));
        var permissions = f.Stored.Permissions.ToArray();
        f.SetActor(null, admin: true);
        await f.Policy.ApproveAsync(f.ApplicationId, f.ScopeId, null);
        Assert.Equal(permissions, f.Stored.Permissions);
        Assert.True(await f.Policy.CanUseScopesAsync(f.Application, [f.ScopeName]));
        f.ResourceNames = ["different-audience"];
        Assert.False(await f.Policy.CanUseScopesAsync(f.Application, [f.ScopeName]));
    }

    [Fact]
    public async Task Actor_ShouldUseCurrentAccountPersonAndNeverTrustBearerAdminOrPersonClaims()
    {
        using var f = new Fixture();
        var resource = await f.AddResourceAsync(f.ResourceOwner);
        f.SetActor(f.ClientOwner, spoofedPerson: f.ResourceOwner);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Policy.ApproveAsync(f.ApplicationId, f.ScopeId, resource.Id));
        f.SetActor(f.ResourceOwner, admin: true, bearer: true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Policy.ApproveAsync(f.ApplicationId, f.ScopeId, resource.Id));
        Assert.False(f.Stored.Properties.ContainsKey(ApiScopeUsagePolicy.ApprovalsProperty));
    }

    [Fact]
    public async Task NewMapping_ShouldRejectForeignScopeBeforeAnyResourceWrite()
    {
        using var f = new Fixture();
        var service = new ApiResourceService(f.Db, f.Scopes.Object,
            Mock.Of<Microsoft.Extensions.Logging.ILogger<ApiResourceService>>(), f.Policy);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateResourceAsync(
            new CreateApiResourceRequest("my-open-api", null, null, null, [f.ScopeId], true, true), f.ClientOwner));
        Assert.Empty(f.Db.ApiResources);
    }

    [Fact]
    public async Task PartialPreflightDenial_ShouldNotPersistEarlierOwnerApproval()
    {
        using var f = new Fixture();
        await f.AddResourceAsync(f.ClientOwner);
        await f.AddResourceAsync(f.ResourceOwner);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Policy.PrepareClientScopesAsync(f.Stored));
        Assert.False(f.Stored.Properties.ContainsKey(ApiScopeUsagePolicy.ApprovalsProperty));
        Assert.Equal(0, f.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedAudienceAdoption_ShouldRejectForeignScopeBeforeAnyWrite(bool rename)
    {
        using var f = new Fixture();
        f.ResourceNames = ["foreign-audience"];
        f.Db.ScopeOwnerships.Add(new ScopeOwnership { ScopeId = f.ScopeId, CreatedByPersonId = f.ResourceOwner });
        f.Db.ScopeExtensions.Add(new ScopeExtension { ScopeId = f.ScopeId, IsPublic = true });
        f.Stored.Permissions.Remove(Oidc.Permissions.GrantTypes.ClientCredentials);
        var original = new ApiResource
        {
            Name = "owned-api", DisplayName = "Original", OwnerPersonId = f.ClientOwner,
            IsUsageOpen = true, IsCatalogVisible = true
        };
        if (rename)
        {
            original.Scopes.Add(new ApiResourceScope { ScopeId = "existing-owned-scope" });
            f.Db.ApiResources.Add(original);
        }
        await f.Db.SaveChangesAsync();
        var permissions = f.Stored.Permissions.ToArray();
        Assert.False(await f.Policy.CanUseScopesAsync(f.Application, [f.ScopeName]));
        var service = new ApiResourceService(f.Db, f.Scopes.Object,
            Mock.Of<Microsoft.Extensions.Logging.ILogger<ApiResourceService>>(), f.Policy);
        List<string> scopeIds = [];

        if (rename)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UpdateResourceAsync(original.Id,
                new UpdateApiResourceRequest("foreign-audience", "Changed", null, null, scopeIds, true, false)));
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateResourceAsync(
                new CreateApiResourceRequest("foreign-audience", "Changed", null, null, scopeIds, true, true)));

        Assert.False(f.Db.ChangeTracker.HasChanges());
        Assert.False(await f.Db.ApiResources.AnyAsync(r => r.Name == "foreign-audience"));
        Assert.Equal(rename ? 1 : 0, await f.Db.ApiResources.CountAsync());
        if (rename)
        {
            var persisted = await f.Db.ApiResources.AsNoTracking().SingleAsync();
            Assert.Equal("owned-api", persisted.Name);
            Assert.Equal("Original", persisted.DisplayName);
            Assert.Equal(f.ClientOwner, persisted.OwnerPersonId);
            Assert.True(persisted.IsUsageOpen);
            Assert.True(persisted.IsCatalogVisible);
            Assert.Null(persisted.UpdatedAt);
            Assert.Equal("existing-owned-scope", (await f.Db.ApiResourceScopes.AsNoTracking().SingleAsync()).ScopeId);
        }
        else
            Assert.Empty(await f.Db.ApiResourceScopes.ToListAsync());
        Assert.Equal(f.ResourceOwner, (await f.Db.ScopeOwnerships.AsNoTracking().SingleAsync()).CreatedByPersonId);
        Assert.True((await f.Db.ScopeExtensions.AsNoTracking().SingleAsync()).IsPublic);
        var scope = await f.Scopes.Object.FindByIdAsync(f.ScopeId);
        Assert.NotNull(scope);
        Assert.Equal(new[] { "foreign-audience" }, await f.Scopes.Object.GetResourcesAsync(scope));
        Assert.False(await f.Policy.CanUseScopesAsync(f.Application, [f.ScopeName]));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Policy.PrepareClientScopesAsync(f.Stored));
        Assert.Equal(permissions, f.Stored.Permissions);
        Assert.False(f.Stored.Properties.ContainsKey(ApiScopeUsagePolicy.ApprovalsProperty));
        Assert.Equal(0, f.Writes);
    }

    [Theory]
    [InlineData(false, "owner")]
    [InlineData(true, "owner")]
    [InlineData(false, "admin")]
    [InlineData(true, "admin")]
    [InlineData(false, "unused")]
    [InlineData(true, "unused")]
    public async Task NamedAudienceAdoption_ShouldAllowScopeAuthorityOrUnusedName(bool rename, string authority)
    {
        using var f = new Fixture();
        f.ResourceNames = ["existing-audience"];
        f.Db.ScopeOwnerships.Add(new ScopeOwnership { ScopeId = f.ScopeId, CreatedByPersonId = f.ResourceOwner });
        var actorPersonId = authority == "owner" ? f.ResourceOwner : f.ClientOwner;
        f.SetActor(actorPersonId, admin: authority == "admin");
        var original = new ApiResource { Name = "owned-api", OwnerPersonId = actorPersonId, IsUsageOpen = true };
        if (rename) f.Db.ApiResources.Add(original);
        await f.Db.SaveChangesAsync();
        var name = authority == "unused" ? "unused-audience" : "existing-audience";
        var service = new ApiResourceService(f.Db, f.Scopes.Object,
            Mock.Of<Microsoft.Extensions.Logging.ILogger<ApiResourceService>>(), f.Policy);

        if (rename)
            Assert.True(await service.UpdateResourceAsync(original.Id,
                new UpdateApiResourceRequest(name, null, null, null, [], true, true)));
        else
            await service.CreateResourceAsync(new CreateApiResourceRequest(name, null, null, null, [], true, true));

        var persisted = await f.Db.ApiResources.AsNoTracking().SingleAsync();
        Assert.Equal(name, persisted.Name);
        Assert.Equal(actorPersonId, persisted.OwnerPersonId);
        Assert.True(persisted.IsUsageOpen);
        Assert.Empty(await f.Db.ApiResourceScopes.ToListAsync());
        Assert.Equal(authority != "unused", await f.Policy.CanUseScopesAsync(f.Application, [f.ScopeName]));
    }

    [Fact]
    public async Task SameNamePolicyUpdate_ShouldAllowResourceOwnerWithExistingForeignScopeMapping()
    {
        using var f = new Fixture();
        var resource = await f.AddResourceAsync(f.ClientOwner);
        f.ResourceNames = [resource.Name];
        f.Db.ScopeOwnerships.Add(new ScopeOwnership { ScopeId = f.ScopeId, CreatedByPersonId = f.ResourceOwner });
        await f.Db.SaveChangesAsync();
        Assert.False(await f.Policy.CanUseScopesAsync(f.Application, [f.ScopeName]));
        var service = new ApiResourceService(f.Db, f.Scopes.Object,
            Mock.Of<Microsoft.Extensions.Logging.ILogger<ApiResourceService>>(), f.Policy);

        Assert.True(await service.UpdateResourceAsync(resource.Id,
            new UpdateApiResourceRequest(resource.Name, "Updated", null, null, [f.ScopeId], true, true)));

        Assert.True((await f.Db.ApiResources.AsNoTracking().SingleAsync()).IsUsageOpen);
        Assert.Equal(f.ScopeId, (await f.Db.ApiResourceScopes.AsNoTracking().SingleAsync()).ScopeId);
        Assert.True(await f.Policy.CanUseScopesAsync(f.Application, [f.ScopeName]));
    }

    private sealed class Fixture : IDisposable
    {
        public readonly ApplicationDbContext Db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public readonly Mock<IOpenIddictApplicationManager> Apps = new();
        public readonly Mock<IOpenIddictScopeManager> Scopes = new();
        private readonly Mock<IAdministrativeAuthorizationBoundary> _boundary = new();
        public readonly Guid ApplicationId = Guid.NewGuid();
        public readonly Guid ClientOwner = Guid.NewGuid();
        public readonly Guid ResourceOwner = Guid.NewGuid();
        public readonly string ScopeId = Guid.NewGuid().ToString();
        public readonly string ScopeName;
        public readonly object Application = new();
        public OpenIddictApplicationDescriptor Stored = new() { ClientId = "client", ClientType = Oidc.ClientTypes.Confidential };
        public ImmutableArray<string> ResourceNames = [];
        public int Writes;
        public ApiScopeUsagePolicy Policy { get; }

        public Fixture(string scopeName = "api:read")
        {
            ScopeName = scopeName;
            var scope = new object();
            Scopes.Setup(m => m.FindByNameAsync(scopeName, It.IsAny<CancellationToken>())).ReturnsAsync(scope);
            Scopes.Setup(m => m.FindByIdAsync(ScopeId, It.IsAny<CancellationToken>())).ReturnsAsync(scope);
            Scopes.Setup(m => m.GetNameAsync(scope, It.IsAny<CancellationToken>())).ReturnsAsync(scopeName);
            Scopes.Setup(m => m.GetIdAsync(scope, It.IsAny<CancellationToken>())).ReturnsAsync(ScopeId);
            Scopes.Setup(m => m.GetResourcesAsync(scope, It.IsAny<CancellationToken>())).ReturnsAsync(() => ResourceNames);
            Scopes.Setup(m => m.FindByResourceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string resource, CancellationToken _) => ResourceNames.Contains(resource, StringComparer.Ordinal)
                    ? new[] { scope }.ToAsyncEnumerable() : AsyncEnumerable.Empty<object>());
            Apps.Setup(m => m.FindByIdAsync(ApplicationId.ToString(), It.IsAny<CancellationToken>())).ReturnsAsync(Application);
            Apps.Setup(m => m.GetIdAsync(Application, It.IsAny<CancellationToken>())).ReturnsAsync(ApplicationId.ToString());
            Apps.Setup(m => m.GetPropertiesAsync(Application, It.IsAny<CancellationToken>())).ReturnsAsync(() => Stored.Properties.ToImmutableDictionary());
            Apps.Setup(m => m.GetPermissionsAsync(Application, It.IsAny<CancellationToken>())).ReturnsAsync(() => Stored.Permissions.ToImmutableArray());
            Apps.Setup(m => m.PopulateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), Application, It.IsAny<CancellationToken>()))
                .Callback<OpenIddictApplicationDescriptor, object, CancellationToken>((d, _, _) =>
                {
                    d.ClientId = Stored.ClientId; d.ClientType = Stored.ClientType;
                    foreach (var p in Stored.Permissions) d.Permissions.Add(p);
                    foreach (var p in Stored.Properties) d.Properties[p.Key] = p.Value;
                });
            Apps.Setup(m => m.UpdateAsync(Application, It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
                .Callback<object, OpenIddictApplicationDescriptor, CancellationToken>((_, d, _) => { Stored = d; Writes++; });
            Apps.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
                .Callback<OpenIddictApplicationDescriptor, CancellationToken>((d, _) => { Stored = d; Writes++; }).ReturnsAsync(Application);
            Stored.Permissions.Add(Oidc.Permissions.GrantTypes.ClientCredentials);
            Stored.Permissions.Add(Oidc.Permissions.Prefixes.Scope + scopeName);
            var authorization = new Mock<IAuthorizationService>();
            authorization.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(), It.IsAny<string>())).ReturnsAsync(AuthorizationResult.Success());
            Policy = new(Db, Scopes.Object, Apps.Object, _boundary.Object, authorization.Object);
            SetActor(ClientOwner);
        }

        public void SetActor(Guid? person, bool admin = false, bool bearer = false, Guid? spoofedPerson = null)
        {
            if (person.HasValue && !Db.Persons.Any(p => p.Id == person)) Db.Persons.Add(new Person { Id = person.Value });
            var user = new ApplicationUser { Id = Guid.NewGuid(), PersonId = person };
            Db.Users.Add(user); Db.SaveChanges();
            var identity = new ClaimsIdentity(bearer ? "Bearer" : IdentityConstants.ApplicationScheme);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
            if (admin) identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
            if (spoofedPerson.HasValue) identity.AddClaim(new Claim("person_id", spoofedPerson.ToString()!));
            _boundary.Setup(b => b.ResolveAsync()).ReturnsAsync(new AdministrativeAuthority(new(identity), bearer,
                Core.Domain.Constants.Permissions.GetAll().ToHashSet(StringComparer.Ordinal)));
        }

        public async Task<ApiResource> AddResourceAsync(Guid owner, bool open = false, bool visible = false)
        {
            var resource = new ApiResource { Name = "api-" + Guid.NewGuid(), OwnerPersonId = owner, IsUsageOpen = open, IsCatalogVisible = visible };
            resource.Scopes.Add(new ApiResourceScope { ScopeId = ScopeId });
            Db.ApiResources.Add(resource); await Db.SaveChangesAsync();
            return resource;
        }

        public void Dispose() => Db.Dispose();
    }
}
