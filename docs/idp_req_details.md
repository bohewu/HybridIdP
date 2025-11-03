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

### Content Security Policy (CSP) Compliance

**CRITICAL:** All Razor Pages (.cshtml files) MUST separate inline CSS and JavaScript into external files to comply with Content Security Policy best practices.

**Implementation Rules:**

1. **NO Inline `<style>` Tags:**
   - ❌ WRONG: `<style>body { color: red; }</style>`
   - ✅ CORRECT: Extract to `wwwroot/css/[page-name].css` and reference with `<link>`

2. **NO Inline `<script>` Tags (except `type="module"` for Vue.js):**
   - ❌ WRONG: `<script>alert('hello');</script>`
   - ✅ CORRECT: Extract to `wwwroot/js/[page-name].js` and reference with `<script src>`
   - ✅ EXCEPTION: Vue.js module scripts are allowed: `<script type="module" src="..."></script>`

3. **NO Inline Style Attributes:**
   - ❌ WRONG: `<div style="color: red; font-size: 14px;">Text</div>`
   - ✅ CORRECT: Create CSS class in external stylesheet: `.custom-text { color: red; font-size: 14px; }`

4. **NO Inline Event Handlers:**
   - ❌ WRONG: `<button onclick="doSomething()">Click</button>`
   - ✅ CORRECT: Use `addEventListener` in external JS file

**File Organization:**

```
wwwroot/
├── css/
│   ├── site.css              # Global styles
│   ├── admin-layout.css      # Admin layout specific styles
│   └── [page-name].css       # Page-specific styles
└── js/
    ├── site.js               # Global scripts
    ├── admin-layout.js       # Admin layout specific scripts
    └── [page-name].js        # Page-specific scripts
```

**Razor Page Pattern:**

```cshtml
@page
@model PageModel

<head>
    <!-- External CSS only -->
    <link href="~/css/page-name.css" rel="stylesheet" asp-append-version="true">
</head>

<body>
    <!-- Content with CSS classes only (no inline styles) -->
    <div class="custom-class">Content</div>
    
    <!-- External JS at the end -->
    <script src="~/js/page-name.js" asp-append-version="true"></script>
</body>
```

**Benefits:**

- Enables strict Content Security Policy headers
- Improves security by preventing XSS attacks
- Better caching and performance
- Easier maintenance and debugging
- Separation of concerns (structure/style/behavior)

---

## Development Workflow

1.  **Implement Sub-Phase:** I will complete one sub-phase at a time.
2.  **Verify:** I will perform the outlined verification steps.
3.  **Request Approval:** I will ask for your approval to proceed.
4.  **Commit:** Upon approval, I will commit the changes with a descriptive message.
5.  **Proceed:** I will then move to the next sub-phase.

---

## Testing Best Practices

### Development & Testing Setup

- **Complete Testing Guidelines:** See `docs/dev_testing_guide.md` for comprehensive instructions on:
  - Correct startup sequence (Database → IdP → Vite dev server)
  - Vite configuration and manual dev server management
  - Default locale (zh-TW) and language settings
  - Bootstrap 5 + Vue.js hybrid architecture
  - Common troubleshooting and cleanup procedures

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

## Phase 3: Admin Portal & OIDC Entity Management (Hybrid Architecture)

> **Hybrid Architecture Note:**  
> This phase uses a **hybrid architecture** combining the strengths of server-side rendering and client-side interactivity:
> - **Bootstrap 5 (CDN)**: Used for `_AdminLayout.cshtml` (sidebar, header, footer) - stable, no build required, works without Vite
> - **Vue.js 3 + Tailwind CSS (Vite)**: Used for main content areas within each admin page - interactive CRUD interfaces
> - **Backend Authorization**: Each Razor Page validates `[Authorize(Roles = AuthConstants.Roles.Admin)]` ensuring server-side security
> - **MPA Structure**: Each admin feature has its own Razor Page (route) with dedicated Vue.js SPA entry point
> 
> See `docs/idp_vue_mpa_structure.md` for MPA configuration and `docs/dev_testing_guide.md` for Bootstrap + Vue integration details.

### Overview

Phase 3 establishes the complete admin portal with a secure hybrid architecture:
- **Server-side security**: Razor Pages control routing and authorization (Backend → Frontend)
- **Client-side interactivity**: Vue.js SPAs handle CRUD operations with Tailwind CSS styling
- **Separation of concerns**: Bootstrap for stable layout structure, Vue for dynamic content areas

- **3.1: Admin Layout & Navigation:** Create role-based admin layout with navigation menu.
  - Goal: Professional admin portal with sidebar navigation and server-side authorization.
  - **Architecture Decision (COMPLETED)**: 
    - **Bootstrap 5 for Layout**: `_AdminLayout.cshtml` uses Bootstrap 5 (CDN) for outer frame
    - **Why Bootstrap?**: Stable, no build dependency, works without Vite dev server, SEO-friendly
    - **Vue.js for Content**: Main content areas use Vue.js SPAs with Tailwind CSS
  - Backend (COMPLETED):
    - ✅ `_AdminLayout.cshtml` with Bootstrap 5 (CDN: 5.3.2)
    - ✅ Bootstrap Icons (1.11.1) for navigation
    - ✅ Each admin page has `[Authorize(Roles = AuthConstants.Roles.Admin)]` - **server-side security**
    - ✅ Custom CSS embedded in layout (sidebar: 260px fixed, responsive mobile sidebar)
  - Frontend Layout Features (COMPLETED):
    - ✅ **Sidebar (Bootstrap 5)**:
      - Fixed 260px width with sticky navigation
      - Logo/brand section with gradient icon
      - Navigation sections: OIDC Management, Identity Management (Phase 4), Settings
      - Active route highlighting (server-side via `ViewData["Breadcrumb"]`)
      - User profile section at bottom with avatar and logout
    - ✅ **Responsive Design**:
      - Mobile: Sidebar hidden by default, slide-out with overlay on toggle
      - Hamburger menu button (`.navbar-toggler`)
      - Smooth CSS transitions for sidebar animation
    - ✅ **Main Content Area**:
      - Header with breadcrumbs (e.g., "Admin / Clients")
      - Content container with proper padding
      - Footer with copyright and links
    - ✅ **Bootstrap Components Used**:
      - Navigation: `.nav`, `.nav-link`, `.active`
      - Layout: `.container-fluid`, `.d-flex`, `.flex-column`
      - Utilities: `.text-muted`, `.border-top`, `.shadow-sm`
      - Icons: Bootstrap Icons (`.bi-speedometer2`, `.bi-grid`, etc.)
  - Navigation Structure:

    ```text
    Admin Portal (Server-side Routes)
    ├── /Admin (Index.cshtml) → Dashboard Vue SPA
    ├── /Admin/Clients (Clients.cshtml) → Clients Vue SPA
    ├── /Admin/Scopes (Scopes.cshtml) → Scopes Vue SPA
    ├── Identity Management (Phase 4)
    │   ├── /Admin/Users → Users Vue SPA
    │   └── /Admin/Roles → Roles Vue SPA
    └── Settings (Phase 5)
        └── /Admin/Settings → Settings Vue SPA
    
    Each route:
    - Has its own Razor Page with [Authorize(Roles = "Admin")]
    - Loads Bootstrap 5 layout (_AdminLayout.cshtml)
    - Mounts a dedicated Vue.js SPA for content area
    ```

  - **Verification (COMPLETED via Playwright MCP)**:
    - ✅ Admin user can access `/Admin` (authorized)
    - ✅ Non-admin users get 403 when accessing `/Admin/*` (server-side security)
    - ✅ Bootstrap 5 sidebar displays correctly (260px fixed width)
    - ✅ Navigation links work (Dashboard, Clients, Scopes)
    - ✅ Active menu item highlighted via server-side breadcrumb
    - ✅ Mobile responsive: sidebar collapses, hamburger menu works
    - ✅ User profile section displays at bottom with logout link
    - ✅ Layout works WITHOUT Vite dev server (Bootstrap CDN)
  - **Agent Question:** "Phase 3.1 is complete. **May I proceed to Phase 3.2?**"

- **3.2: Admin Dashboard:** Create overview page with statistics and quick actions.
  - Goal: Provide admins with at-a-glance metrics and shortcuts.
  - **Architecture Decision:** Rewrite Dashboard as a Vue.js SPA to maintain consistency with Admin Portal architecture (Bootstrap 5 for layout, Vue.js + Tailwind for interactive components).
  - Backend:
    - API: `GET /api/admin/dashboard/stats`
    - Returns: `{ clientCount, scopeCount, userCount, recentClients: Client[], recentActivity: Activity[] }`
  - Frontend:
    - **Razor Page:** `Pages/Admin/Index.cshtml` with Bootstrap 5 `_AdminLayout.cshtml` (sidebar, header, footer)
    - **Vue SPA Entry:** `ClientApp/src/admin/dashboard/main.js` loaded via Vite
    - **Vue Component:** `DashboardApp.vue` with Tailwind CSS styling
    - **Features:**
      - Stat cards with icons: Total Clients, Total Scopes, Total Users (API-driven)
      - Navigation cards: Quick links to Clients and Scopes management pages
      - Responsive grid layout (1 column mobile, 2-3 columns desktop)
      - Loading states and error handling
    - **Migration Note:** Replace current `Index.cshtml` Tailwind-only implementation with Vue.js SPA approach for maintainability
  - Verification:
    - Dashboard loads as Vue.js SPA within Bootstrap 5 layout
    - API returns accurate statistics
    - Stat cards display with proper Tailwind styling
    - Navigation cards link to `/Admin/Clients` and `/Admin/Scopes`
    - Responsive design works on mobile and desktop
  - **Agent Question:** "Phase 3.2 is complete. **May I proceed to Phase 3.3?**"

- **3.3: Secure Admin API Foundation:** Create API controllers and secure them with an 'admin' role policy.
  - **Verification:** API endpoints return 401/403 unless the user is an authenticated admin.
  - **Agent Question:** "Phase 3.3 is complete. **May I proceed to Phase 3.4?**"

- **3.4: Client CRUD API Implementation:** Implement full CRUD for OIDC clients. Remove hardcoded test client.
  - **Verification:** Admin APIs for clients are fully functional with validation.
  - **Agent Question:** "Phase 3.4 is complete. **May I proceed to Phase 3.5?**"

- **3.5: Vue.js Scaffolding:** Configure Vite.AspNetCore and Tailwind CSS. Set up the basic Vue app structure.
  - **MPA Setup:**
    - Install `Vite.AspNetCore` NuGet package.
    - Configure `vite.config.js` with MPA entry points (e.g., `admin: './src/admin/main.js'`).
    - Set `PackageDirectory: "ClientApp"` in `appsettings.Development.json`.
    - Register Vite services in `Program.cs` with `AddViteServices()` and `UseViteDevelopmentServer()`.
    - Enable tag helpers in `_ViewImports.cshtml`: `@addTagHelper *, Vite.AspNetCore`.
  - **Verification:** A basic Vue page is served correctly by the application. The Vite dev server starts automatically in development.
  - **Agent Question:** "Phase 3.5 is complete. **May I proceed to Phase 3.6?**"

- **3.6: Client Management UI Implementation:** Build Vue components to consume the admin APIs for client management.
  - **Security-First Architecture (Backend Routes):**
    - **CRITICAL:** Each admin feature MUST have its own Razor Page with server-side authorization.
    - ✅ Separate Razor Pages created: `/Admin/Clients.cshtml` and `/Admin/Scopes.cshtml`
    - ✅ Each Razor Page has `[Authorize(Roles = AuthConstants.Roles.Admin)]` in PageModel
    - ✅ Each page loads Bootstrap 5 `_AdminLayout.cshtml` (outer frame)
    - ✅ Each page mounts a dedicated Vue.js SPA (main content area)
    - **Benefits:** Server-side auth check on every page load, granular permission control, audit trail, deep linking with proper authorization.
  - **Hybrid Architecture Implementation:**
    - ✅ **Bootstrap 5 Layout (Outer Frame)**:
      - Loaded from `_AdminLayout.cshtml`
      - Includes sidebar, header with breadcrumbs, footer
      - Works WITHOUT Vite dev server (CDN-based)
      - Server-side route protection via Razor Pages
    - ✅ **Vue.js SPA (Main Content)**:
      - Each Razor Page has `<div id="app"></div>` mount point
      - Vite loads Vue SPA via `<script type="module" vite-src="~/src/admin/clients/main.js">`
      - Uses Tailwind CSS for styling (requires Vite dev server)
      - Handles all CRUD interactions via API calls
  - **MPA Configuration:**
    - Update `vite.config.js` with multiple entry points:
      - `admin-clients: './src/admin/clients/main.js'` for Client Management
      - `admin-scopes: './src/admin/scopes/main.js'` for Scope Management
      - `admin-dashboard: './src/admin/dashboard/main.js'` for Dashboard (Phase 3.2)
    - Each Razor Page uses: `<script type="module" vite-src="~/src/admin/[feature]/main.js"></script>`
  - **Client Management Features:**
    - **Application Type Selection:** Choose the platform type for the client:
      - **Web:** Traditional web applications (default) - server-side or SPA applications accessed via browser
      - **Native:** Desktop/mobile applications - apps with custom URI schemes for redirect handling
    - **Client Type Selection:** Administrators explicitly select client type when creating clients:
      - **Public Clients:** For SPAs, mobile apps, desktop apps that cannot securely store secrets
        - Validation: Cannot have a ClientSecret
        - Use cases: JavaScript SPAs (React, Vue, Angular), React Native apps, Electron apps, CLI tools
      - **Confidential Clients:** For server-side apps that can securely store secrets
        - Validation: MUST have a ClientSecret (enforced by backend API)
        - Use cases: ASP.NET Core web apps, Node.js servers, Java Spring apps, API gateways, backend services
    - **Comprehensive Permissions:** All OpenIddict-supported permissions grouped by category:
      - **Endpoints:** Authorization, Token, Logout, Introspection, Revocation, Device Authorization
      - **Grant Types:** Authorization Code, Client Credentials, Refresh Token, Device Code, Password (Resource Owner), Implicit
      - **Scopes:** OpenID, Profile, Email, Roles
    - **Validation Logic:**
      - Backend validates client type and secret combination during creation
      - Confidential clients without secrets are rejected with error: "Confidential clients must have a ClientSecret"
      - Public clients with secrets are rejected with error: "Public clients should not have a ClientSecret"
      - Client type and application type fields are disabled during edit (immutable after creation)
    - **UI Enhancements:**
      - Radio button selection for application type (Web/Native)
      - Radio button selection for client type (Public/Confidential)
      - Client Secret field dynamically adjusts:
        - Required and enabled for Confidential clients
        - Disabled with helpful placeholder for Public clients
      - Permissions grouped by category with descriptive labels (not just codes)
      - Real-time help text updates based on selected type
      - Helpful tip about minimum required permissions for OAuth/OIDC flows
  - **Structure:**

    ```text
    Hybrid Architecture Layout:
    
    /Admin/Clients (Server-side Route)
    ├── Clients.cshtml.cs
    │   └── [Authorize(Roles = AuthConstants.Roles.Admin)] ← Backend Security
    └── Clients.cshtml
        ├── Bootstrap 5 Layout (_AdminLayout.cshtml)
        │   ├── Sidebar (260px, fixed)
        │   ├── Header (Breadcrumbs: "Admin / Clients")
        │   └── Footer
        └── Vue.js Mount Point
            └── <div id="app"></div>
                └── ClientsApp.vue (Tailwind CSS)
                    ├── Client List (Table)
                    ├── Create/Edit Modal
                    ├── Delete Confirmation
                    └── API Integration
    
    File Structure:
    Web.IdP/Pages/Admin/
    ├── Index.cshtml              → Dashboard (Vue SPA with Tailwind)
    ├── Index.cshtml.cs           → [Authorize(Roles = Admin)]
    ├── Clients.cshtml            → Client Management mount point
    ├── Clients.cshtml.cs         → [Authorize(Roles = Admin)]
    ├── Scopes.cshtml             → Scope Management mount point
    ├── Scopes.cshtml.cs          → [Authorize(Roles = Admin)]
    
    Web.IdP/ClientApp/src/admin/
    ├── clients/
    │   ├── main.js               → Entry point for Clients SPA
    │   └── ClientsApp.vue        → Main component (Tailwind CSS)
    ├── scopes/
    │   ├── main.js               → Entry point for Scopes SPA
    │   └── ScopesApp.vue         → Main component (Tailwind CSS)
    └── dashboard/
        ├── main.js               → Entry point for Dashboard SPA (Phase 3.2)
        └── DashboardApp.vue      → Main component (Tailwind CSS)
    ```
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
    - ✅ Bootstrap 5 layout renders correctly (sidebar, breadcrumbs, footer)
    - ✅ Each admin feature has its own Razor Page route with `[Authorize]` attribute
    - ✅ Server validates authorization on every page navigation (403 for non-admin)
    - ✅ Vue.js SPAs mount correctly in `<div id="app">`
    - ✅ Tailwind CSS applies to Vue components (requires Vite dev server running)
    - ✅ The admin UI can create, read, update, and delete OIDC clients
    - ✅ Client type selection appears in create form with validation
    - ✅ Confidential clients require secrets; public clients cannot have secrets
    - ✅ Direct URL access to `/Admin/Clients` requires authentication
    - ✅ Admin navigation menu shows correct active state (server-side breadcrumb)
    - ✅ Hybrid architecture works: Bootstrap layout + Vue content
  - **Testing Reference:** See `docs/dev_testing_guide.md` for startup sequence (DB → IdP → Vite)
  - **Agent Question:** "Phase 3.6 is complete. **May I proceed to Phase 3.7?**"

- **3.7: Client List UX Hardening:** Improve client list display and edit form data loading.
    - Goal: Resolve correctness issues in the Clients list and edit form; expose summary data needed for large lists.
    - API: `GET /api/admin/clients`
      - Include `applicationType` and `redirectUrisCount` (do not expose actual URIs)
    - UI: Client list shows `redirectUrisCount` and uses server `type` (Public/Confidential)
    - UI: Edit flow fetches full client via `GET /api/admin/clients/{id}` to prefill Redirect URIs and Permissions
    - Acceptance:
      - Accurate “X redirect URI(s)” per client in list
      - Public/Confidential matches server value
      - Edit modal prefilled with Redirect URIs and Permissions
  - Verification:
    1. Create a client with at least one Redirect URI
    2. Return to list: count reflects the correct number
    3. Click Edit: fields are prefilled (URIs, permissions)
  - **Agent Question:** "Phase 3.7 is complete. **May I proceed to 3.8?**"

- **3.8: Scalability & Validation**
  - Goal: Prepare for production-scale datasets and consistent client-side validation.
  - Server pagination/filter/sort for clients
    - API: `GET /api/admin/clients?skip=0&take=25&search=&type=&sort=clientId:asc`
    - Response: `{ items: ClientSummary[], totalCount: number }`
    - DB indexing on `ClientId`, `ClientType`
  - UI data grid: paging, sorting, quick search, filters (TanStack Table / PrimeVue DataTable / Vuetify)
  - Client-side validation: Vee‑Validate + Zod; mirror backend rules (confidential requires secret; public forbids secret; per‑line URI validation)
  - OpenIddict server guidance:
    - Endpoints: authorize, token, logout; optional: introspect, revocation, device
    - Grants: Authorization Code + PKCE, Refresh Token; optional Client Credentials; avoid Implicit
    - Scopes: openid, profile, email, roles, API scopes
    - Security: HTTPS in prod, PKCE for public clients, choose JWT vs reference tokens deliberately
  - Acceptance:
    - Clients list supports paging/sorting/search and returns `totalCount`
    - Form has rich client-side validation with clear messages
    - OpenIddict configuration documented and aligned to enabled flows
  - **Agent Question:** "Phase 3.8 is complete. **May I proceed to Phase 3.9?**"

- **3.9: Scope & Resource Management (Basic CRUD)**
  - Goal: Enable admins to create, edit, delete, and manage OIDC scopes/resources dynamically.
  - Backend:
    - API endpoints: `GET /api/admin/scopes`, `GET /api/admin/scopes/{id}`, `POST /api/admin/scopes`, `PUT /api/admin/scopes/{id}`, `DELETE /api/admin/scopes/{id}`
    - DTOs: ScopeSummary (Id, Name, DisplayName, Description, Type [Identity/ApiResource], ClaimCount, ClientCount)
    - Validation: Name required, must be unique, alphanumeric with underscores/colons
    - Pagination/filtering: Similar to clients (skip/take/search/type/sort)
    - **Important:** This phase creates basic scope entities only; claims mapping comes in Phase 3.9A
  - Frontend:
    - Vue SPA: `ClientApp/src/admin/scopes/ScopesApp.vue`
    - Components: ScopeList, ScopeForm
    - Features: Create, edit, delete scopes; filter by type; search by name/display name
    - Validation: Zod schema for name format, required fields
    - UI shows scope usage count (how many clients use this scope)
  - Standard OIDC Scopes to Seed:
    - `openid` (Identity) - "OpenID Connect login"
    - `profile` (Identity) - "User profile information"
    - `email` (Identity) - "Email address"
    - `phone` (Identity) - "Phone number"
    - `address` (Identity) - "Physical address"
  - Structure:

    ```text
    Web.IdP/Pages/Admin/
    └── Scopes.cshtml                 → Scope Management
    └── Scopes.cshtml.cs              → [Authorize(Roles = Admin)]

    Web.IdP/Api/
    └── AdminController.cs            → Add scopes CRUD endpoints

    ClientApp/src/admin/
    └── scopes/
        ├── main.js                   → Entry point
        ├── ScopesApp.vue             → Root component with list/form
        └── components/
            ├── ScopeList.vue         → Displays scopes table
            └── ScopeForm.vue         → Create/edit modal (basic fields only)
    ```

  - Verification:
    - Admin can navigate to `/Admin/Scopes`
    - See standard OIDC scopes (openid, profile, email, phone, address)
    - Create new scope (e.g., `api:read`, display name "Read API Access")
    - Edit scope display name and description
    - Delete unused scope (validation: prevent deletion if used by clients)
    - Search/filter scopes by name or type
    - List shows usage count (number of clients using each scope)
  - **Agent Question:** "Phase 3.9 is complete. **May I proceed to Phase 3.9A (Claims Management)?**"

- **3.9A: Claims & Scope-to-Claims Mapping**
  - Goal: Define available user claims and map them to scopes for ID token/UserInfo endpoint.
  - **Concept:** When a client requests a scope (e.g., `profile`), the IdP needs to know which claims to include in the ID token. This phase creates:
    1. **Claim Definitions:** Pool of available claims with data types and sources
    2. **Scope → Claims Mapping:** Which claims are included when a scope is requested
    3. **User Attribute Mapping:** How to populate claim values from `ApplicationUser` properties

  - **Backend - Part 1: Claim Definitions**
    - Create `UserClaim` entity:
      - `Id`, `Name`, `DisplayName`, `Description`
      - `ClaimType` (standard JWT claim name, e.g., `email`, `family_name`)
      - `UserPropertyPath` (property on ApplicationUser, e.g., `Email`, `LastName`)
      - `DataType` (String, Boolean, Integer, DateTime, JSON)
      - `IsStandard` (true for OIDC standard claims, false for custom)
      - `IsRequired` (always include if user has value)
    - API endpoints: `GET /api/admin/claims`, `POST /api/admin/claims`, `PUT /api/admin/claims/{id}`, `DELETE /api/admin/claims/{id}`
    - DTOs: ClaimDefinitionDto (Id, Name, DisplayName, ClaimType, UserPropertyPath, DataType, IsStandard, ScopeCount)
    - Seed standard OIDC claims:

      ```csharp
      // OpenID Connect Standard Claims (from OIDC Core spec)
      { Name = "sub", DisplayName = "Subject Identifier", ClaimType = "sub", UserPropertyPath = "Id", DataType = "String", IsStandard = true, IsRequired = true },
      { Name = "name", DisplayName = "Full Name", ClaimType = "name", UserPropertyPath = "UserName", DataType = "String", IsStandard = true },
      { Name = "given_name", DisplayName = "Given Name", ClaimType = "given_name", UserPropertyPath = "FirstName", DataType = "String", IsStandard = true },
      { Name = "family_name", DisplayName = "Family Name", ClaimType = "family_name", UserPropertyPath = "LastName", DataType = "String", IsStandard = true },
      { Name = "middle_name", DisplayName = "Middle Name", ClaimType = "middle_name", UserPropertyPath = "MiddleName", DataType = "String", IsStandard = true },
      { Name = "nickname", DisplayName = "Nickname", ClaimType = "nickname", UserPropertyPath = "Nickname", DataType = "String", IsStandard = true },
      { Name = "preferred_username", DisplayName = "Preferred Username", ClaimType = "preferred_username", UserPropertyPath = "UserName", DataType = "String", IsStandard = true },
      { Name = "profile", DisplayName = "Profile URL", ClaimType = "profile", UserPropertyPath = "ProfileUrl", DataType = "String", IsStandard = true },
      { Name = "picture", DisplayName = "Picture URL", ClaimType = "picture", UserPropertyPath = "PictureUrl", DataType = "String", IsStandard = true },
      { Name = "website", DisplayName = "Website", ClaimType = "website", UserPropertyPath = "Website", DataType = "String", IsStandard = true },
      { Name = "email", DisplayName = "Email Address", ClaimType = "email", UserPropertyPath = "Email", DataType = "String", IsStandard = true },
      { Name = "email_verified", DisplayName = "Email Verified", ClaimType = "email_verified", UserPropertyPath = "EmailConfirmed", DataType = "Boolean", IsStandard = true },
      { Name = "phone_number", DisplayName = "Phone Number", ClaimType = "phone_number", UserPropertyPath = "PhoneNumber", DataType = "String", IsStandard = true },
      { Name = "phone_number_verified", DisplayName = "Phone Verified", ClaimType = "phone_number_verified", UserPropertyPath = "PhoneNumberConfirmed", DataType = "Boolean", IsStandard = true },
      { Name = "address", DisplayName = "Address", ClaimType = "address", UserPropertyPath = "Address", DataType = "JSON", IsStandard = true },
      { Name = "birthdate", DisplayName = "Birthdate", ClaimType = "birthdate", UserPropertyPath = "Birthdate", DataType = "String", IsStandard = true },
      { Name = "gender", DisplayName = "Gender", ClaimType = "gender", UserPropertyPath = "Gender", DataType = "String", IsStandard = true },
      { Name = "zoneinfo", DisplayName = "Time Zone", ClaimType = "zoneinfo", UserPropertyPath = "TimeZone", DataType = "String", IsStandard = true },
      { Name = "locale", DisplayName = "Locale", ClaimType = "locale", UserPropertyPath = "Locale", DataType = "String", IsStandard = true },
      { Name = "updated_at", DisplayName = "Updated At", ClaimType = "updated_at", UserPropertyPath = "UpdatedAt", DataType = "Integer", IsStandard = true },
      
      // Custom enterprise claims (examples)
      { Name = "department", DisplayName = "Department", ClaimType = "department", UserPropertyPath = "Department", DataType = "String", IsStandard = false },
      { Name = "job_title", DisplayName = "Job Title", ClaimType = "job_title", UserPropertyPath = "JobTitle", DataType = "String", IsStandard = false },
      { Name = "employee_id", DisplayName = "Employee ID", ClaimType = "employee_id", UserPropertyPath = "EmployeeId", DataType = "String", IsStandard = false },
      ```

  - **Backend - Part 2: Scope-to-Claims Mapping**
    - Create `ScopeClaim` join table:
      - `ScopeId` (FK to Scope entity)
      - `ClaimId` (FK to UserClaim entity)
      - `IsRequired` (always include if available, vs. optional)
      - `Order` (display order in consent screen)
    - API endpoints: `GET /api/admin/scopes/{id}/claims`, `POST /api/admin/scopes/{id}/claims`, `DELETE /api/admin/scopes/{scopeId}/claims/{claimId}`
    - DTOs: ScopeClaimMappingDto (ScopeId, ScopeName, Claims: ClaimDefinitionDto[])
    - Seed standard OIDC scope mappings:

      ```csharp
      // openid scope (required for OIDC)
      openid → [sub]

      // profile scope (OIDC Core spec section 5.4)
      profile → [name, family_name, given_name, middle_name, nickname, 
                 preferred_username, profile, picture, website, gender, 
                 birthdate, zoneinfo, locale, updated_at]

      // email scope
      email → [email, email_verified]

      // phone scope
      phone → [phone_number, phone_number_verified]

      // address scope
      address → [address]

      // Custom scopes (examples)
      department → [department, job_title]
      employee_info → [employee_id, department, job_title]
      ```

  - **Backend - Part 3: MyUserClaimsPrincipalFactory Enhancement**
    - Update `Infrastructure/Identity/MyUserClaimsPrincipalFactory.cs`:
      - Query `ScopeClaim` mappings based on requested scopes from authorization request
      - For each mapped claim, read value from `ApplicationUser` using `UserPropertyPath`
      - Transform data type (e.g., `EmailConfirmed` bool → `"true"` string for `email_verified` claim)
      - Add claims to `ClaimsPrincipal` for inclusion in ID token
    - Handle null/empty values gracefully (don't include claim if user property is null)
    - Support JSON serialization for complex claims (e.g., `address` claim)

  - **Frontend - Claims Management UI**
    - Vue SPA: `ClientApp/src/admin/claims/ClaimsApp.vue`
    - Features:
      - List all claim definitions with type, source property, and usage count
      - Create custom claims (name, display name, claim type, user property path, data type)
      - Edit claim definitions (cannot edit standard claims)
      - Delete unused custom claims
      - Show which scopes use each claim
    - Validation:
      - Claim type must be unique
      - User property path must exist on ApplicationUser (validate via reflection or list)
      - Cannot delete claims used by scopes

  - **Frontend - Scope-to-Claims Mapping UI**
    - Enhance `ScopeForm.vue` with claims mapping section:
      - Multi-select dropdown showing available claims
      - Display claims grouped by standard/custom
      - Checkbox to mark claim as "required" vs "optional"
      - Drag-and-drop to reorder claims (for consent screen display)
    - Show preview of ID token structure when scope is requested:

      ```json
      // When client requests scopes: openid, profile, email
      {
        "sub": "user-id-123",
        "name": "John Doe",
        "given_name": "John",
        "family_name": "Doe",
        "email": "john.doe@example.com",
        "email_verified": true
      }
      ```

  - **Extend ApplicationUser Entity:**
    - Add properties to support standard OIDC claims:

      ```csharp
      public class ApplicationUser : IdentityUser
      {
          // Existing properties...
          public string? FirstName { get; set; }
          public string? LastName { get; set; }
          public string? MiddleName { get; set; }
          public string? Nickname { get; set; }
          public string? ProfileUrl { get; set; }
          public string? PictureUrl { get; set; }
          public string? Website { get; set; }
          public string? Address { get; set; }  // JSON string
          public string? Birthdate { get; set; }  // ISO 8601 format
          public string? Gender { get; set; }
          public string? TimeZone { get; set; }
          public string? Locale { get; set; }
          public DateTime? UpdatedAt { get; set; }
          
          // Custom enterprise claims
          public string? Department { get; set; }
          public string? JobTitle { get; set; }
          public string? EmployeeId { get; set; }
      }
      ```

    - Create database migration for new columns

  - **Structure:**

    ```text
    Core.Domain/
    └── Entities/
        ├── UserClaim.cs              → Claim definition entity
        └── ScopeClaim.cs             → Scope-to-claims mapping entity

    Web.IdP/Api/
    └── AdminController.cs            → Add claims CRUD + scope mapping endpoints

    ClientApp/src/admin/
    ├── claims/
    │   ├── main.js
    │   ├── ClaimsApp.vue             → Claims list and CRUD
    │   └── components/
    │       ├── ClaimList.vue
    │       └── ClaimForm.vue
    └── scopes/
        └── components/
            └── ScopeClaimsMapper.vue → Claims mapping UI (embedded in ScopeForm)
    ```

  - **Verification:**
    - Admin navigates to `/Admin/Claims`
    - See list of standard OIDC claims (sub, name, email, etc.) and custom claims
    - Create custom claim: `department` → `department` claim type → `Department` property
    - Edit scope "profile" and see it includes claims: name, given_name, family_name, etc.
    - Add custom claim "department" to a custom scope "employee_info"
    - Remove claim from scope mapping
    - Delete unused custom claim
    - Cannot delete standard claim or claim used by scopes
    - Preview ID token structure shows correct claims when scope is selected
    - Standard OIDC scope mappings match spec (openid→sub, profile→name/family_name/etc., email→email/email_verified)
  - **Agent Question:** "Phase 3.9A is complete. Phase 3 complete! **May I proceed to Phase 4.1?**"

---

> **Note:** Phase 3.9B (Consent Screen Management & API Resource Scopes) has been moved to Phase 5.6 to allow focus on core identity management features first. The current implementation provides functional OIDC flows with basic scope management.

---

## Phase 4: Identity Management (Users, Roles & Permissions)

> **Critical for IdP:** This phase implements the core identity management system that allows administrators to manage users, roles, and permissions. This is essential for an enterprise-grade Identity Provider.

### Phase 4 Overview

Phase 4 establishes comprehensive user and role management with a modern admin interface. Supports multiple use cases: admin users, application registrars (users who can register their own OIDC clients), and custom role-based access control.

### Sub-Phases

- **4.1: User Management API & Data Model**
  - Goal: Create backend infrastructure for user CRUD operations with role assignments.
  - Backend:
    - Extend `ApplicationUser` entity with additional fields:
      - `Department`, `JobTitle`, `PhoneNumber`
      - `IsActive` (soft delete), `LastLoginDate`
      - `CreatedBy`, `CreatedAt`, `ModifiedBy`, `ModifiedAt`
    - API endpoints: `GET /api/admin/users`, `GET /api/admin/users/{id}`, `POST /api/admin/users`, `PUT /api/admin/users/{id}`, `DELETE /api/admin/users/{id}`
    - DTOs: UserSummary (Id, Email, UserName, Roles, Department, IsActive, LastLoginDate)
    - Support pagination/filtering/sorting: `?skip=0&take=25&search=&role=&isActive=true&sort=email:asc`
  - Validation:
    - Email required and must be valid format
    - Password requirements (min 8 chars, complexity)
    - Username must be unique
    - Cannot delete self or other admins without confirmation
  - Verification:
    - Create user via API with email and temporary password
    - Assign roles to user
    - List users with filtering by role and status
    - Update user details and role assignments
    - Soft delete (deactivate) users
  - **Status: ✅ COMPLETE**
  - **Agent Question:** "Phase 4.1 is complete. **May I proceed to Phase 4.2?**"

- **4.2: Role Management API**
  - Goal: Allow dynamic creation and management of custom roles beyond Admin/User.
  - Backend:
    - API endpoints: `GET /api/admin/roles`, `GET /api/admin/roles/{id}`, `POST /api/admin/roles`, `PUT /api/admin/roles/{id}`, `DELETE /api/admin/roles/{id}`
    - DTOs: RoleSummary (Id, Name, Description, UserCount, Permissions, IsSystem)
    - System roles (Admin, User) cannot be deleted, only modified
    - Custom roles: `ApplicationRegistrar`, `ScopeManager`, `AuditViewer`, etc.
  - Role Permissions (Granular):
    - `clients.read`, `clients.create`, `clients.update`, `clients.delete`
    - `scopes.read`, `scopes.create`, `scopes.update`, `scopes.delete`
    - `users.read`, `users.create`, `users.update`, `users.delete`
    - `roles.read`, `roles.create`, `roles.update`, `roles.delete`
    - `audit.read`, `settings.read`, `settings.update`
  - Verification:
    - Create custom role "ApplicationRegistrar" with permissions: `clients.read`, `clients.create`, `clients.update` (for their own clients)
    - Create custom role "ScopeManager" with full scope permissions
    - Assign custom role to user
    - Prevent deletion of system roles
    - List roles with user count
  - **Status: ✅ COMPLETE**
  - **Agent Question:** "Phase 4.2 is complete. **May I proceed to Phase 4.3?**"

- **4.3: Permission System Implementation**
  - Goal: Implement fine-grained permission checks across all admin APIs.
  - Backend:
    - Create `PermissionAuthorizationHandler` and `PermissionRequirement`
    - Add `[Authorize(Policy = "clients.create")]` to endpoints
    - Implement claims-based permissions (permissions stored as claims on user identity)
    - Create permission middleware for API endpoint protection
  - Permission Scoping:
    - Admin: Full access to everything
    - ApplicationRegistrar: Can only manage their own clients (add `OwnerId` field to Client entity)
    - ScopeManager: Can manage all scopes but not users/roles
    - Custom roles: Granular permission combinations
  - Client Ownership (for ApplicationRegistrar):
    - Add `OwnerId` field to OpenIddict Client entity (FK to ApplicationUser)
    - Update Client APIs to filter by OwnerId for non-admin users
    - `GET /api/admin/clients` returns only user's clients if not admin
    - `PUT/DELETE /api/admin/clients/{id}` validates ownership
  - Verification:
    - Admin can access all endpoints and see all clients
    - ApplicationRegistrar can create clients (owned by them) and manage only their own clients
    - ApplicationRegistrar cannot see or modify other users' clients
    - ScopeManager can manage scopes but not access user management
    - Permission denied returns proper 403 Forbidden
    - Admin sees "Owner" column in client list; can filter by owner
  - **Status: ✅ COMPLETE (Core Implementation)**
  - **Note:** Client ownership (OwnerId field) deferred - can be added when needed for ApplicationRegistrar role
  - **Agent Question:** "Phase 4.3 is complete. **May I proceed to Phase 4.4?**"

- **4.4: User Management UI**
  - Goal: Build comprehensive user management interface for administrators.
  - Frontend:
    - Vue SPA: `ClientApp/src/admin/users/UsersApp.vue`
    - Components: UserList, UserForm, RoleAssignment
    - Features:
      - List users with pagination/search/filter (by role, status)
      - Create user with email, password, role assignment
      - Edit user: update details, assign/remove roles, activate/deactivate
      - Delete (soft delete) users with confirmation
      - Password reset (generate temporary password)
      - Last login timestamp display
    - Validation: Zod schema for email format, password strength
  - UI Layout (Modern Admin Style):
    - Table view with avatar, email, roles badges, status indicator
    - Quick filters: Active/Inactive, Filter by role dropdown
    - Bulk actions: Activate/Deactivate selected users
    - Detail modal with tabs: Profile, Roles, Activity, Security
  - Structure:

    ```text
    Web.IdP/Pages/Admin/
    └── Users.cshtml                → User Management
    └── Users.cshtml.cs             → [Authorize(Policy = "users.read")]

    ClientApp/src/admin/
    └── users/
        ├── main.js                 → Entry point
        ├── UsersApp.vue            → Root component
        └── components/
            ├── UserList.vue        → User table with filters
            ├── UserForm.vue        → Create/edit modal
            └── RoleAssignment.vue  → Multi-select role picker
    ```

  - Verification:
    - Navigate to `/Admin/Users`
    - Create new user with ApplicationRegistrar role
    - Edit user to add/remove roles
    - Search users by email or name
    - Filter users by role and status
    - Deactivate/reactivate user accounts
  - **Agent Question:** "Phase 4.4 is complete. **May I proceed to Phase 4.5?**"

- **4.5: Role Management UI**
  - Goal: Build interface for creating and managing custom roles with permissions.
  - Frontend:
    - Vue SPA: `ClientApp/src/admin/roles/RolesApp.vue`
    - Components: RoleList, RoleForm, PermissionSelector
    - Features:
      - List roles with user count and permission summary
      - Create custom role with name, description
      - Assign permissions to role (multi-select tree/checklist)
      - Edit role permissions
      - Delete custom roles (prevent deletion if users assigned)
      - Show which users have each role
  - Permission Selector UI:
    - Grouped by category: Clients, Scopes, Users, Roles, Audit, Settings
    - Checkboxes: Read, Create, Update, Delete per category
    - Preset templates: "Read Only", "Manager", "Full Access"
  - Structure:

    ```text
    Web.IdP/Pages/Admin/
    └── Roles.cshtml                → Role Management
    └── Roles.cshtml.cs             → [Authorize(Policy = "roles.read")]

    ClientApp/src/admin/
    └── roles/
        ├── main.js                 → Entry point
        ├── RolesApp.vue            → Root component
        └── components/
            ├── RoleList.vue        → Roles table
            ├── RoleForm.vue        → Create/edit modal
            └── PermissionSelector.vue → Permission tree picker
    ```

  - Verification:
    - Navigate to `/Admin/Roles`
    - Create "ApplicationRegistrar" role with client.* permissions
    - Create "AuditViewer" role with audit.read permission only
    - Edit role to add/remove permissions
    - View users assigned to each role
    - Delete custom role (validation prevents if users exist)
  - **Agent Question:** "Phase 4.5 is complete. Phase 4 complete! **May I proceed to Phase 5.1?**"

---

## Phase 5: Dynamic Security Policies (TDD-Driven)

- **5.1: Internationalized Identity Errors:** Create a custom `IdentityErrorDescriber` to provide translated error messages.
  - **Verification:** Identity errors (e.g., 'Password too short') appear in the configured language.
  - **Agent Question:** "Phase 5.1 is complete. **May I proceed to Phase 5.2?**"
- **5.2: TDD for Dynamic Password Validator:** Write failing unit tests for configurable password policies (length, history, etc.).
  - **Verification:** Failing tests for the password validator exist and fail as expected.
  - **Agent Question:** "Phase 5.2 is complete. **May I proceed to Phase 5.3?**"
- **5.3: Implement Dynamic Password Validator:** Write the validator logic to make the TDD tests pass.
  - **Verification:** All password validator unit tests pass.
  - **Agent Question:** "Phase 5.3 is complete. **May I proceed to Phase 5.4?**"
- **5.4: API & UI for Policies:** Build the API and Vue UI for an admin to manage security policies.
  - **MPA Note:** Use the same Admin MPA entry point (e.g., `src/admin/main.js`) and add routing or a new section for security policy management.
  - **Verification:** An admin can view and update security policies via the UI.
  - **Agent Question:** "Phase 5.4 is complete. **May I proceed to Phase 5.5?**"
- **5.5: Integrate Policy System:** Register the new services and add password expiration checks.
  - **Verification:** The system correctly enforces the configured password policies during login and password changes.
  - **Agent Question:** "Phase 5.5 is complete. **May I proceed to Phase 5.6?**"

- **5.6: Consent Screen Management & API Resource Scopes** *(Moved from Phase 3.9B)*
  - Goal: Provide rich consent screen customization and support for API resource protection.
  - **Part 1: Consent Screen Customization**
    - **Concept:** When users authorize a client, they see a consent screen showing what data/permissions the client is requesting. This needs to be clear, translatable, and customizable.
    - Backend:
      - Add fields to `Scope` entity:
        - `ConsentDisplayName` (localized display name for consent screen)
        - `ConsentDescription` (what this permission allows the app to do)
        - `IconUrl` (optional icon for visual identification)
        - `IsRequired` (user cannot opt out if true - e.g., `openid` scope)
        - `DisplayOrder` (order on consent screen)
      - Add `Resources` table for localizing consent strings:
        - Key format: `Scope.{ScopeName}.ConsentDisplayName`, `Scope.{ScopeName}.ConsentDescription`
        - Support multiple languages (en-US, zh-TW, etc.)
    - Frontend (Admin):
      - Enhance `ScopeForm.vue` with consent customization fields
      - Multi-language editor for display name and description
      - Upload/select icon for scope
      - Toggle "Required" checkbox (prevent user from denying)
      - Preview consent screen appearance
    - Frontend (User-Facing):
      - Update `Consent.cshtml` to use localized scope descriptions
      - Group scopes by category (Profile Information, API Access, etc.)
      - Show icons next to each scope
      - Display helpful descriptions instead of technical scope names
      - Mark required scopes clearly (cannot be unchecked)
    - **Example Consent Screen:**

      ```text
      TestClient would like to:
      
      ✓ Know who you are (openid) [Required]
        Access your basic identity information
      
      ☐ Read your profile (profile)
        Access your name, picture, and other profile information
      
      ☐ Access your email (email)
        View your email address and verification status
      
      ☐ Access your company data (api:company:read)
        Read company records on your behalf
      ```

  - **Part 2: API Resource Scopes**
    - **Concept:** Beyond identity scopes (profile, email), IdP must support **API resource scopes** for protecting backend APIs (e.g., `api:read`, `api:write`, `inventory:manage`).
    - Backend:
      - Create `ApiResource` entity:
        - `Id`, `Name`, `DisplayName`, `Description`
        - `BaseUrl` (API base URL for documentation)
        - `Scopes` (collection of scopes belonging to this resource)
      - Example resources:
        - **Company API:** Scopes: `api:company:read`, `api:company:write`, `api:company:delete`
        - **Inventory API:** Scopes: `api:inventory:read`, `api:inventory:write`
        - **User Management API:** Scopes: `api:users:read`, `api:users:manage`
      - API endpoints: `GET /api/admin/resources`, `POST /api/admin/resources`, `PUT /api/admin/resources/{id}`, `DELETE /api/admin/resources/{id}`
      - API endpoints: `GET /api/admin/resources/{id}/scopes` (list scopes for a resource)
    - Frontend:
      - Vue SPA: `ClientApp/src/admin/resources/ResourcesApp.vue`
      - Create API resources with name, display name, base URL
      - Assign scopes to resources (e.g., `api:company:read` → "Company API" resource)
      - Visual grouping: Show scopes grouped by resource in client configuration
    - OpenIddict Integration:
      - Register API resources and scopes in OpenIddict
      - Configure audience claim for access tokens (includes resource identifier)
      - API resource scopes appear in client scope selection

  - **Part 3: Scope Authorization Policies (Whitelisting)**
    - **Concept:** Not all clients should be able to request all scopes. Admins need to whitelist which scopes each client can request.
    - Backend:
      - Add `ClientAllowedScopes` join table (already exists in OpenIddict, ensure proper management)
      - Validation: When client requests scopes during authorization, verify against whitelist
      - API: Update client creation/edit to include allowed scopes selection
    - Frontend:
      - In `ClientForm.vue`, add "Allowed Scopes" multi-select
      - Show available scopes grouped by:
        - Identity Scopes (openid, profile, email, phone, address)
        - API Resources (Company API, Inventory API, etc.)
        - Custom Scopes
      - Validation: At least `openid` must be selected for OIDC clients
    - Verification:
      - Create client and whitelist only `openid`, `profile`, `email` scopes
      - Attempt to request `api:company:read` scope → authorization denied
      - Update client to include `api:company:read` → authorization succeeds

  - **Structure:**

    ```text
    Core.Domain/
    └── Entities/
        └── ApiResource.cs            → API resource entity

    Web.IdP/Api/
    └── AdminController.cs            → Add resources CRUD endpoints

    Web.IdP/Pages/
    └── Consent.cshtml                → Enhanced with localized descriptions, icons, grouping

    ClientApp/src/admin/
    ├── resources/
    │   ├── main.js
    │   ├── ResourcesApp.vue          → API resources management
    │   └── components/
    │       ├── ResourceList.vue
    │       └── ResourceForm.vue
    └── scopes/
        └── components/
            └── ScopeConsentEditor.vue → Consent screen customization UI
    ```

  - **Verification:**
    - **Consent Screen:**
      - Admin edits scope "profile" to add localized description and icon
      - User sees localized consent screen with clear descriptions
      - Required scopes (openid) cannot be unchecked
      - Scopes grouped by category (Identity, API Access)
    - **API Resources:**
      - Admin creates "Company API" resource
      - Adds scopes: `api:company:read`, `api:company:write`, `api:company:delete`
      - Client configuration shows scopes grouped by resource
      - Access token includes audience claim for requested resources
    - **Scope Whitelisting:**
      - Client A whitelisted for `openid`, `profile`, `api:company:read`
      - Client A requests `api:inventory:read` → denied
      - Client B whitelisted for all scopes → can request any scope
  - **Agent Question:** "Phase 5.6 is complete. Phase 5 complete! **May I proceed to Phase 6.1?**"

---

## Phase 6: Production Hardening

- **6.1: Email Service:** Implement a real email service (e.g., SMTP) and an admin UI to manage its settings.
  - **MPA Note:** Email service settings UI can be part of the Admin MPA or a separate section within the Admin app.
  - **Verification:** The system can send emails for features like password reset.
  - **Agent Question:** "Phase 6.1 is complete. **May I proceed to Phase 6.2?**"
- **6.2: Secret Management:** Define and implement a secure strategy for production secrets (e.g., env variables, Docker Secrets).
  - **Verification:** Sensitive data is loaded from the secret store, not appsettings.
  - **Agent Question:** "Phase 6.2 is complete. **May I proceed to Phase 6.3?**"
- **6.3: Redis Integration:** Configure Redis for caching and as a store for OpenIddict.
  - **Verification:** The application uses Redis for caching and OpenIddict data.
  - **Agent Question:** "Phase 6.3 is complete. **May I proceed to Phase 6.4?**"
- **6.4: Background Token Cleanup:** Integrate Quartz.NET to periodically clean up expired tokens from the database.
  - **Verification:** A background job for token cleanup is registered and runs.
  - **Agent Question:** "Phase 6.4 is complete. **May I proceed to Phase 6.5?**"
- **6.5: Auditing & Health Checks:** Integrate Serilog for structured logging and add a health check endpoint.
  - **Verification:** Logs are structured JSON; `/healthz` endpoint reports status of DB and Redis.
  - **Agent Question:** "Phase 6 is complete. **May I proceed to Phase 7.1?**"

---

## Phase 7: User Self-Service & Account Management

> **Vue.js MPA Architecture Note:**  
> This phase introduces a **User Self-Service MPA** for account management features. Create a new entry point (e.g., `src/account-manage/main.js`) in `vite.config.js` for user-facing account pages, separate from the Admin app. See `docs/idp_vue_mpa_structure.md` for guidance.

- **7.1: Account Management UI:** Create protected Razor Pages for users to manage their accounts.
  - **MPA Setup:** Add `accountManage: './src/account-manage/main.js'` to `vite.config.js` inputs. Use `<script type="module" vite-src="~/src/account-manage/main.js"></script>` in the user account Razor Pages.
  - **Verification:** An authenticated user can access the `/Account/Manage` section.
  - **Agent Question:** "Phase 7.1 is complete. **May I proceed to Phase 7.2?**"
- **7.2: Change Password:** Implement the change password feature for authenticated users.
  - **Verification:** A user can change their own password.
  - **Agent Question:** "Phase 7.2 is complete. **May I proceed to Phase 7.3?**"
- **7.3: Forgot Password Flow:** Implement a secure 'Forgot Password' flow via email.
  - **Verification:** A user can reset their password by clicking a link sent to their email.
  - **Agent Question:** "Phase 7.3 is complete. **May I proceed to Phase 7.4?**"
- **7.4: Login Activity View:** Display recent sign-in events for the current user.
  - **Verification:** A user can see a list of their recent login attempts.
  - **Agent Question:** "Phase 7.4 is complete. The project is finished. Congratulations!"

---

## Future Enhancements

The following features are documented separately and can be implemented after the core project is complete:

- **Multi-Factor Authentication (MFA):** See `docs/idp_mfa_req.md` for detailed TOTP-based MFA implementation requirements, including user enrollment, login flow integration, and account recovery.
- **Content Security Policy (CSP):** See `docs/idp_future_enhancements.md` for CSP hardening guidelines.
- **User Email Verification:** See `docs/idp_future_enhancements.md` for email verification flow requirements.
