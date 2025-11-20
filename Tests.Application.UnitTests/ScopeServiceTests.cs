using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Core.Domain.Events;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenIddict.Abstractions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.Application.UnitTests;

public class ScopeServiceTests : IDisposable
{
    private readonly Mock<IOpenIddictScopeManager> _mockScopeManager;
    private readonly Mock<IOpenIddictApplicationManager> _mockApplicationManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IDomainEventPublisher> _mockEventPublisher;
    private readonly ScopeService _scopeService;

    public ScopeServiceTests()
    {
        _mockScopeManager = new Mock<IOpenIddictScopeManager>();
        _mockApplicationManager = new Mock<IOpenIddictApplicationManager>();
        _mockEventPublisher = new Mock<IDomainEventPublisher>();
        
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        _dbContext = new ApplicationDbContext(options);
        
        _scopeService = new ScopeService(_mockScopeManager.Object, _mockApplicationManager.Object, _dbContext, _mockEventPublisher.Object);
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
    }

    #region GetScopesAsync Tests

    [Fact]
    public async Task GetScopesAsync_ShouldReturnAllScopes_WhenNoFiltersApplied()
    {
        // Arrange
        _dbContext.ScopeExtensions.Add(new ScopeExtension 
        { 
            ScopeId = "scope1", 
            ConsentDisplayNameKey = "Consent 1", 
            DisplayOrder = 1 
        });
        await _dbContext.SaveChangesAsync();

        var scopes = new List<object>
        {
            new { Id = "scope1", Name = "openid" }
        };
        
        _mockScopeManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(scopes));
        _mockScopeManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("scope1");
        _mockScopeManager.Setup(m => m.GetNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("openid");
        _mockScopeManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("OpenID");
        _mockScopeManager.Setup(m => m.GetDescriptionAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("OpenID scope");
        _mockScopeManager.Setup(m => m.GetResourcesAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray.Create("resource1"));

        // Act
        var (items, totalCount) = await _scopeService.GetScopesAsync(0, 25, null, null);

        // Assert
        Assert.Single(items);
        Assert.Equal(1, totalCount);
        var scope = items.First();
        Assert.Equal("scope1", scope.Id);
        Assert.Equal("openid", scope.Name);
    }

    [Fact]
    public async Task GetScopesAsync_ShouldFilterBySearch_WhenSearchProvided()
    {
        // Arrange
        // No ScopeExtensions needed for this test

        var scopes = new List<object>
        {
            new { Id = "scope1", Name = "openid" },
            new { Id = "scope2", Name = "profile" }
        };
        
        var index = 0;
        _mockScopeManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(scopes));
        _mockScopeManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"scope{++index}");
        _mockScopeManager.Setup(m => m.GetNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object scope, CancellationToken _) => 
                scope.ToString()!.Contains("scope1") ? "openid" : "profile");
        _mockScopeManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Display");
        _mockScopeManager.Setup(m => m.GetDescriptionAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Description");
        _mockScopeManager.Setup(m => m.GetResourcesAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act
        var (items, totalCount) = await _scopeService.GetScopesAsync(0, 25, "openid", null);

        // Assert
        Assert.Single(items);
        Assert.Equal(1, totalCount);
        Assert.Contains(items, s => s.Name.Contains("openid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetScopesAsync_ShouldApplyPagination_WhenSkipAndTakeProvided()
    {
        // Arrange
        // No ScopeExtensions needed for this test

        var scopes = Enumerable.Range(1, 30).Select(i => new { Id = $"scope{i}", Name = $"scope{i}" }).ToList();
        
        var index = 0;
        _mockScopeManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(scopes.Cast<object>()));
        _mockScopeManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"scope{++index}");
        _mockScopeManager.Setup(m => m.GetNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object scope, CancellationToken _) => scope.ToString());
        _mockScopeManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Display");
        _mockScopeManager.Setup(m => m.GetDescriptionAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Description");
        _mockScopeManager.Setup(m => m.GetResourcesAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act
        var (items, totalCount) = await _scopeService.GetScopesAsync(10, 5, null, null);

        // Assert
        Assert.Equal(5, items.Count());
        Assert.Equal(30, totalCount);
    }

    [Fact]
    public async Task GetScopesAsync_ShouldSortAscending_WhenSortParameterIsNameAsc()
    {
        // Arrange
        // No ScopeExtensions needed for this test

        var scopes = new List<object>
        {
            new { Id = "scope1", Name = "zeta" },
            new { Id = "scope2", Name = "alpha" }
        };
        
        var names = new Queue<string>(new[] { "zeta", "alpha" });
        _mockScopeManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(scopes));
        _mockScopeManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Guid.NewGuid().ToString());
        _mockScopeManager.Setup(m => m.GetNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => names.Dequeue());
        _mockScopeManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Display");
        _mockScopeManager.Setup(m => m.GetDescriptionAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Description");
        _mockScopeManager.Setup(m => m.GetResourcesAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act
        var (items, totalCount) = await _scopeService.GetScopesAsync(0, 25, null, "name:asc");

        // Assert
        var itemList = items.ToList();
        Assert.Equal("alpha", itemList[0].Name);
        Assert.Equal("zeta", itemList[1].Name);
    }

    [Fact]
    public async Task GetScopesAsync_ShouldReturnAll_WhenSearchWhitespace()
    {
        // Arrange
        var scopes = new List<object>
        {
            new { Id = "scope1", Name = "openid" },
            new { Id = "scope2", Name = "profile" }
        };

        var names = new Queue<string>(new[] { "openid", "profile" });
        _mockScopeManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(scopes));
        _mockScopeManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Guid.NewGuid().ToString());
        _mockScopeManager.Setup(m => m.GetNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => names.Dequeue());
        _mockScopeManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Display");
        _mockScopeManager.Setup(m => m.GetDescriptionAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Desc");
        _mockScopeManager.Setup(m => m.GetResourcesAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act
        var (items, totalCount) = await _scopeService.GetScopesAsync(0, 10, "   ", null);

        // Assert
        Assert.Equal(2, totalCount);
        Assert.Equal(2, items.Count());
    }

    [Fact]
    public async Task GetScopesAsync_ShouldSortByNameDesc()
    {
        // Arrange
        var scopes = new List<object>
        {
            new { Id = "scope1", Name = "alpha" },
            new { Id = "scope2", Name = "zeta" },
            new { Id = "scope3", Name = "beta" }
        };
        var names = new Queue<string>(new[] { "alpha", "zeta", "beta" });
        _mockScopeManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(scopes));
        _mockScopeManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Guid.NewGuid().ToString());
        _mockScopeManager.Setup(m => m.GetNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => names.Dequeue());
        _mockScopeManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Display");
        _mockScopeManager.Setup(m => m.GetDescriptionAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Desc");
        _mockScopeManager.Setup(m => m.GetResourcesAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act
        var (items, _) = await _scopeService.GetScopesAsync(0, 25, null, "name:desc");

        // Assert
        var ordered = items.Select(i => i.Name).ToList();
        Assert.Equal(new[] { "zeta", "beta", "alpha" }, ordered);
    }

    [Fact]
    public async Task GetScopesAsync_ShouldSortByDisplayNameAscDesc()
    {
        // Arrange
        var scopes = new List<object>
        {
            new { Id = "scope1" },
            new { Id = "scope2" },
            new { Id = "scope3" }
        };
        var ids = new Queue<string>(new[] { "scope1", "scope2", "scope3" });
        var displayAsc = new Queue<string>(new[] { "Zulu", "alpha", "Bravo" });
        _mockScopeManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(scopes));
        _mockScopeManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ids.Dequeue());
        _mockScopeManager.Setup(m => m.GetNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("name");
        _mockScopeManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => displayAsc.Dequeue());
        _mockScopeManager.Setup(m => m.GetDescriptionAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Desc");
        _mockScopeManager.Setup(m => m.GetResourcesAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act ASC
        var (ascItems, _) = await _scopeService.GetScopesAsync(0, 10, null, "displayName:asc");
        var ascList = ascItems.Select(i => i.DisplayName).ToList();
        Assert.Equal(new[] { "alpha", "Bravo", "Zulu" }, ascList);

        // Re-setup for DESC
        var ids2 = new Queue<string>(new[] { "scope1", "scope2", "scope3" });
        var displayDesc = new Queue<string>(new[] { "Zulu", "alpha", "Bravo" });
        _mockScopeManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ids2.Dequeue());
        _mockScopeManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => displayDesc.Dequeue());

        var (descItems, _) = await _scopeService.GetScopesAsync(0, 10, null, "displayName:desc");
        var descList = descItems.Select(i => i.DisplayName).ToList();
        Assert.Equal(new[] { "Zulu", "Bravo", "alpha" }, descList);
    }

    [Fact]
    public async Task GetScopesAsync_ShouldSortByDescriptionAscDesc()
    {
        // Arrange
        var scopes = new List<object>
        {
            new { Id = "scope1" },
            new { Id = "scope2" },
            new { Id = "scope3" }
        };
        var ids = new Queue<string>(new[] { "scope1", "scope2", "scope3" });
        var descriptions = new Queue<string>(new[] { "Zulu desc", "alpha desc", "Bravo desc" });
        _mockScopeManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(scopes));
        _mockScopeManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ids.Dequeue());
        _mockScopeManager.Setup(m => m.GetNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("name");
        _mockScopeManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("dsp");
        _mockScopeManager.Setup(m => m.GetDescriptionAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => descriptions.Dequeue());
        _mockScopeManager.Setup(m => m.GetResourcesAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act ASC
        var (ascItems, _) = await _scopeService.GetScopesAsync(0, 10, null, "description:asc");
        var ascList = ascItems.Select(i => i.Description).ToList();
        Assert.Equal(new[] { "alpha desc", "Bravo desc", "Zulu desc" }, ascList);

        // Re-setup for DESC
        var ids2 = new Queue<string>(new[] { "scope1", "scope2", "scope3" });
        var descriptions2 = new Queue<string>(new[] { "Zulu desc", "alpha desc", "Bravo desc" });
        _mockScopeManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ids2.Dequeue());
        _mockScopeManager.Setup(m => m.GetDescriptionAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => descriptions2.Dequeue());

        var (descItems, _) = await _scopeService.GetScopesAsync(0, 10, null, "description:desc");
        var descList = descItems.Select(i => i.Description).ToList();
        Assert.Equal(new[] { "Zulu desc", "Bravo desc", "alpha desc" }, descList);
    }

    [Fact]
    public async Task GetScopesAsync_ShouldFallbackToNameAsc_WhenUnknownSortField()
    {
        // Arrange
        var scopes = new List<object>
        {
            new { Id = "scope1", Name = "zeta" },
            new { Id = "scope2", Name = "alpha" },
            new { Id = "scope3", Name = "beta" }
        };
        var names = new Queue<string>(new[] { "zeta", "alpha", "beta" });
        _mockScopeManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(scopes));
        _mockScopeManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Guid.NewGuid().ToString());
        _mockScopeManager.Setup(m => m.GetNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => names.Dequeue());
        _mockScopeManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Display");
        _mockScopeManager.Setup(m => m.GetDescriptionAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Desc");
        _mockScopeManager.Setup(m => m.GetResourcesAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act
        var (items, _) = await _scopeService.GetScopesAsync(0, 25, null, "unknown:asc");

        // Assert
        var ordered = items.Select(i => i.Name).ToList();
        Assert.Equal(new[] { "alpha", "beta", "zeta" }, ordered);
    }

    [Fact]
    public async Task GetScopesAsync_ShouldApplyDefaultPaging_WhenSkipNegativeOrTakeNonPositive()
    {
        // Arrange
        var scopes = Enumerable.Range(1, 40).Select(i => new { Id = $"scope{i}", Name = $"scope{i}" }).ToList();
        var idx = 0;
        _mockScopeManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(scopes));
        _mockScopeManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"scope{++idx}");
        _mockScopeManager.Setup(m => m.GetNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object s, CancellationToken _) => s.ToString()!);
        _mockScopeManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Display");
        _mockScopeManager.Setup(m => m.GetDescriptionAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Desc");
        _mockScopeManager.Setup(m => m.GetResourcesAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act
        var (items, total) = await _scopeService.GetScopesAsync(-10, 0, null, null);

        // Assert
        Assert.Equal(40, total);
        Assert.Equal(25, items.Count());
    }

    #endregion

    #region GetScopeByIdAsync Tests

    [Fact]
    public async Task GetScopeByIdAsync_ShouldReturnScope_WhenScopeExists()
    {
        // Arrange
        _dbContext.ScopeExtensions.Add(new ScopeExtension 
        { 
            ScopeId = "scope1", 
            ConsentDisplayNameKey = "Consent Name" 
        });
        await _dbContext.SaveChangesAsync();

        var scope = new { Id = "scope1" };
        _mockScopeManager.Setup(m => m.FindByIdAsync("scope1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);
        _mockScopeManager.Setup(m => m.GetIdAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("scope1");
        _mockScopeManager.Setup(m => m.GetNameAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("openid");
        _mockScopeManager.Setup(m => m.GetDisplayNameAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("OpenID");
        _mockScopeManager.Setup(m => m.GetDescriptionAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("OpenID scope");
        _mockScopeManager.Setup(m => m.GetResourcesAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray.Create("resource1"));

        // Act
        var result = await _scopeService.GetScopeByIdAsync("scope1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("scope1", result.Id);
        Assert.Equal("openid", result.Name);
        Assert.Equal("Consent Name", result.ConsentDisplayNameKey);
    }

    [Fact]
    public async Task GetScopeByIdAsync_ShouldReturnNull_WhenScopeDoesNotExist()
    {
        // Arrange
        _mockScopeManager.Setup(m => m.FindByIdAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Act
        var result = await _scopeService.GetScopeByIdAsync("nonexistent");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region CreateScopeAsync Tests

    [Fact]
    public async Task CreateScopeAsync_ShouldCreateScope_WithDefaultResource()
    {
        // Arrange
        var request = new CreateScopeRequest(
            Name: "newscope",
            DisplayName: "New Scope",
            Description: "A new scope",
            Resources: null
        );

        var scope = new { Id = "newscope" };
        _mockScopeManager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictScopeDescriptor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);
        _mockScopeManager.Setup(m => m.GetIdAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("newscope");

        // Act
        var result = await _scopeService.CreateScopeAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("newscope", result.Id);
        Assert.Equal("newscope", result.Name);
        _mockScopeManager.Verify(m => m.CreateAsync(
            It.Is<OpenIddictScopeDescriptor>(d => 
                d.Name == "newscope" && 
                d.Resources.Contains(AuthConstants.Resources.ResourceServer)), 
            It.IsAny<CancellationToken>()), Times.Once);
        _mockEventPublisher.Verify(x => x.PublishAsync(It.Is<ScopeCreatedEvent>(e => e.ScopeName == "newscope")), Times.Once);
    }

    [Fact]
    public async Task CreateScopeAsync_ShouldCreateScopeExtension_WhenConsentFieldsProvided()
    {
        // Arrange
        var request = new CreateScopeRequest(
            Name: "newscope",
            DisplayName: "New Scope",
            Description: null,
            Resources: null,
            ConsentDisplayNameKey: "Consent Display",
            ConsentDescriptionKey: "Consent Description",
            IconUrl: "icon.png",
            IsRequired: true,
            DisplayOrder: 5,
            Category: "Custom"
        );

        var scope = new { Id = "newscope" };
        _mockScopeManager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictScopeDescriptor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);
        _mockScopeManager.Setup(m => m.GetIdAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("newscope");

        // Act
        var result = await _scopeService.CreateScopeAsync(request);

        // Assert
        Assert.NotNull(result);
        var extension = await _dbContext.ScopeExtensions.FirstOrDefaultAsync(e => e.ScopeId == "newscope");
        Assert.NotNull(extension);
        Assert.Equal("Consent Display", extension.ConsentDisplayNameKey);
        Assert.Equal("Consent Description", extension.ConsentDescriptionKey);
        Assert.Equal("icon.png", extension.IconUrl);
        Assert.True(extension.IsRequired);
        Assert.Equal(5, extension.DisplayOrder);
        Assert.Equal("Custom", extension.Category);
        _mockEventPublisher.Verify(x => x.PublishAsync(It.Is<ScopeCreatedEvent>(e => e.ScopeName == "newscope")), Times.Once);
    }

    [Fact]
    public async Task CreateScopeAsync_ShouldThrow_WhenDuplicateName()
    {
        // Arrange
        var existing = new { Id = "dup" };
        _mockScopeManager.Setup(m => m.FindByNameAsync("dup", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var request = new CreateScopeRequest("dup", null, null, null);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _scopeService.CreateScopeAsync(request));
    }

    [Fact]
    public async Task CreateScopeAsync_ShouldUseExplicitResources()
    {
        // Arrange
        var request = new CreateScopeRequest("exp", "Exp", null, new List<string> { "r1", "r2" });
        var created = new { Id = "exp" };
        _mockScopeManager.Setup(m => m.FindByNameAsync("exp", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);
        _mockScopeManager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictScopeDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<OpenIddictScopeDescriptor, CancellationToken>((d, _) =>
            {
                Assert.Contains("r1", d.Resources);
                Assert.Contains("r2", d.Resources);
                Assert.DoesNotContain(AuthConstants.Resources.ResourceServer, d.Resources); // should not add default when explicit provided
            })
            .ReturnsAsync(created);
        _mockScopeManager.Setup(m => m.GetIdAsync(created, It.IsAny<CancellationToken>()))
            .ReturnsAsync("exp");

        // Act
        var result = await _scopeService.CreateScopeAsync(request);

        // Assert
        Assert.Equal("exp", result.Id);
        Assert.Equal(2, result.Resources.Count);
    }

    #endregion

    #region UpdateScopeAsync Tests

    [Fact]
    public async Task UpdateScopeAsync_ShouldReturnFalse_WhenScopeDoesNotExist()
    {
        // Arrange
        _mockScopeManager.Setup(m => m.FindByIdAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        var request = new UpdateScopeRequest(Name: "updated", null, null, null);

        // Act
        var result = await _scopeService.UpdateScopeAsync("nonexistent", request);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task UpdateScopeAsync_ShouldUpdateScope_WhenScopeExists()
    {
        // Arrange
        _dbContext.ScopeExtensions.Add(new ScopeExtension 
        { 
            ScopeId = "scope1", 
            ConsentDisplayNameKey = "Old Consent" 
        });
        await _dbContext.SaveChangesAsync();

        var scope = new { Id = "scope1" };
        _mockScopeManager.Setup(m => m.FindByIdAsync("scope1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);
        _mockScopeManager.Setup(m => m.GetNameAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("oldname");
        _mockScopeManager.Setup(m => m.GetDisplayNameAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Old Display");
        _mockScopeManager.Setup(m => m.GetDescriptionAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Old Description");
        _mockScopeManager.Setup(m => m.GetResourcesAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray.Create("resource1"));

        var request = new UpdateScopeRequest(
            Name: "newname",
            DisplayName: "New Display",
            Description: null,
            Resources: null,
            ConsentDisplayNameKey: "New Consent",
            ConsentDescriptionKey: null,
            IconUrl: null,
            IsRequired: null,
            DisplayOrder: null,
            Category: null
        );

        // Act
        var result = await _scopeService.UpdateScopeAsync("scope1", request);

        // Assert
        Assert.True(result);
        _mockScopeManager.Verify(m => m.UpdateAsync(scope, It.IsAny<CancellationToken>()), Times.Once);
        var extension = await _dbContext.ScopeExtensions.FirstOrDefaultAsync(e => e.ScopeId == "scope1");
        Assert.NotNull(extension);
        Assert.Equal("New Consent", extension.ConsentDisplayNameKey);
        _mockEventPublisher.Verify(x => x.PublishAsync(It.Is<ScopeUpdatedEvent>(e => e.ScopeName == "oldname")), Times.Once);
    }

    [Fact]
    public async Task UpdateScopeAsync_ShouldReplaceResources()
    {
        // Arrange
        var scope = new { Id = "scopeX" };
        _mockScopeManager.Setup(m => m.FindByIdAsync("scopeX", It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);
        _mockScopeManager.Setup(m => m.GetNameAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("old-name");
        _mockScopeManager.Setup(m => m.GetDisplayNameAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Old Display");
        _mockScopeManager.Setup(m => m.GetDescriptionAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Old Desc");
        _mockScopeManager.Setup(m => m.GetResourcesAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray.Create("oldr1"));

        OpenIddictScopeDescriptor? captured = null;
        _mockScopeManager.Setup(m => m.PopulateAsync(scope, It.IsAny<OpenIddictScopeDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<object, OpenIddictScopeDescriptor, CancellationToken>((_, d, __) => captured = d)
            .Returns(ValueTask.CompletedTask);
        _mockScopeManager.Setup(m => m.UpdateAsync(scope, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var request = new UpdateScopeRequest("new-name", null, null, new List<string> { "rA", "rB" });

        // Act
        var result = await _scopeService.UpdateScopeAsync("scopeX", request);

        // Assert
        Assert.True(result);
        Assert.NotNull(captured);
        Assert.Contains("rA", captured!.Resources);
        Assert.Contains("rB", captured!.Resources);
        Assert.DoesNotContain("oldr1", captured!.Resources);
    }

    [Fact]
    public async Task UpdateScopeAsync_ShouldPartiallyUpdateConsentFields()
    {
        // Arrange
        _dbContext.ScopeExtensions.Add(new ScopeExtension { ScopeId = "scopeY", ConsentDisplayNameKey = "Orig", IconUrl = "orig.png", DisplayOrder = 1, Category = "CatA" });
        await _dbContext.SaveChangesAsync();
        var scope = new { Id = "scopeY" };
        _mockScopeManager.Setup(m => m.FindByIdAsync("scopeY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);
        _mockScopeManager.Setup(m => m.GetNameAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("scopeY");
        _mockScopeManager.Setup(m => m.GetDisplayNameAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("DispY");
        _mockScopeManager.Setup(m => m.GetDescriptionAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("DescY");
        _mockScopeManager.Setup(m => m.GetResourcesAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray.Create("resY"));
        _mockScopeManager.Setup(m => m.PopulateAsync(scope, It.IsAny<OpenIddictScopeDescriptor>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        _mockScopeManager.Setup(m => m.UpdateAsync(scope, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var request = new UpdateScopeRequest(null, null, null, null, null, null, IconUrl: "new.png", IsRequired: null, DisplayOrder: 9, Category: "CatB");

        // Act
        var result = await _scopeService.UpdateScopeAsync("scopeY", request);

        // Assert
        Assert.True(result);
        var ext = await _dbContext.ScopeExtensions.FirstAsync(e => e.ScopeId == "scopeY");
        Assert.Equal("new.png", ext.IconUrl);
        Assert.Equal(9, ext.DisplayOrder);
        Assert.Equal("CatB", ext.Category);
        Assert.Equal("Orig", ext.ConsentDisplayNameKey); // unchanged
    }

    #endregion

    #region DeleteScopeAsync Tests

    [Fact]
    public async Task DeleteScopeAsync_ShouldReturnFalse_WhenScopeDoesNotExist()
    {
        // Arrange
        _mockScopeManager.Setup(m => m.FindByNameAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Act
        var result = await _scopeService.DeleteScopeAsync("nonexistent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteScopeAsync_ShouldReturnFalse_WhenScopeIsInUse()
    {
        // Arrange
        var scope = new { Id = "scope1" };
        _mockScopeManager.Setup(m => m.FindByNameAsync("scope1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);

        var apps = new List<object> { new { Id = "app1" } };
        _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(apps));
        _mockApplicationManager.Setup(m => m.GetPermissionsAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray.Create($"{OpenIddictConstants.Permissions.Prefixes.Scope}scope1"));

        // Act
        var result = await _scopeService.DeleteScopeAsync("scope1");

        // Assert
        Assert.False(result);
        _mockScopeManager.Verify(m => m.DeleteAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteScopeAsync_ShouldDeleteScope_WhenNotInUse()
    {
        // Arrange
        _dbContext.ScopeExtensions.Add(new ScopeExtension { ScopeId = "scope1" });
        await _dbContext.SaveChangesAsync();

        var scope = new { Id = "scope1" };
        _mockScopeManager.Setup(m => m.FindByNameAsync("scope1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);
        _mockScopeManager.Setup(m => m.GetIdAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("scope1");

        var apps = new List<object>();
        _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(apps));

        // Act
        var result = await _scopeService.DeleteScopeAsync("scope1");

        // Assert
        Assert.True(result);
        _mockScopeManager.Verify(m => m.DeleteAsync(scope, It.IsAny<CancellationToken>()), Times.Once);
        var extension = await _dbContext.ScopeExtensions.FirstOrDefaultAsync(e => e.ScopeId == "scope1");
        Assert.Null(extension);
        _mockEventPublisher.Verify(x => x.PublishAsync(It.Is<ScopeDeletedEvent>(e => e.ScopeName == "scope1")), Times.Once);
    }

    [Fact]
    public async Task DeleteScopeAsync_ShouldReturnFalse_OnDeleteException()
    {
        // Arrange
        var scope = new { Id = "scopeE" };
        _mockScopeManager.Setup(m => m.FindByNameAsync("scopeE", It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);
        _mockScopeManager.Setup(m => m.GetIdAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("scopeE");
        // No clients using scope
        _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(Array.Empty<object>()));
        _mockScopeManager.Setup(m => m.DeleteAsync(scope, It.IsAny<CancellationToken>()))
            .Throws(new Exception("boom"));

        // Act
        var result = await _scopeService.DeleteScopeAsync("scopeE");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Scope Claims Tests (GetScopeClaimsAsync & UpdateScopeClaimsAsync)

    [Fact]
    public async Task GetScopeClaimsAsync_ShouldThrowKeyNotFoundException_WhenScopeNotFound()
    {
        // Arrange
        _mockScopeManager.Setup(m => m.FindByIdAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _scopeService.GetScopeClaimsAsync("nonexistent"));
    }

    [Fact]
    public async Task GetScopeClaimsAsync_ShouldReturnEmptyList_WhenScopeHasNoClaims()
    {
        // Arrange
        var scopeObj = new { Id = "scope1", Name = "openid" };
        _mockScopeManager.Setup(m => m.FindByIdAsync("scope1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(scopeObj);
        _mockScopeManager.Setup(m => m.GetNameAsync(scopeObj, It.IsAny<CancellationToken>()))
            .ReturnsAsync("openid");

        // Act
        var result = await _scopeService.GetScopeClaimsAsync("scope1");

        // Assert
        Assert.Equal("scope1", result.scopeId);
        Assert.Equal("openid", result.scopeName);
        Assert.Empty(result.claims);
    }

    [Fact]
    public async Task GetScopeClaimsAsync_ShouldReturnClaims_WithCorrectDtoMapping()
    {
        // Arrange
        var scopeObj = new { Id = "scope1", Name = "profile" };
        _mockScopeManager.Setup(m => m.FindByIdAsync("scope1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(scopeObj);
        _mockScopeManager.Setup(m => m.GetNameAsync(scopeObj, It.IsAny<CancellationToken>()))
            .ReturnsAsync("profile");

        var claim = new UserClaim
        {
            Id = 1,
            Name = "name",
            DisplayName = "Full Name",
            ClaimType = "name",
            UserPropertyPath = "name",
            DataType = "String",
            IsRequired = false
        };
        _dbContext.Set<UserClaim>().Add(claim);

        var scopeClaim = new ScopeClaim
        {
            Id = 1,
            ScopeId = "scope1",
            ScopeName = "profile",
            UserClaimId = 1,
            AlwaysInclude = true,
            CustomMappingLogic = null
        };
        _dbContext.ScopeClaims.Add(scopeClaim);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _scopeService.GetScopeClaimsAsync("scope1");

        // Assert
        Assert.Single(result.claims);
        var dto = result.claims.First();
        Assert.Equal(1, dto.Id);
        Assert.Equal("scope1", dto.ScopeId);
        Assert.Equal("profile", dto.ScopeName);
        Assert.Equal(1, dto.ClaimId);
        Assert.Equal("name", dto.ClaimName);
        Assert.Equal("Full Name", dto.ClaimDisplayName);
        Assert.Equal("name", dto.ClaimType);
        Assert.True(dto.AlwaysInclude);
        Assert.Null(dto.CustomMappingLogic);
    }

    #endregion

    #region UpdateScopeClaimsAsync Tests

    [Fact]
    public async Task UpdateScopeClaimsAsync_ShouldThrowKeyNotFoundException_WhenScopeNotFound()
    {
        // Arrange
        _mockScopeManager.Setup(m => m.FindByIdAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        var request = new UpdateScopeClaimsRequest(new List<int> { 1 });

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _scopeService.UpdateScopeClaimsAsync("nonexistent", request));
    }

    [Fact]
    public async Task UpdateScopeClaimsAsync_ShouldThrowArgumentException_WhenClaimNotFound()
    {
        // Arrange
        var scopeObj = new { Id = "scope1", Name = "profile" };
        _mockScopeManager.Setup(m => m.FindByIdAsync("scope1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(scopeObj);
        _mockScopeManager.Setup(m => m.GetNameAsync(scopeObj, It.IsAny<CancellationToken>()))
            .ReturnsAsync("profile");

        var request = new UpdateScopeClaimsRequest(new List<int> { 999 });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _scopeService.UpdateScopeClaimsAsync("scope1", request));
        Assert.Contains("Claim with ID 999 not found", ex.Message);
    }

    [Fact]
    public async Task UpdateScopeClaimsAsync_ShouldRemoveOldClaims_AndAddNewOnes()
    {
        // Arrange
        var scopeObj = new { Id = "scope1", Name = "profile" };
        _mockScopeManager.Setup(m => m.FindByIdAsync("scope1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(scopeObj);
        _mockScopeManager.Setup(m => m.GetNameAsync(scopeObj, It.IsAny<CancellationToken>()))
            .ReturnsAsync("profile");

        // Add existing claim
        var oldClaim = new UserClaim
        {
            Id = 1,
            Name = "old_claim",
            DisplayName = "Old Claim",
            ClaimType = "old_claim",
            UserPropertyPath = "old_claim",
            DataType = "String",
            IsRequired = false
        };
        _dbContext.Set<UserClaim>().Add(oldClaim);

        var oldScopeClaim = new ScopeClaim
        {
            ScopeId = "scope1",
            ScopeName = "profile",
            UserClaimId = 1,
            AlwaysInclude = false
        };
        _dbContext.ScopeClaims.Add(oldScopeClaim);

        // Add new claim
        var newClaim = new UserClaim
        {
            Id = 2,
            Name = "new_claim",
            DisplayName = "New Claim",
            ClaimType = "new_claim",
            UserPropertyPath = "new_claim",
            DataType = "String",
            IsRequired = true
        };
        _dbContext.Set<UserClaim>().Add(newClaim);
        await _dbContext.SaveChangesAsync();

        var request = new UpdateScopeClaimsRequest(new List<int> { 2 });

        // Act
        var result = await _scopeService.UpdateScopeClaimsAsync("scope1", request);

        // Assert
        Assert.Single(result.claims);
        Assert.Equal(2, result.claims.First().ClaimId);
        Assert.Equal("new_claim", result.claims.First().ClaimName);
        
        // Verify old claim removed
        var remainingClaims = _dbContext.ScopeClaims.Where(sc => sc.ScopeId == "scope1").ToList();
        Assert.Single(remainingClaims);
        Assert.Equal(2, remainingClaims.First().UserClaimId);
        _mockEventPublisher.Verify(x => x.PublishAsync(It.Is<ScopeClaimChangedEvent>(e => e.ScopeName == "profile")), Times.Once);
    }

    [Fact]
    public async Task UpdateScopeClaimsAsync_ShouldSetAlwaysInclude_FromClaimIsRequired()
    {
        // Arrange
        var scopeObj = new { Id = "scope1", Name = "openid" };
        _mockScopeManager.Setup(m => m.FindByIdAsync("scope1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(scopeObj);
        _mockScopeManager.Setup(m => m.GetNameAsync(scopeObj, It.IsAny<CancellationToken>()))
            .ReturnsAsync("openid");

        var requiredClaim = new UserClaim
        {
            Id = 1,
            Name = "sub",
            DisplayName = "Subject",
            ClaimType = "sub",
            UserPropertyPath = "sub",
            DataType = "String",
            IsRequired = true  // Required claim
        };
        _dbContext.Set<UserClaim>().Add(requiredClaim);
        await _dbContext.SaveChangesAsync();

        var request = new UpdateScopeClaimsRequest(new List<int> { 1 });

        // Act
        var result = await _scopeService.UpdateScopeClaimsAsync("scope1", request);

        // Assert
        Assert.Single(result.claims);
        Assert.True(result.claims.First().AlwaysInclude); // Should be true because IsRequired is true
    }

    [Fact]
    public async Task UpdateScopeClaimsAsync_ShouldAllowEmptyClaimsList()
    {
        // Arrange
        var scopeObj = new { Id = "scope1", Name = "profile" };
        _mockScopeManager.Setup(m => m.FindByIdAsync("scope1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(scopeObj);
        _mockScopeManager.Setup(m => m.GetNameAsync(scopeObj, It.IsAny<CancellationToken>()))
            .ReturnsAsync("profile");

        // Add existing claim
        var claim = new UserClaim
        {
            Id = 1,
            Name = "name",
            DisplayName = "Name",
            ClaimType = "name",
            UserPropertyPath = "name",
            DataType = "String",
            IsRequired = false
        };
        _dbContext.Set<UserClaim>().Add(claim);

        var scopeClaim = new ScopeClaim
        {
            ScopeId = "scope1",
            ScopeName = "profile",
            UserClaimId = 1,
            AlwaysInclude = false
        };
        _dbContext.ScopeClaims.Add(scopeClaim);
        await _dbContext.SaveChangesAsync();

        var request = new UpdateScopeClaimsRequest(null); // Empty list

        // Act
        var result = await _scopeService.UpdateScopeClaimsAsync("scope1", request);

        // Assert
        Assert.Empty(result.claims);
        
        // Verify all claims removed
        var remainingClaims = _dbContext.ScopeClaims.Where(sc => sc.ScopeId == "scope1").ToList();
        Assert.Empty(remainingClaims);
    }

    #endregion

    // Helper method to create async enumerable for mocking
    private IAsyncEnumerable<object> CreateAsyncEnumerable(IEnumerable<object> items)
    {
        return new AsyncEnumerable(items);
    }

    private class AsyncEnumerable : IAsyncEnumerable<object>
    {
        private readonly IEnumerable<object> _items;

        public AsyncEnumerable(IEnumerable<object> items)
        {
            _items = items;
        }

        public IAsyncEnumerator<object> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new AsyncEnumerator(_items.GetEnumerator());
        }
    }

    private class AsyncEnumerator : IAsyncEnumerator<object>
    {
        private readonly IEnumerator<object> _enumerator;

        public AsyncEnumerator(IEnumerator<object> enumerator)
        {
            _enumerator = enumerator;
        }

        public object Current => _enumerator.Current;

        public ValueTask DisposeAsync()
        {
            _enumerator.Dispose();
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> MoveNextAsync()
        {
            return new ValueTask<bool>(_enumerator.MoveNext());
        }
    }
}
