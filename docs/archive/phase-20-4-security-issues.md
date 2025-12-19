# Phase 20.4 - Security Issues & Fixes

**Date**: 2025-12-17  
**Priority**: 🔴 **HIGH - Must fix before deployment**

---

## Security Audit Summary

### ✅ What's Secure (Other MFA)

**TOTP & Email MFA** (`MfaController.cs`):
- ✅ All endpoints protected by `[ApiAuthorize]`
- ✅ Proper user authentication checks
- ✅ Rate limiting on critical endpoints
- ✅ Audit logging for all MFA operations

**Login Flow** (`Login.cshtml.cs`):
- ✅ Person.Status validation (Line 290-303: `PersonInactive` check)
- ✅ User.IsActive validation (Line 275-288: `UserInactive` check)
- ✅ Account lockout handling (Line 259-273)
- ✅ Abnormal login detection & blocking

---

## ⚠️ Critical Issues in Passkey Implementation

### Issue 1: PasskeyService is STUB (🔴 CRITICAL)

**Location**: `Infrastructure/Services/PasskeyService.cs`

**Problem**:
```csharp
// Line 72-79 - Registration is fake!
public Task<(bool Success, string? Error)> RegisterCredentialsAsync(...)
{
    _logger.LogInformation("Stub: Registering credentials...");
    return Task.FromResult((true, (string?)null)); // Always succeeds!
}

// Line 96-114 - Login verification is fake!
public Task<(bool Success, ApplicationUser? User, string? Error)> VerifyAssertionAsync(...)
{
    _logger.LogInformation("Stub: Verifying assertion");
    var stubUser = new ApplicationUser { UserName = "stub-user", Id = Guid.NewGuid() };
    return Task.FromResult((true, (ApplicationUser?)stubUser, (string?)null)); // Always succeeds with fake user!
}
```

**Risk**: 
- Anyone can register fake passkeys
- Anyone can login without valid credentials
- **SEVERITY**: 🔴 Critical - Complete authentication bypass

**Fix Required**:
```csharp
public async Task<(bool Success, string? Error)> RegisterCredentialsAsync(
    ApplicationUser user, 
    string jsonResponse, 
    string originalOptionsJson, 
    CancellationToken ct = default)
{
    try
    {
        // 1. Parse the response
        var attestationResponse = AuthenticatorAttestationRawResponse.Parse(jsonResponse);
        var options = CredentialCreateOptions.FromJson(originalOptionsJson);
        
        // 2. Verify with Fido2
        var result = await _fido2.MakeNewCredentialAsync(
            attestationResponse, 
            options, 
            async (args, ct) => 
            {
                // Check if credential already exists
                var exists = await _dbContext.UserCredentials
                    .AnyAsync(c => c.CredentialId == args.CredentialId, ct);
                return !exists;
            }, 
            ct);
        
        if (result.Status != "ok")
        {
            return (false, result.ErrorMessage);
        }
        
        // 3. Save to database
        var credential = new UserCredential
        {
            UserId = user.Id,
            CredentialId = result.Result.CredentialId,
            PublicKey = result.Result.PublicKey,
            SignatureCounter = result.Result.Counter,
            CredType = result.Result.CredType,
            RegDate = DateTime.UtcNow,
            AaGuid = result.Result.Aaguid,
            DeviceName = deviceName
        };
        
        _dbContext.UserCredentials.Add(credential);
        await _dbContext.SaveChangesAsync(ct);
        
        return (true, null);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to register passkey for user {UserId}", user.Id);
        return (false, "Registration failed");
    }
}
```

---

### Issue 2: No Person.Status Check on Passkey Login (🔴 HIGH)

**Location**: `PasskeyController.cs:128-132`

**Problem**:
```csharp
if (result.Success && result.User != null)
{
    await _signInManager.SignInAsync(result.User, isPersistent: false);
    // 缺少 Person.Status 檢查！
}
```

**Risk**:
- Suspended/Deactivated Person 仍可透過 Passkey 登入
- 繞過 Access Control

**Fix Required**:
```csharp
if (result.Success && result.User != null)
{
    // 1. Check Person.Status
    if (result.User.Person != null)
    {
        switch (result.User.Person.Status)
        {
            case PersonStatus.Suspended:
                return BadRequest(new { success = false, error = "Account suspended" });
            case PersonStatus.Inactive:
                return BadRequest(new { success = false, error = "Account inactive" });
        }
    }
    
    // 2. Check User.IsActive
    if (!result.User.IsActive)
    {
        return BadRequest(new { success = false, error = "User account deactivated" });
    }
    
    // 3. Sign in
    await _signInManager.SignInAsync(result.User, isPersistent: false);
    LogPasskeyLogin(result.User.UserName);
    return Ok(new { success = true, username = result.User.UserName });
}
```

---

### Issue 3: No MaxPasskeysPerUser Limit Check (🟡 MEDIUM)

**Location**: `PasskeyController.cs` - `register-options` endpoint

**Problem**:
- No check against `SecurityPolicy.MaxPasskeysPerUser`
- Users can register unlimited passkeys

**Risk**:
- Database bloat
- Potential DoS via excessive registrations
- **SEVERITY**: 🟡 Medium

**Fix Required**:
```csharp
[HttpPost("register-options")]
[ApiAuthorize]
public async Task<IActionResult> MakeCredentialOptions(CancellationToken ct)
{
    var user = await GetAuthenticatedUserAsync();
    if (user == null) return Unauthorized();

    // 1. Get security policy
    var policy = await _securityPolicyService.GetCurrentPolicyAsync();
    
    // 2. Check if passkey is enabled
    if (!policy.EnablePasskey)
    {
        return StatusCode(403, new { error = "Passkey authentication is disabled" });
    }
    
    // 3. Count existing passkeys
    var existingCount = await _dbContext.UserCredentials
        .CountAsync(c => c.UserId == user.Id, ct);
    
    if (existingCount >= policy.MaxPasskeysPerUser)
    {
        return BadRequest(new { 
            error = $"Maximum passkey limit reached ({policy.MaxPasskeysPerUser})" 
        });
    }

    var options = await _passkeyService.GetRegistrationOptionsAsync(user, ct);
    HttpContext.Session.SetString("fido2.attestationOptions", options.ToJson());
    LogRegistrationOptionsGenerated(user.UserName);
    return Ok(options);
}
```

---

### Issue 4: Missing APIs (🟡 MEDIUM)

**Currently Missing**:
- `GET /api/passkey/list` - List user's passkeys
- `DELETE /api/passkey/{id}` - Delete specific passkey

**Risk**:
- Users cannot manage their passkeys
- No way to remove compromised keys
- **SEVERITY**: 🟡 Medium (Functionality gap)

**Fix Required**: See implementation plan Step 1

---

### Issue 5: No Rate Limiting on Registration (🟢 LOW)

**Location**: `PasskeyController.cs`

**Problem**:
- No `[EnableRateLimiting]` attribute on passkey endpoints

**Risk**:
- Potential abuse to exhaust resources
- **SEVERITY**: 🟢 Low

**Fix Required**:
```csharp
[Route("api/passkey")]
[ApiController]
[EnableRateLimiting("default")] // Add this
public partial class PasskeyController : ControllerBase
{
    // ...
}
```

---

## Implementation Priority

| Priority | Issue | Impact | Effort |
|----------|-------|--------|--------|
| 🔴 P0 | PasskeyService Stub | Complete auth bypass | High |
| 🔴 P1 | Person.Status Check | Access control bypass | Low |
| 🟡 P2 | MaxPasskeys Limit | Resource abuse | Low |
| 🟡 P3 | Missing List/Delete APIs | Functionality | Medium |
| 🟢 P4 | Rate Limiting | Minor abuse | Low |

---

## Testing Requirements

### Security Tests (MUST HAVE)

```csharp
[Fact]
public async Task PasskeyLogin_SuspendedPerson_ShouldFail()
{
    // Arrange: User with suspended Person
    // Act: Login with valid passkey
    // Assert: Should return 403 Forbidden
}

[Fact]
public async Task RegisterPasskey_ExceedLimit_ShouldFail()
{
    // Arrange: User has MaxPasskeysPerUser passkeys
    // Act: Try to register one more
    // Assert: Should return 400 Bad Request
}

[Fact]
public async Task RegisterPasskey_FeatureDisabled_ShouldFail()
{
    // Arrange: SecurityPolicy.EnablePasskey = false
    // Act: Try to register
    // Assert: Should return 403 Forbidden
}

[Fact]
public async Task DeletePasskey_NotOwner_ShouldFail()
{
    // Arrange: Try to delete another user's passkey
    // Act: DELETE /api/passkey/{othersId}
    // Assert: Should return 404 or 403
}
```

---

## Recommendation

**DO NOT deploy Passkey feature to production until:**
1. ✅ PasskeyService properly implements Fido2 verification
2. ✅ Person.Status validation added to login flow
3. ✅ MaxPasskeysPerUser limit enforced
4. ✅ List/Delete APIs implemented
5. ✅ Security tests pass

**Estimated Fix Time**: 4-6 hours

**Risk Assessment**: Current implementation = **UNSAFE FOR PRODUCTION**
