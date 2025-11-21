using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Core.Application.DTOs;
using Core.Application; // for IApplicationDbContext explicit access
using Core.Domain.Entities;
using Core.Domain; // if ApplicationUser lives in Core.Domain
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenIddict.Abstractions;
using Xunit;
using Infrastructure;
using Core.Domain.Events;

namespace Tests.Application.UnitTests;

/// <summary>
/// Integration-style test (in-memory EF + services) validating that optional scope claims
/// are absent when those scopes are not granted (partial grant scenario) prior to token issuance.
/// NOTE: This does not execute the full OpenIddict authorization pipeline but focuses on claim mapping behavior.
/// </summary>
public class PartialGrantIntegrationTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ScopeService _scopeService;

    public PartialGrantIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        var scopeMgr = new Mock<IOpenIddictScopeManager>();
        var appMgr = new Mock<IOpenIddictApplicationManager>();
        var publisher = new Mock<IDomainEventPublisher>();
        _scopeService = new ScopeService(scopeMgr.Object, appMgr.Object, _db, publisher.Object);
    }

    [Fact]
    public async Task PartialGrant_OptionalScopesUnchecked_ClaimsFromUncheckedScopesAbsent()
    {
        // Arrange requested scopes: required: openid; optional: profile, email
        var requestedScopes = new [] { "openid", "profile", "email" };
        var availableScopes = new []
        {
            new ScopeSummary { Name = "openid", IsRequired = true },
            new ScopeSummary { Name = "profile", IsRequired = false },
            new ScopeSummary { Name = "email", IsRequired = false }
        };
        // Simulate user only granting nothing (clicked continue with minimal) => grantedScopes null
        string[]? grantedScopes = null;

        // Seed claim definitions
        var claimProfileGiven = new UserClaim
        {
            Name = "given_name",
            DisplayName = "Given Name",
            ClaimType = "given_name",
            UserPropertyPath = "FirstName",
            DataType = "string",
            IsStandard = true,
            IsRequired = false
        };
        var claimEmail = new UserClaim
        {
            Name = "email",
            DisplayName = "Email",
            ClaimType = "email",
            UserPropertyPath = "Email",
            DataType = "string",
            IsStandard = true,
            IsRequired = false
        };
        ((IApplicationDbContext)_db).UserClaims.AddRange(claimProfileGiven, claimEmail);
        await _db.SaveChangesAsync();

        // Map scope->claim (these should only appear if their scope is allowed)
        _db.ScopeClaims.AddRange(
            new ScopeClaim { ScopeId = "profile-id", ScopeName = "profile", UserClaimId = claimProfileGiven.Id, AlwaysInclude = true },
            new ScopeClaim { ScopeId = "email-id", ScopeName = "email", UserClaimId = claimEmail.Id, AlwaysInclude = true }
        );
        await _db.SaveChangesAsync();

        // Application user with values present
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@example.local", FirstName = "Alice" };

        // Act: classify scopes and then emulate claim mapping query limited to allowed scopes
        var classification = _scopeService.ClassifyScopes(requestedScopes, availableScopes, grantedScopes);

        // Query mappings only for classification.Allowed scopes
        var allowedNames = classification.Allowed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mappings = await _db.ScopeClaims
            .Include(sc => sc.UserClaim)
            .Where(sc => allowedNames.Contains(sc.ScopeName))
            .ToListAsync();

        // Simulate AddScopeMappedClaimsAsync logic to build identity
        var identity = new ClaimsIdentity();
        foreach (var map in mappings)
        {
            var def = map.UserClaim!;
            var value = ResolveUserProperty(user, def.UserPropertyPath);
            if (identity.HasClaim(c => c.Type == def.ClaimType)) continue;
            if (string.IsNullOrEmpty(value) && !map.AlwaysInclude) continue;
            identity.AddClaim(new Claim(def.ClaimType, value ?? string.Empty));
        }

        // Assert: only required (openid) scope allowed thus no profile/email claims
        Assert.True(classification.IsPartialGrant);
        Assert.DoesNotContain("profile", classification.Allowed);
        Assert.DoesNotContain("email", classification.Allowed);
        Assert.Empty(identity.Claims.Where(c => c.Type == "given_name"));
        Assert.Empty(identity.Claims.Where(c => c.Type == "email"));
    }

    private static string? ResolveUserProperty(ApplicationUser user, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var prop = user.GetType().GetProperty(path);
        return prop?.GetValue(user)?.ToString();
    }

    public void Dispose() => _db.Dispose();
}
