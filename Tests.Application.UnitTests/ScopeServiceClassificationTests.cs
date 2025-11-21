using System;
using System.Collections.Generic;
using System.Linq;
using Core.Application.DTOs;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenIddict.Abstractions;
using Xunit;
using Core.Domain.Events;
using Infrastructure;
using Core.Application;

namespace Tests.Application.UnitTests;

public class ScopeServiceClassificationTests : IDisposable
{
    private readonly Mock<IOpenIddictScopeManager> _mockScopeManager = new();
    private readonly Mock<IOpenIddictApplicationManager> _mockApplicationManager = new();
    private readonly Mock<IDomainEventPublisher> _mockEventPublisher = new();
    private readonly ApplicationDbContext _dbContext;
    private readonly ScopeService _service;

    public ScopeServiceClassificationTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);
        _service = new ScopeService(_mockScopeManager.Object, _mockApplicationManager.Object, _dbContext, _mockEventPublisher.Object);
    }

    [Fact]
    public void ClassifyScopes_NoGrantedScopes_ReturnsOnlyRequired()
    {
        var requested = new [] { "openid", "profile", "email" };
        var available = new []
        {
            new ScopeSummary { Name = "openid", IsRequired = true },
            new ScopeSummary { Name = "profile", IsRequired = false },
            new ScopeSummary { Name = "email", IsRequired = false }
        };

        var result = _service.ClassifyScopes(requested, available, null);

        Assert.Contains("openid", result.Allowed);
        Assert.DoesNotContain("profile", result.Allowed);
        Assert.DoesNotContain("email", result.Allowed);
        Assert.Equal(new [] { "openid" }, result.Required);
        Assert.True(result.IsPartialGrant); // profile & email rejected
        // Alphabetical ordering after OrderBy should be email, profile
        Assert.Equal(new [] { "email", "profile" }, result.Rejected.OrderBy(x => x));
    }

    [Fact]
    public void ClassifyScopes_GrantedOptionalScopes_IncludesRequiredAndGranted()
    {
        var requested = new [] { "openid", "profile", "email" };
        var available = new []
        {
            new ScopeSummary { Name = "openid", IsRequired = true },
            new ScopeSummary { Name = "profile", IsRequired = false },
            new ScopeSummary { Name = "email", IsRequired = false }
        };
        var granted = new [] { "profile" }; // user consented only profile

        var result = _service.ClassifyScopes(requested, available, granted);

        Assert.Equal(new [] { "openid", "profile" }.OrderBy(x => x), result.Allowed.OrderBy(x => x));
        Assert.Equal(new [] { "openid" }, result.Required);
        Assert.Equal(new [] { "email" }, result.Rejected);
        Assert.True(result.IsPartialGrant);
    }

    [Fact]
    public void ClassifyScopes_GrantedContainsNonRequestedScope_ExcludedFromAllowed()
    {
        var requested = new [] { "openid", "profile" };
        var available = new []
        {
            new ScopeSummary { Name = "openid", IsRequired = true },
            new ScopeSummary { Name = "profile", IsRequired = false },
            new ScopeSummary { Name = "email", IsRequired = false }
        };
        var granted = new [] { "profile", "email" }; // email was not requested

        var result = _service.ClassifyScopes(requested, available, granted);

        Assert.DoesNotContain("email", result.Allowed);
        Assert.Contains("profile", result.Allowed);
        Assert.Contains("openid", result.Allowed);
        Assert.Empty(result.Rejected); // all requested covered
        Assert.False(result.IsPartialGrant);
    }

    public void Dispose() => _dbContext.Dispose();
}
