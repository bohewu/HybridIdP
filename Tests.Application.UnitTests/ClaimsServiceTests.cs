using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Tests.Application.UnitTests;

public class ClaimsServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IApplicationDbContext _dbInterface;
    private readonly IClaimsService _claimsService;

    public ClaimsServiceTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        _dbContext = new ApplicationDbContext(options);
        _dbInterface = _dbContext;
        
        _claimsService = new ClaimsService(_dbInterface);
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
    }

    #region GetClaimsAsync Tests

    [Fact]
    public async Task GetClaimsAsync_ShouldReturnAllClaims_WhenNoFiltersApplied()
    {
        // Arrange
        _dbInterface.UserClaims.Add(new UserClaim
        {
            Name = "email",
            DisplayName = "Email Address",
            ClaimType = "email",
            UserPropertyPath = "Email",
            DataType = "String",
            IsStandard = true,
            IsRequired = false
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var (items, totalCount) = await _claimsService.GetClaimsAsync(skip: 0, take: 20, search: null, sortBy: "name", sortDirection: "asc");

        // Assert
        Assert.Single(items);
        Assert.Equal(1, totalCount);
        var claim = items.First();
        Assert.Equal("email", claim.Name);
        Assert.Equal("Email Address", claim.DisplayName);
        Assert.Equal(0, claim.ScopeCount);
    }

    [Fact]
    public async Task GetClaimsAsync_ShouldFilterBySearch_WhenSearchProvided()
    {
        // Arrange
        _dbInterface.UserClaims.AddRange(
            new UserClaim
            {
                Name = "email",
                DisplayName = "Email Address",
                Description = "User email",
                ClaimType = "email",
                UserPropertyPath = "Email",
                DataType = "String",
                IsStandard = true,
                IsRequired = false
            },
            new UserClaim
            {
                Name = "department",
                DisplayName = "Department",
                Description = "User department",
                ClaimType = "department",
                UserPropertyPath = "Department",
                DataType = "String",
                IsStandard = false,
                IsRequired = false
            });
        await _dbContext.SaveChangesAsync();

        // Act
        var (items, totalCount) = await _claimsService.GetClaimsAsync(skip: 0, take: 20, search: "email", sortBy: "name", sortDirection: "asc");

        // Assert
        Assert.Single(items);
        Assert.Equal(1, totalCount);
        Assert.Equal("email", items.First().Name);
    }

    [Fact]
    public async Task GetClaimsAsync_ShouldSortByDisplayName_WhenSortByDisplayName()
    {
        // Arrange
        _dbInterface.UserClaims.AddRange(
            new UserClaim
            {
                Name = "email",
                DisplayName = "Zebra Email",
                ClaimType = "email",
                UserPropertyPath = "Email",
                DataType = "String",
                IsStandard = true,
                IsRequired = false
            },
            new UserClaim
            {
                Name = "name",
                DisplayName = "Alpha Name",
                ClaimType = "name",
                UserPropertyPath = "Name",
                DataType = "String",
                IsStandard = true,
                IsRequired = false
            });
        await _dbContext.SaveChangesAsync();

        // Act
        var (items, totalCount) = await _claimsService.GetClaimsAsync(skip: 0, take: 20, search: null, sortBy: "displayname", sortDirection: "asc");

        // Assert
        Assert.Equal(2, items.Count());
        Assert.Equal("Alpha Name", items.First().DisplayName);
        Assert.Equal("Zebra Email", items.Last().DisplayName);
    }

    [Fact]
    public async Task GetClaimsAsync_ShouldSortDescending_WhenSortDirectionDesc()
    {
        // Arrange
        _dbInterface.UserClaims.AddRange(
            new UserClaim
            {
                Name = "aaa",
                DisplayName = "AAA",
                ClaimType = "aaa",
                UserPropertyPath = "AAA",
                DataType = "String",
                IsStandard = false,
                IsRequired = false
            },
            new UserClaim
            {
                Name = "zzz",
                DisplayName = "ZZZ",
                ClaimType = "zzz",
                UserPropertyPath = "ZZZ",
                DataType = "String",
                IsStandard = false,
                IsRequired = false
            });
        await _dbContext.SaveChangesAsync();

        // Act
        var (items, totalCount) = await _claimsService.GetClaimsAsync(skip: 0, take: 20, search: null, sortBy: "name", sortDirection: "desc");

        // Assert
        Assert.Equal(2, items.Count());
        Assert.Equal("zzz", items.First().Name);
        Assert.Equal("aaa", items.Last().Name);
    }

    [Fact]
    public async Task GetClaimsAsync_ShouldApplyPagination_WhenSkipAndTakeProvided()
    {
        // Arrange
        for (int i = 1; i <= 5; i++)
        {
            _dbInterface.UserClaims.Add(new UserClaim
            {
                Name = $"claim{i}",
                DisplayName = $"Claim {i}",
                ClaimType = $"claim{i}",
                UserPropertyPath = $"Claim{i}",
                DataType = "String",
                IsStandard = false,
                IsRequired = false
            });
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var (items, totalCount) = await _claimsService.GetClaimsAsync(skip: 2, take: 2, search: null, sortBy: "name", sortDirection: "asc");

        // Assert
        Assert.Equal(2, items.Count());
        Assert.Equal(5, totalCount);
        Assert.Equal("claim3", items.First().Name);
    }

    [Fact]
    public async Task GetClaimsAsync_ShouldIncludeScopeCount_WhenClaimHasScopeClaims()
    {
        // Arrange
        var claim = new UserClaim
        {
            Name = "email",
            DisplayName = "Email",
            ClaimType = "email",
            UserPropertyPath = "Email",
            DataType = "String",
            IsStandard = true,
            IsRequired = false
        };
        _dbInterface.UserClaims.Add(claim);
        await _dbContext.SaveChangesAsync();

        _dbInterface.ScopeClaims.AddRange(
            new ScopeClaim { ScopeId = "scope1", ScopeName = "Scope1", UserClaimId = claim.Id, AlwaysInclude = true },
            new ScopeClaim { ScopeId = "scope2", ScopeName = "Scope2", UserClaimId = claim.Id, AlwaysInclude = false }
        );
        await _dbContext.SaveChangesAsync();

        // Act
        var (items, totalCount) = await _claimsService.GetClaimsAsync(skip: 0, take: 20, search: null, sortBy: "name", sortDirection: "asc");

        // Assert
        Assert.Single(items);
        Assert.Equal(2, items.First().ScopeCount);
    }

    #endregion

    #region GetClaimByIdAsync Tests

    [Fact]
    public async Task GetClaimByIdAsync_ShouldReturnClaim_WhenClaimExists()
    {
        // Arrange
        var claim = new UserClaim
        {
            Name = "email",
            DisplayName = "Email Address",
            Description = "User's email address",
            ClaimType = "email",
            UserPropertyPath = "Email",
            DataType = "String",
            IsStandard = true,
            IsRequired = true
        };
        _dbInterface.UserClaims.Add(claim);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _claimsService.GetClaimByIdAsync(claim.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("email", result.Name);
        Assert.Equal("Email Address", result.DisplayName);
        Assert.True(result.IsStandard);
        Assert.True(result.IsRequired);
    }

    [Fact]
    public async Task GetClaimByIdAsync_ShouldReturnNull_WhenClaimDoesNotExist()
    {
        // Act
        var result = await _claimsService.GetClaimByIdAsync(999);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetClaimByIdAsync_ShouldIncludeScopeCount_WhenClaimExists()
    {
        // Arrange
        var claim = new UserClaim
        {
            Name = "profile",
            DisplayName = "Profile",
            ClaimType = "profile",
            UserPropertyPath = "Profile",
            DataType = "String",
            IsStandard = true,
            IsRequired = false
        };
        _dbInterface.UserClaims.Add(claim);
        await _dbContext.SaveChangesAsync();

        _dbInterface.ScopeClaims.Add(new ScopeClaim
        {
            ScopeId = "scope1",
            ScopeName = "Scope1",
            UserClaimId = claim.Id,
            AlwaysInclude = true
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _claimsService.GetClaimByIdAsync(claim.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.ScopeCount);
    }

    #endregion

    #region CreateClaimAsync Tests

    [Fact]
    public async Task CreateClaimAsync_ShouldCreateClaim_WhenValidRequest()
    {
        // Arrange
        var request = new CreateClaimRequest(
            Name: "department",
            DisplayName: "Department",
            Description: "User department",
            ClaimType: "department",
            UserPropertyPath: "Department",
            DataType: "String",
            IsRequired: false
        );

        // Act
        var result = await _claimsService.CreateClaimAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("department", result.Name);
        Assert.Equal("Department", result.DisplayName);
        Assert.Equal("User department", result.Description);
        Assert.False(result.IsStandard);
        Assert.False(result.IsRequired);
        Assert.Equal(0, result.ScopeCount);

        var savedClaim = await _dbInterface.UserClaims.FirstOrDefaultAsync(c => c.Name == "department");
        Assert.NotNull(savedClaim);
    }

    [Fact]
    public async Task CreateClaimAsync_ShouldUseDefaults_WhenOptionalFieldsNull()
    {
        // Arrange
        var request = new CreateClaimRequest(
            Name: "custom_claim",
            DisplayName: null,
            Description: null,
            ClaimType: "custom",
            UserPropertyPath: null,
            DataType: null,
            IsRequired: null
        );

        // Act
        var result = await _claimsService.CreateClaimAsync(request);

        // Assert
        Assert.Equal("custom_claim", result.DisplayName); // Default to Name
        Assert.Equal("custom_claim", result.UserPropertyPath); // Default to Name
        Assert.Equal("String", result.DataType); // Default to String
        Assert.False(result.IsRequired); // Default to false
    }

    [Fact]
    public async Task CreateClaimAsync_ShouldThrowInvalidOperationException_WhenNameAlreadyExists()
    {
        // Arrange
        _dbInterface.UserClaims.Add(new UserClaim
        {
            Name = "email",
            DisplayName = "Email",
            ClaimType = "email",
            UserPropertyPath = "Email",
            DataType = "String",
            IsStandard = true,
            IsRequired = false
        });
        await _dbContext.SaveChangesAsync();

        var request = new CreateClaimRequest(
            Name: "email",
            DisplayName: "Duplicate Email",
            Description: null,
            ClaimType: "email",
            UserPropertyPath: null,
            DataType: null,
            IsRequired: null
        );

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _claimsService.CreateClaimAsync(request));
        Assert.Contains("already exists", exception.Message);
    }

    [Fact]
    public async Task CreateClaimAsync_ShouldThrowArgumentException_WhenNameIsEmpty()
    {
        // Arrange
        var request = new CreateClaimRequest(
            Name: "",
            DisplayName: "Test",
            Description: null,
            ClaimType: "test",
            UserPropertyPath: null,
            DataType: null,
            IsRequired: null
        );

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _claimsService.CreateClaimAsync(request));
    }

    [Fact]
    public async Task CreateClaimAsync_ShouldThrowArgumentException_WhenClaimTypeIsEmpty()
    {
        // Arrange
        var request = new CreateClaimRequest(
            Name: "test",
            DisplayName: "Test",
            Description: null,
            ClaimType: "",
            UserPropertyPath: null,
            DataType: null,
            IsRequired: null
        );

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _claimsService.CreateClaimAsync(request));
    }

    #endregion

    #region UpdateClaimAsync Tests

    [Fact]
    public async Task UpdateClaimAsync_ShouldUpdateClaim_WhenValidRequest()
    {
        // Arrange
        var claim = new UserClaim
        {
            Name = "department",
            DisplayName = "Department",
            ClaimType = "department",
            UserPropertyPath = "Department",
            DataType = "String",
            IsStandard = false,
            IsRequired = false
        };
        _dbInterface.UserClaims.Add(claim);
        await _dbContext.SaveChangesAsync();

        var request = new UpdateClaimRequest(
            DisplayName: "Department Name",
            Description: "Updated description",
            ClaimType: "dept",
            UserPropertyPath: "DeptName",
            DataType: "String",
            IsRequired: true
        );

        // Act
        var result = await _claimsService.UpdateClaimAsync(claim.Id, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Department Name", result.DisplayName);
        Assert.Equal("Updated description", result.Description);
        Assert.Equal("dept", result.ClaimType);
        Assert.Equal("DeptName", result.UserPropertyPath);
        Assert.True(result.IsRequired);
    }

    [Fact]
    public async Task UpdateClaimAsync_ShouldThrowKeyNotFoundException_WhenClaimDoesNotExist()
    {
        // Arrange
        var request = new UpdateClaimRequest(
            DisplayName: "Test",
            Description: null,
            ClaimType: null,
            UserPropertyPath: null,
            DataType: null,
            IsRequired: null
        );

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _claimsService.UpdateClaimAsync(999, request));
    }

    [Fact]
    public async Task UpdateClaimAsync_ShouldThrowInvalidOperationException_WhenUpdatingStandardClaimCoreProperties()
    {
        // Arrange
        var claim = new UserClaim
        {
            Name = "email",
            DisplayName = "Email",
            ClaimType = "email",
            UserPropertyPath = "Email",
            DataType = "String",
            IsStandard = true,
            IsRequired = false
        };
        _dbInterface.UserClaims.Add(claim);
        await _dbContext.SaveChangesAsync();

        var request = new UpdateClaimRequest(
            DisplayName: "Updated Email",
            Description: "Updated description",
            ClaimType: "new_email", // Trying to change core property
            UserPropertyPath: "NewEmail",
            DataType: "String",
            IsRequired: true
        );

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _claimsService.UpdateClaimAsync(claim.Id, request));
        Assert.Contains("standard", exception.Message.ToLower());
    }

    [Fact]
    public async Task UpdateClaimAsync_ShouldAllowDisplayNameAndDescriptionUpdate_ForStandardClaims()
    {
        // Arrange
        var claim = new UserClaim
        {
            Name = "email",
            DisplayName = "Email",
            ClaimType = "email",
            UserPropertyPath = "Email",
            DataType = "String",
            IsStandard = true,
            IsRequired = false
        };
        _dbInterface.UserClaims.Add(claim);
        await _dbContext.SaveChangesAsync();

        var request = new UpdateClaimRequest(
            DisplayName: "Email Address (Updated)",
            Description: "User's primary email",
            ClaimType: null, // Not updating core properties
            UserPropertyPath: null,
            DataType: null,
            IsRequired: null
        );

        // Act
        var result = await _claimsService.UpdateClaimAsync(claim.Id, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Email Address (Updated)", result.DisplayName);
        Assert.Equal("User's primary email", result.Description);
        Assert.Equal("email", result.ClaimType); // Unchanged
        Assert.True(result.IsStandard);
    }

    [Fact]
    public async Task UpdateClaimAsync_ShouldOnlyUpdateProvidedFields_WhenPartialUpdate()
    {
        // Arrange
        var claim = new UserClaim
        {
            Name = "department",
            DisplayName = "Department",
            Description = "Original description",
            ClaimType = "department",
            UserPropertyPath = "Department",
            DataType = "String",
            IsStandard = false,
            IsRequired = false
        };
        _dbInterface.UserClaims.Add(claim);
        await _dbContext.SaveChangesAsync();

        var request = new UpdateClaimRequest(
            DisplayName: "Updated Department",
            Description: null, // Not updating description
            ClaimType: null,
            UserPropertyPath: null,
            DataType: null,
            IsRequired: null
        );

        // Act
        var result = await _claimsService.UpdateClaimAsync(claim.Id, request);

        // Assert
        Assert.Equal("Updated Department", result.DisplayName);
        Assert.Equal("Original description", result.Description); // Unchanged
    }

    #endregion

    #region DeleteClaimAsync Tests

    [Fact]
    public async Task DeleteClaimAsync_ShouldDeleteClaim_WhenValidRequest()
    {
        // Arrange
        var claim = new UserClaim
        {
            Name = "custom_claim",
            DisplayName = "Custom Claim",
            ClaimType = "custom",
            UserPropertyPath = "Custom",
            DataType = "String",
            IsStandard = false,
            IsRequired = false
        };
        _dbInterface.UserClaims.Add(claim);
        await _dbContext.SaveChangesAsync();

        // Act
        await _claimsService.DeleteClaimAsync(claim.Id);

        // Assert
        var deletedClaim = await _dbInterface.UserClaims.FindAsync(claim.Id);
        Assert.Null(deletedClaim);
    }

    [Fact]
    public async Task DeleteClaimAsync_ShouldThrowKeyNotFoundException_WhenClaimDoesNotExist()
    {
        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _claimsService.DeleteClaimAsync(999));
    }

    [Fact]
    public async Task DeleteClaimAsync_ShouldThrowInvalidOperationException_WhenDeletingStandardClaim()
    {
        // Arrange
        var claim = new UserClaim
        {
            Name = "email",
            DisplayName = "Email",
            ClaimType = "email",
            UserPropertyPath = "Email",
            DataType = "String",
            IsStandard = true,
            IsRequired = false
        };
        _dbInterface.UserClaims.Add(claim);
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _claimsService.DeleteClaimAsync(claim.Id));
        Assert.Contains("standard", exception.Message.ToLower());
    }

    [Fact]
    public async Task DeleteClaimAsync_ShouldThrowInvalidOperationException_WhenClaimIsUsedByScopes()
    {
        // Arrange
        var claim = new UserClaim
        {
            Name = "department",
            DisplayName = "Department",
            ClaimType = "department",
            UserPropertyPath = "Department",
            DataType = "String",
            IsStandard = false,
            IsRequired = false
        };
        _dbInterface.UserClaims.Add(claim);
        await _dbContext.SaveChangesAsync();

        _dbInterface.ScopeClaims.Add(new ScopeClaim
        {
            ScopeId = "scope1",
            ScopeName = "Scope1",
            UserClaimId = claim.Id,
            AlwaysInclude = true
        });
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _claimsService.DeleteClaimAsync(claim.Id));
        Assert.Contains("used by", exception.Message.ToLower());
    }

    #endregion
}
