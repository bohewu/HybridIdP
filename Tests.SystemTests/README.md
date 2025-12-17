# System Tests

## Running Tests

### Quick Tests (excluding slow tests)
```bash
# Use this for everyday development - skips slow rate-limit and lifecycle tests
dotnet test Tests.SystemTests --filter "Category!=Slow"
```

### Full Tests (including slow tests)
```bash
# Use this for CI/CD or before release - runs all tests
dotnet test Tests.SystemTests
```

### Specific Test Category
```bash
# Run only MFA tests
dotnet test Tests.SystemTests --filter "FullyQualifiedName~MfaApiTests"

# Run only CRUD tests
dotnet test Tests.SystemTests --filter "FullyQualifiedName~CrudTests"
```

## Test Categories

Tests marked with `[Trait("Category", "Slow")]` are excluded from quick runs:
- **MfaRateLimitTests** - waits for rate limit cooldowns (60+ seconds)
- **EmailMfaFlowTests** - rate limit waits + email queue processing
- **PersonLifecycleSystemTests** - lifecycle state transitions with date checks
