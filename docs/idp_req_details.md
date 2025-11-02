# AI Agent Task: Build a Hybrid Authentication Identity Provider (IdP)

## Project Overview

- **Project Name:** HybridAuthIdP
- **Objective:** Create a robust, production-ready IdP supporting local and legacy authentication.
- **Technology Stack:** .NET 9, ASP.NET Core, EF Core, PostgreSQL, OpenIddict 6.x, Vue 3, Docker.
- **UI Tech:** Razor Pages with Bootstrap 5 (User-Facing), Vue 3 with Tailwind CSS (Admin UI).
- **Architecture:** Clean Architecture with TDD for critical logic.

---

## Coding Standards and Best Practices

### Use Constants for Magic Strings

**CRITICAL:** All role names, claim types, scope names, and other string identifiers MUST be defined as constants to prevent typos and improve maintainability.

**Implementation:**

- All authentication/authorization constants are centralized in `Core.Domain.Constants.AuthConstants`
- This includes:
  - **Role Names:** `AuthConstants.Roles.Admin`, `AuthConstants.Roles.User`
  - **Claim Types:** `AuthConstants.Claims.PreferredUsername`, `AuthConstants.Claims.Department`
  - **OIDC Scopes:** `AuthConstants.Scopes.OpenId`, `AuthConstants.Scopes.Email`, etc.
  - **Default Admin Credentials:** `AuthConstants.DefaultAdmin.Email`, `AuthConstants.DefaultAdmin.Password`
  - **Resource Identifiers:** `AuthConstants.Resources.ResourceServer`

**Usage Example:**

```csharp
// ✅ CORRECT - Using constants
[Authorize(Roles = AuthConstants.Roles.Admin)]
public class AdminController : ControllerBase { }

await userManager.AddToRoleAsync(user, AuthConstants.Roles.Admin);

identity.AddClaim(new Claim(AuthConstants.Claims.Department, department));

// ❌ WRONG - Magic strings (error-prone)
[Authorize(Roles = "Admin")]  // Typo risk!
await userManager.AddToRoleAsync(user, "admin");  // Case mismatch!
identity.AddClaim(new Claim("deparment", department));  // Typo!
```

**Benefits:**

- Prevents typos and case sensitivity issues
- Enables IntelliSense/autocomplete
- Single source of truth for all identifiers
- Easier refactoring and maintenance
- Compile-time checking instead of runtime errors

---

## Development Workflow

1.  **Implement Sub-Phase:** I will complete one sub-phase at a time.
2.  **Verify:** I will perform the outlined verification steps.
3.  **Request Approval:** I will ask for your approval to proceed.
4.  **Commit:** Upon approval, I will commit the changes with a descriptive message.
5.  **Proceed:** I will then move to the next sub-phase.

---

## Testing Best Practices

### Process Management

- **Terminate Processes Between Tests:** After completing verification of a phase, stop any running `dotnet.exe` processes before starting the next phase's verification.
  - Use `taskkill /F /IM dotnet.exe /T` on Windows or `pkill -9 dotnet` on Linux/macOS to ensure ports are freed and no stale processes interfere with subsequent tests.
  - This prevents port conflicts and ensures a clean test environment.

### UI Testing

- **Playwright MCP for Automated UI Tests:** Use the Playwright Model Context Protocol (MCP) integration to automate browser-based verification steps.
  - Playwright can navigate to pages, fill forms, click buttons, and verify page content/state.
  - Example workflow for OIDC flow testing:
    1. Navigate to the protected resource (e.g., `/Account/Profile`).
    2. Verify redirect to IdP login page.
    3. Fill login credentials and submit.
    4. Verify redirect to consent page and approve.
    5. Verify final redirect back to the client with authenticated session and claims displayed.
  - Use `mcp_playwright_browser_*` tools to script these interactions and capture page snapshots for validation.

---

## Phase 0: Project Scaffolding and Foundation

**Goal:** Establish the solution structure, Docker environment, and foundational elements.

**Verification:** Solution builds, Docker starts, i18n is configured.

**Agent Verification:** "Phase 0 is complete. I will now commit the work. **May I proceed to Phase 1.1?**"

---

## Phase 1: Local Account OIDC Core (Razor Pages & Bootstrap)

- **1.1: Database & Identity Setup:** Configure PostgreSQL, define user entities, create initial DB schema.
  - **Verification:** DB is created with Identity schema.
  - **Agent Question:** "Phase 1.1 is complete. **May I proceed to Phase 1.2?**"
- **1.2: OpenIddict Core Config:** Integrate OpenIddict services and seed default scopes/roles.
  - **Verification:** OpenIddict tables are created and seeded.
  - **Agent Question:** "Phase 1.2 is complete. **May I proceed to Phase 1.3?**"
- **1.3: User Registration Page:** Implement the user registration Razor Page and backend logic.
  - **Verification:** A new user can be created via the UI.
  - **Agent Question:** "Phase 1.3 is complete. **May I proceed to Phase 1.4?**"
- **1.4: Login & Logout Pages:** Implement OIDC-compliant login and logout Razor Pages.
  - **Verification:** A registered user can log in and out.
  - **Agent Question:** "Phase 1.4 is complete. **May I proceed to Phase 1.5?**"
- **1.5: OIDC Client & Consent Page:** Configure a test client and the OIDC consent Razor Page.
  - **Verification:** The OIDC consent flow is functional.
  - **Agent Question:** "Phase 1.5 is complete. **May I proceed to Phase 1.6?**"
- **1.6: Test Client Integration:** Create and configure an MVC client to test the IdP.
  - **Verification:** The Test Client can authenticate against the IdP.
  - **Agent Question:** "Phase 1.6 is complete. **May I proceed to Phase 1.7?**"
- **1.7: Cloudflare Turnstile:** Add optional CAPTCHA protection to login/registration pages.
  - **Verification:** Turnstile widget appears when enabled.
  - **Agent Question:** "Phase 1 is complete. **May I proceed to Phase 2.1?**"

---

## Phase 2: JIT Provisioning & Hybrid Auth (TDD-Driven)

- **2.1: TDD Setup for JIT Provisioning:** Define interfaces (`ILegacyAuthService`, `IJitProvisioningService`) and create failing unit tests.
  - **Verification:** Failing tests for user creation/updates exist and fail as expected.
  - **Agent Question:** "Phase 2.1 is complete. **May I proceed to Phase 2.2?**"
- **2.2: Implement JIT Provisioning Service:** Write the service logic to make the TDD tests pass.
  - **Verification:** All JIT provisioning unit tests pass.
  - **Agent Question:** "Phase 2.2 is complete. **May I proceed to Phase 2.3?**"
- **2.3: Integrate JIT Provisioning:** Create a mock `LegacyAuthService` and integrate the JIT flow into the login page.
  - **Verification:** Login with a legacy user provisions a new account in the IdP.
  - **Agent Question:** "Phase 2.3 is complete. **May I proceed to Phase 2.4?**"

### Quick E2E (Playwright) — Legacy + JIT

Dev-only flow for rapid verification (no real legacy API needed):

- Legacy stub password: `LegacyDev@123`
- Username: any email (e.g., `jane@example.com`)

Steps:

1. Start both apps (IdP + TestClient) with HTTPS profiles.
2. Open TestClient at `https://localhost:7001` and click “Login”.
3. On IdP login (`https://localhost:7035/Account/Login`), enter the email and password above.
4. On the consent screen, click “Allow Access”.
5. You should land on TestClient’s Profile page and see claims including your email.

Notes:

- Turnstile is disabled by default. If enabled, complete the CAPTCHA on login.
- After testing, stop processes to keep the environment clean.

Optional commands (PowerShell):

```powershell
# Stop any prior dotnet processes
taskkill /F /IM dotnet.exe /T

# Start IdP (https)
cd .\Web.IdP
dotnet run --launch-profile https

# In another terminal: start TestClient (https)
cd ..\TestClient
dotnet run --launch-profile https
```

Cleanup after verification:

```powershell
taskkill /F /IM dotnet.exe /T
```

### Negative E2E checks (deliberate failures)

Add these quick verifications to ensure failure paths are correct:

1. Invalid legacy password

- Steps: Start both apps → go to IdP Login via TestClient → enter any email (e.g., `jane@example.com`) and a wrong password (not `LegacyDev@123`) → click Login.
- Expect: Remain on IdP Login; an error message “Invalid login attempt.” is shown; no consent screen appears.

1. Optional: Deny consent

- Steps: Login successfully → on consent page click “Deny”.
- Expect: The client receives an access denied error and the user is not authenticated.

- **2.4: Custom Claims Factory:** Implement a claims factory to add custom claims from the user profile.
  - **Verification:** Custom claims appear in the user's token/cookie after login.
  - **Details:**
    - A custom `MyUserClaimsPrincipalFactory` ensures the `preferred_username` claim is present on the Identity principal (from Email/UserName).
    - The JIT provisioning service enriches users with basic claims such as `name` and `department` when provisioning/updating.
    - Token emission updated so `preferred_username` and `department` are included in BOTH the access token and the identity token (so the TestClient cookie/principal contains them and they render in the Profile view).
    - E2E updated to assert `preferred_username` and `department` (value `IT`) are visible on the TestClient Profile page after consent.
  - **Agent Question:** "Phase 2 is complete. **May I proceed to Phase 3.1?**"

---

## Phase 3: Admin API & Management UI (Vue 3 & Tailwind)

> **Vue.js MPA Architecture Note:**  
> This phase uses a **Multi-Page Application (MPA)** structure for Vue.js, powered by Vite and the `Vite.AspNetCore` library. Each functional area (e.g., Admin Portal, User Self-Service) has its own entry point and is loaded on specific Razor Pages. See `docs/idp_vue_mpa_structure.md` for detailed configuration and usage instructions.

- **3.1: Secure Admin API Foundation:** Create API controllers and secure them with an 'admin' role policy.
  - **Verification:** API endpoints return 401/403 unless the user is an authenticated admin.
  - **Agent Question:** "Phase 3.1 is complete. **May I proceed to Phase 3.2?**"
- **3.2: API Implementation:** Implement CRUD for OIDC clients and scopes. Remove hardcoded test client.
  - **Verification:** Admin APIs for clients/scopes are fully functional.
  - **Agent Question:** "Phase 3.2 is complete. **May I proceed to Phase 3.3?**"
- **3.3: Vue.js Scaffolding:** Configure Vite.AspNetCore and Tailwind CSS. Set up the basic Vue app structure.
  - **MPA Setup:**
    - Install `Vite.AspNetCore` NuGet package.
    - Configure `vite.config.js` with MPA entry points (e.g., `admin: './src/admin/main.js'`).
    - Set `PackageDirectory: "ClientApp"` in `appsettings.Development.json`.
    - Register Vite services in `Program.cs` with `AddViteServices()` and `UseViteDevelopmentServer()`.
    - Enable tag helpers in `_ViewImports.cshtml`: `@addTagHelper *, Vite.AspNetCore`.
  - **Verification:** A basic Vue page is served correctly by the application. The Vite dev server starts automatically in development.
  - **Agent Question:** "Phase 3.3 is complete. **May I proceed to Phase 3.4?**"
- **3.4: Admin UI Implementation:** Build Vue components to consume the admin APIs for client/scope management.
  - **Security-First Architecture (Backend Routes):**
    - **CRITICAL:** Each admin feature MUST have its own Razor Page with server-side authorization.
    - Create separate Razor Pages: `/Admin/Clients` and `/Admin/Scopes` (not client-side routes).
    - Each Razor Page validates `[Authorize(Roles = AuthConstants.Roles.Admin)]` on every navigation.
    - Each page loads a focused Vue SPA for that specific feature only.
    - **Benefits:** Server-side auth check on every page load, granular permission control, audit trail, deep linking with proper authorization.
  - **MPA Configuration:**
    - Update `vite.config.js` with multiple entry points:
      - `admin-clients: './src/admin/clients/main.js'` for Client Management
      - `admin-scopes: './src/admin/scopes/main.js'` for Scope Management
    - Each Razor Page uses: `<script type="module" vite-src="~/src/admin/clients/main.js"></script>`
  - **Client Management Features:**
    - **Client Type Selection:** Administrators explicitly select client type when creating clients:
      - **Public Clients:** For SPAs, mobile apps, desktop apps that cannot securely store secrets
        - Validation: Cannot have a ClientSecret
        - Use cases: JavaScript SPAs, React Native apps, Electron apps
      - **Confidential Clients:** For server-side apps that can securely store secrets
        - Validation: MUST have a ClientSecret (enforced by backend API)
        - Use cases: ASP.NET Core web apps, Node.js servers, API gateways
    - **Validation Logic:**
      - Backend validates client type and secret combination during creation
      - Confidential clients without secrets are rejected with error: "Confidential clients must have a ClientSecret"
      - Public clients with secrets are rejected with error: "Public clients should not have a ClientSecret"
      - Client type field is disabled during edit (immutable after creation)
    - **UI Enhancements:**
      - Radio button selection for client type with descriptive labels
      - Client Secret field dynamically adjusts:
        - Required and enabled for Confidential clients
        - Disabled with helpful placeholder for Public clients
      - Real-time help text updates based on selected type
  - **Structure:**

    ```text
    Web.IdP/Pages/Admin/
    ├── Index.cshtml              → Dashboard/Overview
    ├── Clients.cshtml            → Client Management (loads clients Vue SPA)
    ├── Clients.cshtml.cs         → [Authorize(Roles = Admin)]
    ├── Scopes.cshtml             → Scope Management (loads scopes Vue SPA)
    └── Scopes.cshtml.cs          → [Authorize(Roles = Admin)]

    ClientApp/src/admin/
    ├── clients/
    │   ├── main.js              → Entry point for Clients page
    │   ├── ClientsApp.vue       → Root component
    │   └── components/          → ClientList, ClientForm (with type selection)
    └── scopes/
        ├── main.js              → Entry point for Scopes page
        ├── ScopesApp.vue        → Root component
        └── components/          → ScopeList, ScopeForm
    ```

  - **Verification:**
    - Each admin feature has its own Razor Page route (`/Admin/Clients`, `/Admin/Scopes`).
    - Server validates authorization on every page navigation.
    - The admin UI can create, read, update, and delete OIDC clients and scopes.
    - Client type selection appears in create form with validation
    - Confidential clients require secrets; public clients cannot have secrets
    - Direct URL access to `/Admin/Clients` requires authentication.
  - **Agent Question:** "Phase 3 is complete. **May I proceed to Phase 4.1?**"

---

## Phase 4: Dynamic Security Policies (TDD-Driven)

- **4.1: Internationalized Identity Errors:** Create a custom `IdentityErrorDescriber` to provide translated error messages.
  - **Verification:** Identity errors (e.g., 'Password too short') appear in the configured language.
  - **Agent Question:** "Phase 4.1 is complete. **May I proceed to Phase 4.2?**"
- **4.2: TDD for Dynamic Password Validator:** Write failing unit tests for configurable password policies (length, history, etc.).
  - **Verification:** Failing tests for the password validator exist and fail as expected.
  - **Agent Question:** "Phase 4.2 is complete. **May I proceed to Phase 4.3?**"
- **4.3: Implement Dynamic Password Validator:** Write the validator logic to make the TDD tests pass.
  - **Verification:** All password validator unit tests pass.
  - **Agent Question:** "Phase 4.3 is complete. **May I proceed to Phase 4.4?**"
- **4.4: API & UI for Policies:** Build the API and Vue UI for an admin to manage security policies.
  - **MPA Note:** Use the same Admin MPA entry point (e.g., `src/admin/main.js`) and add routing or a new section for security policy management.
  - **Verification:** An admin can view and update security policies via the UI.
  - **Agent Question:** "Phase 4.4 is complete. **May I proceed to Phase 4.5?**"
- **4.5: Integrate Policy System:** Register the new services and add password expiration checks.
  - **Verification:** The system correctly enforces the configured password policies during login and password changes.
  - **Agent Question:** "Phase 4 is complete. **May I proceed to Phase 5.1?**"

---

## Phase 5: Production Hardening

- **5.1: Email Service:** Implement a real email service (e.g., SMTP) and an admin UI to manage its settings.
  - **MPA Note:** Email service settings UI can be part of the Admin MPA or a separate section within the Admin app.
  - **Verification:** The system can send emails for features like password reset.
  - **Agent Question:** "Phase 5.1 is complete. **May I proceed to Phase 5.2?**"
- **5.2: Secret Management:** Define and implement a secure strategy for production secrets (e.g., env variables, Docker Secrets).
  - **Verification:** Sensitive data is loaded from the secret store, not appsettings.
  - **Agent Question:** "Phase 5.2 is complete. **May I proceed to Phase 5.3?**"
- **5.3: Redis Integration:** Configure Redis for caching and as a store for OpenIddict.
  - **Verification:** The application uses Redis for caching and OpenIddict data.
  - **Agent Question:** "Phase 5.3 is complete. **May I proceed to Phase 5.4?**"
- **5.4: Background Token Cleanup:** Integrate Quartz.NET to periodically clean up expired tokens from the database.
  - **Verification:** A background job for token cleanup is registered and runs.
  - **Agent Question:** "Phase 5.4 is complete. **May I proceed to Phase 5.5?**"
- **5.5: Auditing & Health Checks:** Integrate Serilog for structured logging and add a health check endpoint.
  - **Verification:** Logs are structured JSON; `/healthz` endpoint reports status of DB and Redis.
  - **Agent Question:** "Phase 5 is complete. **May I proceed to Phase 6.1?**"

---

## Phase 6: User Account Management

> **Vue.js MPA Architecture Note:**  
> This phase introduces a **User Self-Service MPA** for account management features. Create a new entry point (e.g., `src/account-manage/main.js`) in `vite.config.js` for user-facing account pages, separate from the Admin app. See `docs/idp_vue_mpa_structure.md` for guidance.

- **6.1: Account Management UI:** Create protected Razor Pages for users to manage their accounts.
  - **MPA Setup:** Add `accountManage: './src/account-manage/main.js'` to `vite.config.js` inputs. Use `<script type="module" vite-src="~/src/account-manage/main.js"></script>` in the user account Razor Pages.
  - **Verification:** An authenticated user can access the `/Account/Manage` section.
  - **Agent Question:** "Phase 6.1 is complete. **May I proceed to Phase 6.2?**"
- **6.2: Change Password:** Implement the change password feature for authenticated users.
  - **Verification:** A user can change their own password.
  - **Agent Question:** "Phase 6.2 is complete. **May I proceed to Phase 6.3?**"
- **6.3: Forgot Password Flow:** Implement a secure 'Forgot Password' flow via email.
  - **Verification:** A user can reset their password by clicking a link sent to their email.
  - **Agent Question:** "Phase 6.3 is complete. **May I proceed to Phase 6.4?**"
- **6.4: Login Activity View:** Display recent sign-in events for the current user.
  - **Verification:** A user can see a list of their recent login attempts.
  - **Agent Question:** "Phase 6 is complete. The project is finished. Congratulations!"

---

## Future Enhancements

The following features are documented separately and can be implemented after the core project is complete:

- **Multi-Factor Authentication (MFA):** See `docs/idp_mfa_req.md` for detailed TOTP-based MFA implementation requirements, including user enrollment, login flow integration, and account recovery.
- **Content Security Policy (CSP):** See `docs/idp_future_enhancements.md` for CSP hardening guidelines.
- **User Email Verification:** See `docs/idp_future_enhancements.md` for email verification flow requirements.
