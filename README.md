# HybridIdP Admin

A comprehensive Identity Provider Administration System.

## Documentation
- [How to Test](docs/TESTING.md) 👈 **Start Here for Testing & Seeding**
- [Architecture](docs/ARCHITECTURE.md)
- [Features](docs/FEATURES.md)
- [Impersonation Guide](docs/Impersonation.md)
- [Deployment Guide](docs/DEPLOYMENT_GUIDE.md)

## Key Features

### 🔐 OpenID Connect & OAuth 2.0 Provider
Built on top of the robust **OpenIddict** framework, supporting modern security protocols:
- **Authorization Code Flow + PKCE**: Secure authentication for SPAs and mobile apps.
- **Client Credentials Flow**: Machine-to-Machine (M2M) communication.
- **Device Authorization Flow**: Input-constrained devices (IoT, Smart TVs, CLI tools).
- **Advanced Token Management**: Reference tokens, Refresh token rotation, and Revocation endpoints.
- **Standard Discovery**: Full OIDC discovery document (`/.well-known/openid-configuration`).

### 🛡️ Identity & Access Management (IAM)
- **Granular RBAC**: Role-Based Access Control with hierarchical permissions.
- **Dynamic Scope & Claims**: Manage scopes and authorized resources at runtime without code changes.
- **User Management dashboard**:
  - User provisioning and lifecycle management.
  - **User Impersonation**: "Login As" feature for administrative support and troubleshooting.
  - Account locking and security policy configuration.
  - Multi-factor authentication ready (extensible).

### 🌐 Compliance & Localization
- **Multi-language Support**: Built-in localization (i18n) for UI and Consent screens (currently supports `en-US` and `zh-TW`).
- **Audit Logging**: Comprehensive tracking of all administrative actions, login attempts, and security events.
- **Security Headers**: Pre-configured CSP, HSTS, and other security best practices.

### 🛠️ Developer Experience
- **UI Framework**: Tailwind CSS for all styling (no Bootstrap dependency). Razor Pages for server-rendered views, Vue.js for Admin SPA.
- **Docker Ready**: Includes `docker compose` for rapid deployment.
- **Data Seeding**: Automated environments setup for Development (Test Users, Clients, Resources).
- **Tests**: High coverage with Unit, Integration, and End-to-End (Playwright) tests.

## Disclaimer: AI-Assisted Development
> [!NOTE]
> **This project was developed with the significant assistance of advanced AI coding agents.**
> 
> While the architecture follows industry best practices and security standards (OAuth 2.0/OIDC), the codebase is provided "AS IS" without warranty of any kind. 
> - We recommend a thorough security audit before deploying to a high-risk production environment.
> - The AI agents have strived for clean, maintainable code, but human review is encouraged for critical security paths.

## Quick Start
1.  **Development Setup**:
    Start backend dependencies (SQL Server, Redis, etc.) using Docker Compose:
    ```bash
    docker compose -f docker-compose.dev.yml up -d
    ```
2.  Run the backend (Web.IdP).
3.  Run the frontend (ClientApp) or use the hosted Razor views.
4.  Login as Admin.
