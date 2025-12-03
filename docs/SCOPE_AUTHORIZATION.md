# Scope-Based Authorization Guide

**Status:** Phase 9 — Scope Authorization & Management: Completed (2025-11-28) ✅
See `docs/archive/phases/phase-9-scope-authorization.md` for implementation details and E2E test coverage.

This guide explains how to use and configure scope-based authorization in HybridIdP to protect API endpoints and manage user consent for resource access.

## Table of Contents

- [Overview](#overview)
- [Protecting API Endpoints](#protecting-api-endpoints)
- [Configuring Client Required Scopes](#configuring-client-required-scopes)
- [Consent Page Behavior](#consent-page-behavior)
- [Best Practices](#best-practices)
- [Troubleshooting](#troubleshooting)

## Overview

HybridIdP implements OAuth 2.0/OpenID Connect scope-based authorization, allowing fine-grained access control for protected resources. Scopes represent permissions that clients can request, and users can grant or deny during the consent process.

### Key Features

- **Runtime Authorization**: Use `[Authorize(Policy = "RequireScope:scope_name")]` to protect endpoints
- **Required Scopes**: Configure per-client required scopes that cannot be denied by users
- **Consent Management**: Users see required scopes as disabled checkboxes with clear indicators
- **Tampering Detection**: Server-side validation prevents users from bypassing required scopes
- **OIDC Compliance**: Enforces `openid` scope requirement for `/connect/userinfo` endpoint

## Protecting API Endpoints

### Basic Usage

Use the `RequireScope:` policy pattern to protect controllers or actions:

```csharp
[ApiController]
[Route("api/[controller]")]
public class CompanyController : ControllerBase
{
    [Authorize(Policy = "RequireScope:api:company:read")]
    [HttpGet]
    public IActionResult GetCompanyData()
    {
        // Only accessible with api:company:read scope in access token
        return Ok(new { company = "Acme Corp" });
    }

    [Authorize(Policy = "RequireScope:api:company:write")]
    [HttpPost]
    public IActionResult UpdateCompanyData([FromBody] CompanyDto data)
    {
        // Only accessible with api:company:write scope
        return Ok();
    }
}
```

### Multiple Scopes

Require multiple scopes by applying multiple `[Authorize]` attributes:

```csharp
[Authorize(Policy = "RequireScope:api:company:read")]
[Authorize(Policy = "RequireScope:api:admin")]
[HttpGet("sensitive")]
public IActionResult GetSensitiveData()
{
    // Requires BOTH api:company:read AND api:admin scopes
    return Ok();
}
```

### Scope Naming Conventions

Follow these conventions for consistent scope naming:

- **OIDC Standard Scopes**: `openid`, `profile`, `email`, `address`, `phone`
- **API Resource Scopes**: `api:resource:action` (e.g., `api:company:read`, `api:inventory:write`)
- **Admin Scopes**: `admin:area` (e.g., `admin:users`, `admin:settings`)
- **Custom Scopes**: Use descriptive names with colons as separators

## Configuring Client Required Scopes

### Via Admin API

Mark scopes as required using the `/api/admin/clients/{id}/required-scopes` endpoint:

```csharp
// Set required scopes
PUT /api/admin/clients/{clientGuid}/required-scopes
Content-Type: application/json

{
  "scopes": ["openid", "profile"]
}
```

**Validation Rules**:
- Required scopes MUST be in the client's allowed scopes list
- Returns `400 Bad Request` if validation fails

### Via Admin UI

1. Navigate to **Admin > Clients**
2. Click **Edit** on the target client
3. In the **Client Scope Manager** section:
   - Ensure scopes are in the **Selected Scopes** list (allowed)
   - Toggle the **Required** switch for scopes that must be included
4. Click **Save**

### Programmatically (in code)

```csharp
public class ClientSetupService
{
    private readonly IClientAllowedScopesService _scopeService;

    public async Task ConfigureClient(Guid clientId)
    {
        // Set required scopes for a client
        await _scopeService.SetRequiredScopesAsync(clientId, new[] 
        { 
            "openid",
            "api:company:read"
        });

        // Check if a scope is required
        var isRequired = await _scopeService.IsScopeRequiredAsync(clientId, "openid");
    }
}
```

## Consent Page Behavior

### For End Users

When users authenticate and grant consent, they will see:

**Required Scopes**:
- Checkbox is **disabled** (grayed out)
- Checkbox is **pre-checked**
- Badge shows "Required"
- Cannot be unchecked

**Optional Scopes**:
- Checkbox is **enabled**
- User can check or uncheck
- Unchecked scopes are excluded from the access token

### Example Consent Page

```
┌─────────────────────────────────────────┐
│ Authorize Application                   │
├─────────────────────────────────────────┤
│ TestClient wants to access:             │
│                                          │
│ ☑ openid - Sign in          [Required]  │ (disabled)
│ ☑ profile - Your profile    [Required]  │ (disabled)
│ ☑ email - Your email address            │ (enabled)
│ ☑ api:company:read - Company data       │ (enabled)
│                                          │
│ [Deny]                [Allow]           │
└─────────────────────────────────────────┘
```

### Tampering Detection

The server validates that all required scopes are present in the consent submission:

```csharp
// Server-side validation in Authorize.cshtml.cs
var clientRequiredScopes = await _clientAllowedScopesService.GetRequiredScopesAsync(clientGuid);
var missingRequired = clientRequiredScopes.Except(effectiveScopes, StringComparer.OrdinalIgnoreCase).ToList();

if (missingRequired.Any())
{
    await _auditService.LogAsync(new AuditEvent
    {
        EventType = AuditEventType.ConsentTamperingDetected,
        UserId = userId,
        Details = new Dictionary<string, object>
        {
            ["clientId"] = clientId,
            ["missingRequiredScopes"] = missingRequired
        }
    });

    return BadRequest("Required scopes cannot be excluded from consent.");
}
```

**Audit Trail**: Tampering attempts are logged as `ConsentTamperingDetected` events for security monitoring.

## Best Practices

### 1. Principle of Least Privilege

Only request scopes that are actually needed:

```csharp
// ❌ Bad: Requesting unnecessary scopes
permissions: ['scp:openid', 'scp:profile', 'scp:email', 'scp:address', 
              'scp:phone', 'scp:api:admin', 'scp:api:company:write']

// ✅ Good: Only request what you need
permissions: ['scp:openid', 'scp:profile', 'scp:api:company:read']
```

### 2. Use Required Scopes Sparingly

Only mark scopes as required if they are **absolutely essential** for the application to function:

- **Required**: `openid` (for OIDC userinfo)
- **Required**: `api:core` (if app cannot work without it)
- **Optional**: `profile`, `email`, `api:analytics` (nice-to-have features)

### 3. Document Scope Purposes

Provide clear descriptions for all scopes in the Admin UI:

```csharp
await _scopeService.CreateAsync(new CreateScopeDto
{
    Name = "api:company:read",
    DisplayName = "Read Company Data",
    Description = "Allows the application to read your company information, including name, address, and contact details."
});
```

### 4. Group Related Scopes

Organize scopes by resource or feature area:

```
api:company:read
api:company:write
api:company:delete

api:inventory:read
api:inventory:write

admin:users
admin:settings
admin:audit
```

### 5. Handle Missing Scopes Gracefully

When calling APIs, check for scope-related errors:

```typescript
async function callProtectedApi() {
  try {
    const response = await fetch('/api/company', {
      headers: { Authorization: `Bearer ${accessToken}` }
    });
    
    if (response.status === 403) {
      // Missing required scope - prompt user to re-consent
      console.error('Insufficient permissions. Please grant required scopes.');
      redirectToConsent();
    }
    
    return await response.json();
  } catch (error) {
    console.error('API call failed:', error);
  }
}
```

## Troubleshooting

### Issue: "Required scopes cannot be excluded from consent"

**Cause**: User (or script) attempted to remove a required scope during consent.

**Solution**:
1. Check which scopes are marked as required for the client
2. Verify the client configuration in Admin UI
3. Review audit logs for `ConsentTamperingDetected` events

### Issue: 403 Forbidden when calling /connect/userinfo

**Cause**: Access token does not contain the `openid` scope.

**Solution**:
1. Ensure `openid` is in the client's allowed scopes
2. Mark `openid` as required for the client
3. Re-authenticate and grant consent

### Issue: Scope not appearing in access token

**Cause**: Scope was not granted during consent or is not in client's allowed scopes.

**Solution**:
1. Check client's allowed scopes in Admin > Clients
2. Verify scope exists in Admin > Scopes
3. Clear browser session and re-authenticate
4. Check consent page to ensure scope is displayed and checked

### Issue: Cannot mark scope as required in Admin UI

**Cause**: Scope is not in the client's allowed scopes list.

**Solution**:
1. First, add the scope to the client's allowed scopes (Selected Scopes list)
2. Save the client
3. Re-edit and toggle the Required switch

### Issue: ScopeAuthorizationHandler not working

**Check Registration**:
```csharp
// In Program.cs, verify these registrations exist:
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    Infrastructure.Authorization.ScopeAuthorizationHandler>();

builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider,
    Infrastructure.Authorization.ScopeAuthorizationPolicyProvider>();
```

**Check Policy Name**:
```csharp
// ✅ Correct format
[Authorize(Policy = "RequireScope:api:company:read")]

// ❌ Incorrect format
[Authorize(Policy = "api:company:read")]
[Authorize(Policy = "Scope:api:company:read")]
```

**Check Token Claims**:
Verify the access token contains the scope claim:
```json
{
  "scope": "openid profile api:company:read",
  "scp": ["openid", "profile", "api:company:read"]
}
```

### Issue: E2E tests failing with "invalid_scope" error

**Cause**: Test client is requesting scopes that don't exist or aren't allowed.

**Solution**:
1. Run `e2e/wait-for-idp-ready.ps1` to ensure scopes are seeded
2. Check `e2e/global-setup.ts` to verify test client configuration
3. Ensure API scopes (`api:company:read`, `api:inventory:read`) are created

## API Reference

### IClientAllowedScopesService

```csharp
public interface IClientAllowedScopesService
{
    // Get required scopes for a client
    Task<IReadOnlyList<string>> GetRequiredScopesAsync(Guid clientId);
    
    // Set required scopes (replaces existing)
    Task SetRequiredScopesAsync(Guid clientId, IEnumerable<string> scopeNames);
    
    // Check if a specific scope is required
    Task<bool> IsScopeRequiredAsync(Guid clientId, string scopeName);
    
    // Get allowed scopes for a client
    Task<IReadOnlyList<string>> GetAllowedScopesAsync(Guid clientId);
}
```

### Admin API Endpoints

```
GET    /api/admin/clients/{id}/scopes
       Returns: { scopes: string[] }
       
PUT    /api/admin/clients/{id}/scopes
       Body: { scopes: string[] }
       Sets allowed scopes for client
       
GET    /api/admin/clients/{id}/required-scopes
       Returns: { scopes: string[] }
       
PUT    /api/admin/clients/{id}/required-scopes
       Body: { scopes: string[] }
       Sets required scopes for client
       Validates that all required scopes are in allowed scopes
```

## Related Documentation

- [ARCHITECTURE.md](./ARCHITECTURE.md) - System architecture and design decisions
- [FEATURES.md](./FEATURES.md) - Complete feature list
- [phase-9-scope-authorization.md](./phase-9-scope-authorization.md) - Implementation details
- [E2E Testing Guide](../e2e/README.md) - Testing scope authorization flows

## Security Considerations

1. **Required scopes are enforced server-side** - Client-side checks are for UX only
2. **Access tokens are validated on every request** - Scopes are checked by the authorization handler
3. **Consent tampering is logged** - Security events are recorded for audit
4. **Scope changes require re-consent** - Users must approve new scopes
5. **OIDC compliance** - `openid` scope is mandatory for userinfo endpoint

---

**Version**: 1.0  
**Last Updated**: 2025-11-28  
**Phase**: 9.7 - E2E Testing & Documentation
