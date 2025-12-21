using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using System.Linq.Expressions;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

/// <summary>
/// Unit tests for SecurityPolicyService business rule validations.
/// </summary>
public class SecurityPolicyServiceTests
{
    private readonly Mock<IApplicationDbContext> _mockDb;
    private readonly Mock<IMemoryCache> _mockCache;
    private readonly Mock<ILogger<SecurityPolicyService>> _mockLogger;

    public SecurityPolicyServiceTests()
    {
        _mockDb = new Mock<IApplicationDbContext>();
        _mockCache = new Mock<IMemoryCache>();
        _mockLogger = new Mock<ILogger<SecurityPolicyService>>();
    }

    [Fact]
    public async Task UpdatePolicyAsync_EnforceMfaWithoutAnyMfaMethod_ThrowsInvalidOperationException()
    {
        // Arrange - Validation happens BEFORE DB access, so we don't need to mock DbSet
        var policyDto = new SecurityPolicyDto
        {
            EnforceMandatoryMfaEnrollment = true,
            EnableTotpMfa = false,
            EnableEmailMfa = false,
            EnablePasskey = false,
            MinPasswordLength = 8
        };

        var service = new SecurityPolicyService(_mockDb.Object, _mockCache.Object, _mockLogger.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdatePolicyAsync(policyDto, "TestUser"));

        Assert.Contains("MFA", exception.Message);
        Assert.Contains("TOTP", exception.Message);
        Assert.Contains("Passkey", exception.Message);
    }

    [Fact]
    public async Task UpdatePolicyAsync_EnforceMfaWithTotpEnabled_DoesNotThrow()
    {
        // Arrange
        var policyDto = new SecurityPolicyDto
        {
            EnforceMandatoryMfaEnrollment = true,
            EnableTotpMfa = true,   // TOTP is enabled
            EnableEmailMfa = false,
            EnablePasskey = false,
            MinPasswordLength = 8
        };

        SetupDbSetMock();
        
        var service = new SecurityPolicyService(_mockDb.Object, _mockCache.Object, _mockLogger.Object);

        // Act - Should not throw
        await service.UpdatePolicyAsync(policyDto, "TestUser");

        // Assert
        _mockDb.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdatePolicyAsync_EnforceMfaWithEmailEnabled_DoesNotThrow()
    {
        // Arrange
        var policyDto = new SecurityPolicyDto
        {
            EnforceMandatoryMfaEnrollment = true,
            EnableTotpMfa = false,
            EnableEmailMfa = true,  // Email is enabled
            EnablePasskey = false,
            MinPasswordLength = 8
        };

        SetupDbSetMock();
        
        var service = new SecurityPolicyService(_mockDb.Object, _mockCache.Object, _mockLogger.Object);

        // Act - Should not throw
        await service.UpdatePolicyAsync(policyDto, "TestUser");

        // Assert
        _mockDb.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdatePolicyAsync_EnforceMfaWithPasskeyEnabled_DoesNotThrow()
    {
        // Arrange
        var policyDto = new SecurityPolicyDto
        {
            EnforceMandatoryMfaEnrollment = true,
            EnableTotpMfa = false,
            EnableEmailMfa = false,
            EnablePasskey = true,   // Passkey is enabled
            MinPasswordLength = 8
        };

        SetupDbSetMock();
        
        var service = new SecurityPolicyService(_mockDb.Object, _mockCache.Object, _mockLogger.Object);

        // Act - Should not throw
        await service.UpdatePolicyAsync(policyDto, "TestUser");

        // Assert
        _mockDb.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdatePolicyAsync_NoEnforceMfaWithNoMfaMethods_DoesNotThrow()
    {
        // Arrange - Enforcement is OFF, so it's OK to have no MFA methods
        var policyDto = new SecurityPolicyDto
        {
            EnforceMandatoryMfaEnrollment = false, // Enforcement is OFF
            EnableTotpMfa = false,
            EnableEmailMfa = false,
            EnablePasskey = false,
            MinPasswordLength = 8
        };

        SetupDbSetMock();
        
        var service = new SecurityPolicyService(_mockDb.Object, _mockCache.Object, _mockLogger.Object);

        // Act - Should not throw because enforcement is off
        await service.UpdatePolicyAsync(policyDto, "TestUser");

        // Assert
        _mockDb.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private void SetupDbSetMock()
    {
        var policies = new List<SecurityPolicy> { new() { Id = Guid.NewGuid() } };
        var mockDbSet = CreateMockDbSet(policies);
        _mockDb.Setup(x => x.SecurityPolicies).Returns(mockDbSet.Object);
        _mockDb.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mockCache.Setup(x => x.Remove(It.IsAny<object>()));
    }

    // Helper method to create a mock DbSet with async support
    private static Mock<DbSet<T>> CreateMockDbSet<T>(List<T> sourceList) where T : class
    {
        var queryable = sourceList.AsQueryable();
        var mockDbSet = new Mock<DbSet<T>>();
        
        mockDbSet.As<IAsyncEnumerable<T>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<T>(queryable.GetEnumerator()));

        mockDbSet.As<IQueryable<T>>()
            .Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
        mockDbSet.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryable.Expression);
        mockDbSet.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        mockDbSet.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(() => queryable.GetEnumerator());

        return mockDbSet;
    }
}

// Note: TestAsyncEnumerator, TestAsyncEnumerable, TestAsyncQueryProvider are already defined 
// in UserManagementTests.cs in Tests.Application.UnitTests. Since the test helpers are internal,
// we need to redefine them here or make them public and move to a shared location.

internal class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
{
    private readonly IEnumerator<T> _inner;

    public TestAsyncEnumerator(IEnumerator<T> inner) => _inner = inner;

    public T Current => _inner.Current;

    public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(_inner.MoveNext());

    public ValueTask DisposeAsync()
    {
        _inner.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal class TestAsyncQueryProvider<TEntity> : IAsyncQueryProvider
{
    private readonly IQueryProvider _inner;

    public TestAsyncQueryProvider(IQueryProvider inner) => _inner = inner;

    public IQueryable CreateQuery(Expression expression) => new TestAsyncEnumerable<TEntity>(expression);
    public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new TestAsyncEnumerable<TElement>(expression);
    public object? Execute(Expression expression) => _inner.Execute(expression);
    public TResult Execute<TResult>(Expression expression) => _inner.Execute<TResult>(expression);

    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
    {
        var resultType = typeof(TResult).GetGenericArguments()[0];
        var executeMethod = typeof(IQueryProvider).GetMethod(nameof(IQueryProvider.Execute), 1, new[] { typeof(Expression) })!;
        var result = executeMethod.MakeGenericMethod(resultType).Invoke(_inner, new object[] { expression });
        return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType).Invoke(null, new[] { result })!;
    }
}

internal class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
{
    public TestAsyncEnumerable(IEnumerable<T> enumerable) : base(enumerable) { }
    public TestAsyncEnumerable(Expression expression) : base(expression) { }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());

    IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
}
