# AI Agent Task: Build a Hybrid Authentication Identity Provider (IdP)

## Project Overview

- **Project Name:** HybridAuthIdP
- **Objective:** Create a robust and production-ready Identity Provider (IdP) that supports both local user accounts and legacy system authentication.
- **Technology Stack:** .NET 9, ASP.NET Core, Entity Framework Core, **PostgreSQL**, OpenIddict 6.x, Vue 3, Docker
- **Architecture:** Clean Architecture
- **Key Requirements:**
    - Test-Driven Development (TDD) for critical business logic.
    - Internationalization (i18n) for `en-US` and `zh-TW`.
    - UI styling with **Tailwind CSS**.
    - Phased development with explicit confirmation points.
    - Use `dotnet new` for project scaffolding.
    - Use `docker compose` for container orchestration.

---

## Git Workflow

This project will be managed under Git version control. After each phase is successfully completed and approved, all changes for that phase will be committed to the repository. This creates a clean history and a checkpoint for each stage of the development process.

---

## Phase 0: Project Scaffolding and Foundation

**Goal:** Establish the solution structure using `dotnet new` templates, set up the Docker environment for PostgreSQL, and integrate the foundational elements for TDD and i18n.

**Definition of Done:**
- A Git repository is initialized.
- A solution with the correct project structure is created inside a `HybridIdP` folder.
- All projects build successfully.
- The Docker environment (including a PostgreSQL container) starts without errors.
- i18n services are registered and configured.

### Steps:

1.  **Initialize Git Repository:**
    - `git init`
    - Create a standard `.gitignore` file for .NET projects.
2.  **Create Solution and Projects:**
    - Create a root folder named `HybridIdP`.
    - Inside `HybridIdP`, create the solution and all required projects (`Core.Domain`, `Core.Application`, `Infrastructure`, `Web.IdP`, and test projects) using `dotnet new` commands.
    - Add all projects to the solution file.
3.  **Set up Docker Environment for PostgreSQL:**
    - Create `docker-compose.yml` in the root directory with services for the IdP, PostgreSQL, and Redis.
        ```yaml
        services:
          idp-service:
            build:
              context: .
              dockerfile: Web.IdP/Dockerfile
            ports:
              - "8080:80"
            depends_on:
              - db-service
              - redis-service
          db-service:
            image: postgres:latest
            environment:
              POSTGRES_DB: hybridauth_idp
              POSTGRES_USER: user
              POSTGRES_PASSWORD: password
            ports:
              - "5432:5432"
          redis-service:
            image: redis:alpine
            ports:
              - "6379:6379"
        ```
    - Create `Web.IdP/Dockerfile` using the `dotnet/sdk:9.0` and `dotnet/aspnet:9.0` base images.
4.  **Configure i18n in `Web.IdP/Program.cs`:**
    - Add `AddLocalization`, `AddMvc().AddViewLocalization()`, and `UseRequestLocalization` middleware.
    - Create a `Resources` folder in `Web.IdP`.

### Agent Verification for Phase 0:

- **Action:** Pause execution.
- **Question:** "Phase 0 (Project Scaffolding and Foundation) is complete and verified. The project structure is created, the solution builds, and the PostgreSQL-based Docker environment is running. An initial commit for Phase 0 will be made. **May I proceed to Phase 1 (Local Account OIDC Core)?**"

---

## Phase 1: Local Account OIDC Core

**Goal:** Implement a fully functional OIDC login/logout flow using PostgreSQL and establish the initial required data for the application.

**Definition of Done:**
- EF Core is configured to use PostgreSQL.
- Users can register, log in, and log out using local accounts.
- The OIDC consent screen is functional.
- A data seeding process creates essential initial data.

### Steps:

1.  **Add PostgreSQL Provider for EF Core:**
    - Install the `Npgsql.EntityFrameworkCore.PostgreSQL` NuGet package to the `Infrastructure` project.
2.  **Implement `ApplicationUser` & `DbContext`:**
    - `ApplicationUser` in `Core.Domain` should inherit from `IdentityUser<Guid>`.
    - `ApplicationDbContext.cs` in `Infrastructure` should inherit from `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>` and implement `IApplicationDbContext`.
3.  **Configure `Web.IdP/Program.cs`:**
    - Configure the `DbContext` to use PostgreSQL with `options.UseNpgsql()` and the correct connection string.
    - Add and configure `ASP.NET Core Identity` and `OpenIddict` services.
    - Create a hardcoded test client registration for this phase.
    - Run initial database migrations: `dotnet ef migrations add InitialCreate --project Infrastructure --startup-project Web.IdP` and `dotnet ef database update --project Infrastructure --startup-project Web.IdP`.
4.  **Create UI Pages & Resources:** Create Razor Pages (`Login.cshtml`, `Logout.cshtml`, `Register.cshtml`, `Consent.cshtml`) with corresponding `.resx` resource files.
5.  **Implement UI Backend Logic:** Implement `UserManager.CreateAsync` for registration, `SignInManager.PasswordSignInAsync` for login, and OIDC flows for logout and consent.
6.  **Integrate Cloudflare Turnstile (CAPTCHA):** Add a disabled-by-default Turnstile integration to the login and registration pages.
7.  **Implement Initial Data Seeding:** Create a data seeding service that runs on application startup to ensure default OpenIddict scopes (e.g., `openid`, `profile`, `roles`) and a default "admin" role exist in the database.
8.  **Create a Test Client:** `dotnet new mvc -n TestClient` and configure it to use the IdP for authentication.

---

## Phase 2: JIT Provisioning and Hybrid Authentication (TDD-Driven)

**Goal:** Implement Just-In-Time (JIT) user provisioning from a legacy system, driven by Test-Driven Development (TDD).

**Definition of Done:**
- TDD unit tests for the JIT provisioning service are written and passing.
- The login process authenticates against a legacy service.
- New users from the legacy system are automatically provisioned in the IdP.
- Custom claims from the legacy system are included in the user's token.

### Steps:

1.  **Define Interfaces and DTOs in `Core.Application`:** `ILegacyAuthService` and `IJitProvisioningService`.
2.  **[TDD Red] Create Failing Tests in `Tests.Application.UnitTests`:** Create `JitProvisioningServiceTests.cs`. Mock `UserManager<ApplicationUser>`. Write tests for new user creation and existing user updates.
3.  **[TDD Green] Implement `JitProvisioningService` in `Infrastructure`:** Write the business logic to make the TDD tests pass.
4.  **Implement `LegacyAuthService` in `Infrastructure`:** Create a mock implementation that simulates validating credentials against a legacy system.
5.  **Modify Login Logic in `Web.IdP/Pages/Account/Login.cshtml.cs`:** Replace `SignInManager.PasswordSignInAsync` with a new flow: call `_legacyAuthService.ValidateAsync`, then `_jitProvisioningService.ProvisionUserAsync`, then `signInManager.SignInAsync`.
6.  **Implement Custom Claims Factory:** Create `MyUserClaimsPrincipalFactory` to add custom claims from the user profile during cookie/token creation. Register it in `Program.cs`.

---

## Phase 3: Admin API and Management UI

**Goal:** Build the secure backend API and frontend UI for managing OIDC clients, scopes, and claims.

**Definition of Done:**
- CRUD APIs for clients, scopes, and claims are implemented and secured.
- The hardcoded test client is removed.
- A basic Vue.js admin UI, styled with Tailwind CSS, is set up.

### Steps:

1.  **Create API Controllers:** `ClientsController.cs`, `ScopesController.cs`, `ClaimsController.cs`.
2.  **Implement API Endpoints:** Implement full CRUD functionality for managing OpenIddict applications and scopes.
3.  **Secure Admin APIs:** Apply an authorization policy (e.g., requiring an `admin` role) to all controllers under `/Api/Admin/`.
4.  **Remove Hardcoded Client:** Remove the test client creation logic from `Program.cs`.
5.  **Set up Vue 3 MPA with Tailwind CSS:** Configure `Vite.AspNetCore` and Tailwind CSS. Create a `ClientApp` directory following the architecture in `idp_vue_mpa_structure.md`.

---

## Phase 4: Dynamic Security Policies (TDD-Driven)

**Goal:** Implement a flexible security policy system, driven by TDD, and ensure all Identity-related error messages are internationalized.

**Definition of Done:**
- TDD unit tests for the dynamic password validator are written and passing.
- Identity error messages are internationalized.
- Password complexity, history, and expiration can be configured dynamically by an administrator.

### Steps:

1.  **Create `MultiLingualIdentityErrorDescriber` in `Infrastructure/Identity`:** Inherit from `IdentityErrorDescriber` and use `IStringLocalizer` to provide translated error messages.
2.  **[TDD Red] Create Failing Tests in `Tests.Infrastructure.UnitTests`:** Create `DynamicPasswordValidatorTests.cs`. Mock `IPolicyService`. Write tests to verify password length, history, and other custom rules.
3.  **[TDD Green] Implement `DynamicPasswordValidator` in `Infrastructure/Identity`:** Implement `IPasswordValidator<ApplicationUser>`. Inject `IPolicyService` and write logic to pass the TDD tests.
4.  **Create Admin UI & API for Policies:** Build a `PoliciesController.cs` and a corresponding Vue UI for admins to view and update the system's security policies.
5.  **Register Dynamic Validator in `Program.cs`:** Register the new services via `.AddPasswordValidator<DynamicPasswordValidator>()` and `.AddErrorDescriber<MultiLingualIdentityErrorDescriber>()`.
6.  **Implement Password Expiration Logic:** Add checks for `PasswordMinAge` and `PasswordMaxAge` during password change and login flows.

---

## Phase 5: Production Hardening

**Goal:** Add the final infrastructure pieces to make the IdP production-ready, including a secure secret management strategy.

**Definition of Done:**
- A real email service is implemented and configurable.
- A secure strategy for managing production secrets is in place.
- Redis is integrated for caching and OpenIddict stores.
- A background job cleans up expired tokens.
- Structured auditing and health checks are available.

### Steps:

1.  **Implement Email Service:** Create `SmtpEmailSender` and an admin UI to manage SMTP settings. Use DI to switch between `FakeEmailSender` and `SmtpEmailSender`.
2.  **Define and Implement Secret Management:** Establish a strategy for production secrets (e.g., environment variables, Docker Secrets, or Azure Key Vault). Refactor existing code to load secrets securely.
3.  **Integrate Redis:** Configure `AddStackExchangeRedisCache` and tell OpenIddict to `.UseRedis()`.
4.  **Integrate Token Cleanup:** Configure Quartz.NET with `AddQuartz()` and OpenIddict's `.UseQuartz()`.
5.  **Implement Enhanced Auditing:** Integrate **Serilog** for structured JSON logging. Create an `IAuditLogger` service to log critical security events.
6.  **Add Health Check Endpoint:** Use `AddHealthChecks()` to add checks for the database and Redis. Map the endpoint at `/healthz`.

---

## Phase 6: User Account Management

**Goal:** Provide a self-service portal for users to manage their own account, including password recovery.

**Definition of Done:**
- Authenticated users can access a dedicated account management page.
- Users can change their password.
- A secure "Forgot Password" flow is implemented.
- Users can view their recent login activity.

### Steps:

1.  **Create Account Management UI:** Create protected Razor Pages under `Pages/Account/Manage/`.
2.  **Implement Change Password Logic:** Use `UserManager.ChangePasswordAsync` in the backend for the change password page.
3.  **Implement Forgot Password Flow:** Create the necessary pages and backend logic to allow users to reset their password securely via an email link.
4.  **Implement Login Activity View:** Create a service to query the structured audit logs and display recent sign-in events for the current user.

---

## Future Enhancements

This section documents features that are planned but deferred for a future release.

- **Multi-Factor Authentication (MFA):** Requirements are detailed in `idp_mfa_req.md`.
- **Other Enhancements:** Further desirable features, such as a Content Security Policy (CSP) and User Email Verification, are detailed in `idp_future_enhancements.md`.