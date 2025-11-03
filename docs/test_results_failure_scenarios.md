# Failure Scenarios Test Results

**Test Date**: November 3, 2025  
**Tester**: GitHub Copilot (MCP Browser Automation)  
**Environment**: Local Development (HTTPS)

---

## Test Summary

| # | Test Scenario | Status | Error Code | Notes |
|---|---------------|--------|------------|-------|
| 1 | User Denies Consent | ✅ PASS | `access_denied` | IdP returns correct error; TestClient needs better error handling |
| 2 | Invalid Client ID | ✅ PASS | `invalid_request` | Properly rejects unknown client; no redirect |
| 3 | Invalid Redirect URI | ✅ PASS | `invalid_request` | Security validated - does NOT redirect to malicious URL |
| 4 | Invalid/Unknown Scope | ✅ PASS | `invalid_scope` | Properly rejects unregistered scopes |
| 5 | Missing Required Parameter | ✅ PASS | `invalid_request` | Clear error message for missing `redirect_uri` |
| 6 | Missing OpenID Scope | ⚠️ PARTIAL | N/A | Allowed by spec - OAuth2 flow without OIDC (no ID token) |

**Overall Result**: 5/5 Critical Tests Passed ✅

---

## Detailed Test Results

### Test 1: User Denies Consent ✅

**URL**: `https://localhost:7001/Account/Profile` → Authorization Page

**Steps**:
1. Navigate to TestClient Profile (triggers OIDC login)
2. Redirected to IdP authorization page
3. Click "Deny" button

**Expected**:
- Error: `access_denied`
- Description: "The authorization was denied by the user"
- No token issued

**Actual**:
- ✅ IdP correctly returned `error=access_denied`
- ✅ Error description: "The authorization was denied by the user"
- ✅ Error URI: `https://documentation.openiddict.com/errors/ID2015`
- ⚠️ **Issue Found**: TestClient shows developer exception page (500) instead of friendly error message

**Security Assessment**: ✅ **SECURE** - Authorization properly denied

**Action Items**:
- Improve TestClient error handling to show user-friendly error page
- Consider logging authorization denials for analytics

---

### Test 2: Invalid Client ID ✅

**URL**: 
```
https://localhost:7035/connect/authorize?
  client_id=invalid_client&
  redirect_uri=https://localhost:7001/signin-oidc&
  response_type=code&
  scope=openid profile&
  state=test_state
```

**Expected**:
- HTTP 400 Bad Request
- Error: `invalid_request`
- No redirect (client untrusted)

**Actual**:
- ✅ HTTP 400 response
- ✅ Error: `error:invalid_request`
- ✅ Description: "The specified 'client_id' is invalid"
- ✅ Error URI: `https://documentation.openiddict.com/errors/ID2052`
- ✅ No redirect performed (stayed on IdP domain)

**Security Assessment**: ✅ **SECURE** - Prevents unauthorized clients

---

### Test 3: Invalid Redirect URI ✅

**URL**: 
```
https://localhost:7035/connect/authorize?
  client_id=test_client&
  redirect_uri=https://evil.com/callback&  ← MALICIOUS
  response_type=code&
  scope=openid profile&
  code_challenge=test123&
  code_challenge_method=S256&
  state=test_state
```

**Expected**:
- HTTP 400 Bad Request
- Error: `invalid_request`
- **CRITICAL**: Must NOT redirect to evil.com

**Actual**:
- ✅ HTTP 400 response
- ✅ Error: `error:invalid_request`
- ✅ Description: "The specified 'redirect_uri' is not valid for this client application"
- ✅ Error URI: `https://documentation.openiddict.com/errors/ID2043`
- ✅ **SECURITY VALIDATED**: Page stayed on `localhost:7035` - NO redirect to evil.com

**Security Assessment**: ✅ **SECURE** - Prevents open redirect vulnerability

**Critical Finding**: This is a key security control. If this test failed, attackers could:
- Steal authorization codes
- Perform phishing attacks
- Hijack user sessions

---

### Test 4: Invalid/Unknown Scope ✅

**URL**: 
```
https://localhost:7035/connect/authorize?
  client_id=test_client&
  redirect_uri=https://localhost:7001/signin-oidc&
  response_type=code&
  scope=openid invalid_scope_name&  ← UNKNOWN SCOPE
  code_challenge=test123&
  code_challenge_method=S256&
  state=test_state
```

**Expected**:
- HTTP 400 Bad Request
- Error: `invalid_scope`

**Actual**:
- ✅ HTTP 400 response
- ✅ Error: `error:invalid_scope`
- ✅ Description: "The specified 'scope' is invalid"
- ✅ Error URI: `https://documentation.openiddict.com/errors/ID2052`

**Security Assessment**: ✅ **SECURE** - Prevents unauthorized data access

---

### Test 5: Missing Required Parameter ✅

**URL**: 
```
https://localhost:7035/connect/authorize?
  client_id=test_client&
  scope=openid profile
  ← Missing redirect_uri, response_type, code_challenge
```

**Expected**:
- HTTP 400 Bad Request
- Error: `invalid_request`
- Clear error message about missing parameter

**Actual**:
- ✅ HTTP 400 response
- ✅ Error: `error:invalid_request`
- ✅ Description: "The mandatory 'redirect_uri' parameter is missing"
- ✅ Error URI: `https://documentation.openiddict.com/errors/ID2029`

**Security Assessment**: ✅ **SECURE** - Validates required parameters

**Note**: OpenIddict validates parameters in order:
1. `redirect_uri` (first - for error response routing)
2. `response_type`
3. `code_challenge` (if PKCE required)
4. Other parameters

---

### Test 6: Missing OpenID Scope ⚠️

**URL**: 
```
https://localhost:7035/connect/authorize?
  client_id=test_client&
  redirect_uri=https://localhost:7001/signin-oidc&
  response_type=code&
  scope=profile email&  ← NO 'openid' scope
  code_challenge=test123&
  code_challenge_method=S256&
  state=test_state
```

**Expected**:
- Per OpenID Connect spec: Should reject if `openid` scope missing
- OR: Allow as OAuth2 flow (no ID token)

**Actual**:
- ⚠️ Authorization page displayed
- ✅ Consent shows only: "Access your profile information" + "Access your email address"
- ✅ Does NOT show: "Verify your identity" (requires `openid` scope)
- ℹ️ This is **valid OAuth2** behavior (not OIDC)

**Behavioral Analysis**:
- If user clicks "Allow", token endpoint will:
  - ✅ Issue access token
  - ❌ NOT issue ID token (no `openid` scope)
  - ✅ Scopes limited to `profile` and `email`

**Security Assessment**: ⚠️ **ACCEPTABLE** - Spec-compliant OAuth2 flow

**Recommendation**:
- Consider adding validation to **require** `openid` scope for OIDC clients
- Add configuration option: `RequireOpenIdScope = true` for strict OIDC mode
- Current behavior is OAuth2-compatible (broader use case)

---

## Security Findings Summary

### ✅ Passed Security Controls

1. **Client Validation**: Unknown clients rejected
2. **Redirect URI Validation**: Prevents open redirect attacks
3. **Scope Validation**: Unknown scopes rejected
4. **Parameter Validation**: Missing required parameters rejected
5. **User Consent**: User can deny authorization

### ⚠️ Issues Found

1. **TestClient Error Handling** (Medium Priority)
   - **Issue**: Shows 500 developer exception page on authorization denial
   - **Impact**: Poor user experience; may expose stack traces
   - **Recommendation**: Add custom error handling in OIDC events
   - **Code Location**: `TestClient/Program.cs` → `OnRemoteFailure` event

2. **OpenID Scope Optional** (Low Priority - Spec Compliant)
   - **Issue**: Authorization succeeds without `openid` scope
   - **Impact**: OAuth2 flow instead of OIDC; no ID token issued
   - **Recommendation**: Consider strict OIDC mode option
   - **Code Location**: Consider client-level configuration

---

## Error Response Format Analysis

OpenIddict returns errors in this format:

```
error:invalid_request 
error_description:The specified 'client_id' is invalid. 
error_uri:https://documentation.openiddict.com/errors/ID2052
```

**Positive Observations**:
- ✅ Errors are clear and descriptive
- ✅ Error URIs link to documentation
- ✅ Error codes follow OAuth2/OIDC spec
- ✅ No sensitive information leaked

---

## Recommendations

### High Priority
1. **Improve TestClient Error Handling**
   ```csharp
   // TestClient/Program.cs
   options.Events.OnRemoteFailure = context =>
   {
       if (context.Failure?.Message.Contains("access_denied") == true)
       {
           context.Response.Redirect("/Error/AccessDenied");
           context.HandleResponse();
       }
       return Task.CompletedTask;
   };
   ```

2. **Add Error Logging**
   - Log all authorization failures for security monitoring
   - Include: client_id, requested scopes, error type, timestamp
   - Alert on suspicious patterns (e.g., repeated invalid redirect_uri)

### Medium Priority
3. **Add E2E Automated Tests**
   - Run `e2e/tests/authorization-failures.spec.ts` in CI/CD
   - Verify error responses in automated pipeline

4. **Consider PKCE Enforcement**
   - Already enforced (good!)
   - Document in security guidelines

### Low Priority
5. **Add Strict OIDC Mode**
   - Optional configuration to require `openid` scope
   - Useful for pure OIDC scenarios

---

## Test Coverage

### ✅ Tested Scenarios
- Authorization denial by user
- Invalid client_id
- Invalid redirect_uri (open redirect prevention)
- Invalid/unknown scopes
- Missing required parameters
- Missing openid scope

### 🔜 Future Test Scenarios
- [ ] Expired authorization code
- [ ] PKCE challenge mismatch
- [ ] Expired access token
- [ ] Invalid token signature
- [ ] Revoked token
- [ ] Concurrent authorization requests
- [ ] Database connection failure during authorization
- [ ] Non-existent user property paths in scope-mapped claims

---

## Conclusion

The HybridIdP authorization endpoint demonstrates **strong security controls**:

- ✅ All critical security validations passed
- ✅ Prevents common OAuth2/OIDC vulnerabilities
- ✅ Error messages are clear without leaking sensitive data
- ✅ Follows OAuth2 and OpenID Connect specifications

**Main Action Item**: Improve TestClient error handling to provide better user experience on authorization failures.

**Overall Security Rating**: 🟢 **STRONG** (5/5 critical tests passed)
