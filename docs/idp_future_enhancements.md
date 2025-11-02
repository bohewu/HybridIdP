# Future Enhancements for HybridAuthIdP

This document outlines features that are important for a robust and secure Identity Provider but can be deferred for implementation in a future release, after the initial core product is delivered.

---

## 1. Advanced Claims Transformation

**Goal:** Support complex claim value transformations beyond simple property mapping.

**Description:** Implement a flexible claims transformation engine that allows administrators to define custom logic for claim values. This goes beyond the basic property mapping in Phase 3.9A.

**Use Cases:**
- **Computed Claims:** Combine multiple properties (e.g., `full_name` = `FirstName + " " + LastName`)
- **Conditional Claims:** Include claim only if condition met (e.g., `is_premium` = true if `SubscriptionLevel == "Premium"`)
- **Format Transformations:** Convert values (e.g., `phone_number` formatted as international format)
- **External Data Sources:** Fetch additional claims from external APIs or databases
- **Group/Role Mapping:** Map internal roles to external claim values (e.g., `Admin` → `company_admin`)

**Implementation Considerations:**
- Create `ClaimTransformation` entity with transformation rules
- Support transformation types:
  - **JavaScript Expression:** Evaluate JavaScript for complex logic
  - **Template String:** Use placeholders like `{FirstName} {LastName}`
  - **Lookup Table:** Map values from dictionary (e.g., department codes → names)
  - **External API Call:** Fetch data from REST endpoint
- UI for defining transformations with testing/preview
- Cache external API results to avoid performance impact
- Security: Sandbox JavaScript execution to prevent code injection

**Example Transformations:**
```csharp
// Template String
full_name: "{FirstName} {LastName}"

// JavaScript Expression
is_vip: "user.Department === 'Executive' || user.JobTitle.includes('Director')"

// Lookup Table
department_name: { "IT": "Information Technology", "HR": "Human Resources", "FIN": "Finance" }

// External API (cached)
credit_score: "GET https://api.creditbureau.example.com/score?userId={Id}"
```

---

## 2. Dynamic Client Registration (DCR)

**Goal:** Allow clients to self-register via standard OAuth 2.0 Dynamic Client Registration Protocol (RFC 7591).

**Description:** Implement the OAuth 2.0 Dynamic Client Registration endpoint to enable automated client registration without manual admin intervention. This is useful for SaaS platforms where tenants need to create their own OIDC clients programmatically.

**Implementation Considerations:**
- Create endpoint: `POST /connect/register`
- Support standard DCR metadata fields (redirect_uris, grant_types, response_types, etc.)
- Authentication options:
  - **Open Registration:** Allow any client to register (with rate limiting)
  - **Token-Based:** Require initial access token for registration
  - **Whitelisted Domains:** Only allow registration from approved domains
- Assign default permissions to auto-registered clients
- Admin review/approval workflow option
- Support for client metadata management endpoint (RFC 7592)
- Rate limiting and abuse prevention
- Automatic cleanup of unused clients

**Reference:** https://datatracker.ietf.org/doc/html/rfc7591

---

## 3. Session Management & Single Logout (SLO)

**Goal:** Provide comprehensive session management and support for Single Logout across all connected clients.

**Description:** Implement session tracking and Single Logout (SLO) functionality so that when a user logs out from one application, they are automatically logged out from all applications that share the same IdP session.

**Implementation Considerations:**
- **Session Tracking:**
  - Store active sessions in database/Redis with expiration
  - Track which clients user is logged into
  - Session timeout and idle timeout configuration
  - Admin UI to view and revoke user sessions
  
- **Single Logout:**
  - Support OIDC RP-Initiated Logout (front-channel and back-channel)
  - Implement `end_session_endpoint` with `id_token_hint`
  - Maintain list of logged-in clients for each user session
  - Send logout notifications to all clients via:
    - **Front-channel:** Invisible iframes trigger client logout
    - **Back-channel:** Direct HTTP POST to client logout endpoints
  
- **Admin Features:**
  - View active sessions per user
  - Force logout specific sessions or all sessions for a user
  - Session activity log (login time, IP address, user agent, client app)
  
- **User Features:**
  - "Logged in on these devices" view
  - "Log out all other sessions" button
  - Suspicious login alerts

**OIDC Spec Reference:** https://openid.net/specs/openid-connect-rpinitiated-1_0.html

---

## 4. Token Introspection & Revocation

**Goal:** Provide endpoints for clients to validate tokens and revoke access/refresh tokens.

**Description:** Implement OAuth 2.0 Token Introspection (RFC 7662) and Token Revocation (RFC 7009) endpoints to allow resource servers to validate access tokens and clients to revoke tokens when needed.

**Implementation Considerations:**
- **Introspection Endpoint (`/connect/introspect`):**
  - Validate access tokens and return token metadata
  - Return: `active`, `scope`, `client_id`, `username`, `exp`, etc.
  - Require client authentication (only authorized resource servers can introspect)
  - Cache introspection results for performance
  
- **Revocation Endpoint (`/connect/revoke`):**
  - Allow clients to revoke access tokens and refresh tokens
  - Revoke all related tokens in token family (if refresh token revoked)
  - Audit log for token revocations
  
- **Admin Features:**
  - View active tokens per user
  - Revoke specific tokens or all tokens for a user
  - Token audit log (issued, used, revoked)

**Reference:**
- RFC 7662: https://datatracker.ietf.org/doc/html/rfc7662
- RFC 7009: https://datatracker.ietf.org/doc/html/rfc7009

---

## 5. Device Flow (RFC 8628)

**Goal:** Support authentication for input-constrained devices (smart TVs, IoT devices, CLI tools).

**Description:** Implement OAuth 2.0 Device Authorization Grant (Device Flow) to allow users to authenticate on devices that don't have a web browser or keyboard.

**User Experience:**
1. Device displays a code (e.g., "ABCD-1234") and URL (e.g., "https://idp.example.com/device")
2. User visits URL on their phone/computer and enters the code
3. User authenticates and approves the device
4. Device receives access token and refresh token

**Implementation Considerations:**
- Endpoints: `/connect/device_authorization`, `/connect/token` (with device_code grant)
- Device code generation and storage (short expiration, e.g., 10 minutes)
- User code should be human-friendly (short, easy to type)
- Polling configuration (interval, slow down responses)
- Admin UI to view pending device authorizations
- Security: Rate limiting, code expiration, max retry attempts

**Reference:** https://datatracker.ietf.org/doc/html/rfc8628

---

## 6. Stepped-Up Authentication & ACR Values

**Goal:** Support different authentication levels for sensitive operations.

**Description:** Implement Authentication Context Class Reference (ACR) values to allow clients to request specific authentication assurance levels. Users may be asked to re-authenticate or use stronger authentication for sensitive operations.

**Use Cases:**
- Banking app requires re-authentication for wire transfers
- Admin operations require MFA even if user is already logged in
- Different authentication levels: password-only (acr=1), password+MFA (acr=2), biometric (acr=3)

**Implementation Considerations:**
- Support standard ACR values (e.g., `urn:mace:incommon:iap:silver`, `urn:mace:incommon:iap:gold`)
- Define custom ACR levels:
  - `level1`: Password authentication
  - `level2`: Password + TOTP MFA
  - `level3`: Password + Hardware key (WebAuthn)
- Track authentication timestamp and level in session
- Force re-authentication if ACR not met or session too old
- Include `acr` claim in ID token
- Admin configuration for ACR policies per client

**OIDC Spec Reference:** https://openid.net/specs/openid-connect-core-1_0.html#acrSemantics

---

## 7. WebAuthn / Passwordless Authentication

**Goal:** Support FIDO2/WebAuthn for passwordless and phishing-resistant authentication.

**Description:** Implement WebAuthn authentication to allow users to sign in using biometrics (fingerprint, Face ID), security keys (YubiKey), or platform authenticators instead of passwords.

**Implementation Considerations:**
- Register WebAuthn credentials for users
- Store credential public keys in database
- Implement WebAuthn ceremony for registration and authentication
- Support multiple credentials per user (YubiKey + Face ID)
- Fallback to password if WebAuthn not available
- Admin option to enforce WebAuthn for specific roles
- UI for managing registered authenticators

**Libraries:**
- Fido2NetLib (ASP.NET Core): https://github.com/passwordless-lib/fido2-net-lib

---

## 8. Audit Log UI & Advanced Reporting

**Goal:** Provide comprehensive audit trail with searchable UI and reporting capabilities.

**Description:** Expand the audit logging foundation from Phase 3.10 into a full-featured audit log viewer with advanced filtering, search, and reporting.

**Features:**
- **Audit Log Viewer:**
  - Search by user, action type, entity, date range
  - Filter by severity (info, warning, error)
  - Export to CSV/JSON for compliance reporting
  - Drill-down to view full event details
  
- **Reports:**
  - User login activity report
  - Failed login attempts report
  - Client creation/modification report
  - Permission changes report
  - Compliance reports (GDPR, SOC2, etc.)
  
- **Alerting:**
  - Email/webhook alerts for suspicious activity
  - Multiple failed login attempts
  - Privilege escalation attempts
  - Client configuration changes

---

## 9. Content Security Policy (CSP)

**Goal:** Enhance the application's security posture by mitigating Cross-Site Scripting (XSS) and other code injection attacks.

**Description:** Implement a Content Security Policy (CSP) header for the `Web.IdP` application. This policy will define a whitelist of trusted content sources (scripts, stylesheets, images, fonts, etc.) that the browser is allowed to load and execute. Any content from sources not explicitly allowed will be blocked by the browser.

**Implementation Considerations:**
- Start with a strict policy and gradually relax it as needed, using report-only mode initially.
- Identify all legitimate sources for scripts, styles, images, and other assets, including those from third-party libraries (e.g., Cloudflare Turnstile, Vue.js, Vite).
- Configure the CSP header in `Program.cs` or via middleware.

---

## 2. User Email Verification

**Goal:** Improve account security and data quality by ensuring that registered email addresses are valid and owned by the user.

**Description:** Implement an email verification flow for new user registrations. After a user registers, an email containing a unique verification link will be sent to their provided address. The user will not be able to fully log in or use certain features until their email address has been successfully verified by clicking this link.

**Implementation Considerations:**
- Extend the `ApplicationUser` entity with a property like `EmailConfirmed`.
- Generate a unique, time-limited token for email verification.
- Create an email template for the verification link.
- Implement a Razor Page (`/Account/VerifyEmail`) to handle the verification link and confirm the user's email.
- Modify the login flow to check the `EmailConfirmed` status.
