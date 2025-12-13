# System Tests - Test Data Cleanup Guidelines

## Principles

All tests that create data **MUST** clean up after completion to ensure:
1. Test independence (no impact on other tests)
2. Repeatability (can run multiple times)
3. Clean test environment

## Current Test Status

**All existing tests are READ-ONLY** and do not create data:
- Health checks - read-only
- Endpoint validation (401/400) - read-only
- OIDC flows (M2M, Device, Legacy) - use pre-seeded test users

**No cleanup required** for current tests.

## Future CRUD Test Guidelines

When adding authenticated Admin API CRUD tests:

### Pattern 1: IAsyncLifetime Cleanup (Recommended)

```csharp
public class UserCrudTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly List<string> _createdUserIds = new();
    
    public Task InitializeAsync() => Task.CompletedTask;
    
    public async Task DisposeAsync()
    {
        // Clean up all created test data
        foreach (var userId in _createdUserIds)
        {
            await DeleteUserAsync(userId);
        }
    }
    
    [Fact]
    public async Task CreateUser_ValidData_ReturnsCreated()
    {
        var userId = await CreateUserAsync(...);
        _createdUserIds.Add(userId); // Track for cleanup
        
        // ... assertions
    }
}
```

### Pattern 2: Test-Specific Identifiers

Use identifiable test data prefixes:
```csharp
var testUser = new User 
{ 
    Email = $"test_{Guid.NewGuid()}@test.local",
    Username = $"test_user_{DateTime.UtcNow.Ticks}"
};
```

Then batch cleanup:
```sql
DELETE FROM Users WHERE Email LIKE 'test_%@test.local';
```

### Pattern 3: Dedicated Test Client

Create a dedicated test client that automatically tracks and cleans up:
```csharp
public class AdminApiTestClient
{
    private readonly List<string> _resourcesToCleanup = new();
    
    public async Task<string> CreateUserAsync(...)
    {
        var userId = await PostAsync(...);
        _resourcesToCleanup.Add($"/api/admin/users/{userId}");
        return userId;
    }
    
    public async Task CleanupAsync()
    {
        foreach (var resource in _resourcesToCleanup)
        {
            await DeleteAsync(resource);
        }
    }
}
```

## Recommendations

1. **System Tests**: Use IAsyncLifetime cleanup pattern
2. **CI Environment**: Use dedicated test database, reset before tests
3. **Local Development**: Use test-specific prefixes for easy manual cleanup

## Test Data Helper (Future)

```csharp
public class TestDataCleaner
{
    private readonly HttpClient _client;
    private readonly string _adminToken;
    
    public async Task CleanupUserAsync(string userId)
    {
        await _client.DeleteAsync($"/api/admin/users/{userId}");
    }
    
    public async Task CleanupAllTestDataAsync()
    {
        // Cleanup all data with test_ prefix
    }
}
```

## Action Required

**Current**: No action needed (all tests are read-only)

**Future**: When adding CRUD tests, implement cleanup using Pattern 1 or Pattern 3

## Example Implementation

See `UserCrudTests.cs` for a complete example of:
- IAsyncLifetime with InitializeAsync and DisposeAsync
- Cleanup before tests (removes leftover data from previous runs)
- Cleanup after tests (removes all created test data)
- Test data prefix pattern for easy identification
- Automatic tracking of created resources

**Note**: UserCrudTests currently marked as TODO due to missing admin M2M client with proper scopes.
To enable: Add admin M2M client to ClientSeeder with `/api/admin/*` permissions.
