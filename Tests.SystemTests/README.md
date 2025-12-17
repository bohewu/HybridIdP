# System Tests

## Overview

System tests for the HybridIdP project. Tests are categorized as:
- **Quick Tests**: Fast-running tests (~14 seconds total, 183 tests)
- **Slow Tests**: Tests with rate limiting, long waits, or external dependencies (~33 tests)

## Running Tests

### Quick Tests (Recommended for Development)

```bash
# Run all quick tests (excludes slow tests)
dotnet test Tests.SystemTests --filter "Category!=Slow"
```

### All Tests

```bash
# Run all tests (including slow tests, ~3-5 minutes)
dotnet test Tests.SystemTests
```

### Slow Tests

#### Run All Slow Tests
```bash
dotnet test Tests.SystemTests --filter "Category=Slow"
```

⚠️ **Warning**: Running all slow tests in batch can take 3-5 minutes due to rate limiting and sequential execution.

#### Run Slow Tests Individually (Recommended)

For faster verification, run each slow test class individually:

```bash
# MFA API Tests (13 tests, ~30s)
dotnet test Tests.SystemTests --filter "FullyQualifiedName~MfaApiTests"

# MFA Full Flow Tests (5 tests, ~10s)
dotnet test Tests.SystemTests --filter "FullyQualifiedName~MfaFullFlowTests"

# Email MFA Flow Tests (4 tests, ~10s)
dotnet test Tests.SystemTests --filter "FullyQualifiedName~EmailMfaFlowTests"

# Person Lifecycle Tests (8 tests, ~5s)
dotnet test Tests.SystemTests --filter "FullyQualifiedName~PersonLifecycleSystemTests"

# Device Flow Tests (1 test, ~10s)
dotnet test Tests.SystemTests --filter "FullyQualifiedName~DeviceFlowSystemTests"

# Email System Tests (1 test, ~15s)
dotnet test Tests.SystemTests --filter "FullyQualifiedName~EmailSystemTests"

# MFA Rate Limit Tests (1 test, ~60s)
dotnet test Tests.SystemTests --filter "FullyQualifiedName~MfaRateLimitTests"
```

## Test Categories

### Quick Tests (183 tests)
- Login/Logout flows
- Authorization tests
- CRUD operations
- API validation
- User management

### Slow Tests (33 tests)

| Test Class | Count | Duration | Reason |
|------------|-------|----------|--------|
| `MfaApiTests` | 13 | ~30s | Sequential execution, rate limiting |
| `MfaFullFlowTests` | 5 | ~10s | Sequential execution |
| `EmailMfaFlowTests` | 4 | ~10s | Sequential execution, rate limiting |
| `PersonLifecycleSystemTests` | 8 | ~5s | Multiple API calls |
| `DeviceFlowSystemTests` | 1 | ~10s | External process, 10s timeout |
| `EmailSystemTests` | 1 | ~15s | Waits for Mailpit delivery |
| `MfaRateLimitTests` | 1 | ~60s | Tests rate limiting cooldown |

## Test Isolation

MFA-related tests use:
- **Sequential Execution**: `[Collection("MFA Tests")]` prevents parallel runs
- **Pre-cleanup**: `InitializeAsync()` disables MFA before tests start
- **Post-cleanup**: `DisposeAsync()` disables MFA after tests complete

This ensures test isolation and prevents state pollution between test runs.

## CI/CD Recommendations

### Fast Feedback (PR Checks)
```bash
dotnet test Tests.SystemTests --filter "Category!=Slow"
```

### Full Validation (Nightly Builds)
```bash
# Run each slow test class individually for better parallelization
dotnet test Tests.SystemTests --filter "FullyQualifiedName~MfaApiTests" &
dotnet test Tests.SystemTests --filter "FullyQualifiedName~MfaFullFlowTests" &
dotnet test Tests.SystemTests --filter "FullyQualifiedName~EmailMfaFlowTests" &
dotnet test Tests.SystemTests --filter "FullyQualifiedName~PersonLifecycleSystemTests" &
wait
```

## Troubleshooting

### Tests Fail Due to MFA State
If tests fail because MFA is already enabled:
1. The pre-cleanup in `InitializeAsync` should handle this automatically
2. If issues persist, manually reset the test user's MFA state in the database

### Rate Limit Failures
- `EmailMfa` tests have a 60-second cooldown between sends
- Tests include automatic rate limit handling and retries
- If running tests repeatedly, wait 60 seconds between runs

### Mailpit Email Delivery
- `EmailSystemTests` waits up to 15 seconds for email delivery
- Ensure Mailpit is running: `docker-compose up -d mailpit`
- Check Mailpit UI: http://localhost:8025
