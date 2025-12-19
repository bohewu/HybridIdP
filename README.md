# HybridAuth IdP

A robust, enterprise-ready Identity Provider (IdP) built on .NET 10+ and OpenIddict, featuring a hybrid SSR (Razor Pages) and SPA (Vue.js) architecture.

> [!CAUTION]
> **SECURITY WARNING**: This project includes default development passwords (e.g., `YourStrong!Passw0rd`). **Never** use these in production. Always use Environment Variables or a Secret Manager to override sensitive settings.

---

## 📖 Documentation

- [Getting Started](docs/DEVELOPMENT_GUIDE.md)
- [Architecture Guide](docs/ARCHITECTURE.md)
- [Testing & Seeding](docs/TESTING.md)
- [Security Policy](docs/SECURITY.md)
- [Feature Overview](docs/FEATURES.md)
- [Deployment Guide](docs/DEPLOYMENT_GUIDE.md)

---

## 🏗️ Project Structure

- **Core.Domain**: Entities, constants, and core business models.
- **Core.Application**: Interfaces, DTOs, and application logic.
- **Infrastructure**: Data access (EF Core), external services, and security implementations.
- **Web.IdP**: The main Identity Provider host (Razor Pages + Vue.js Admin UI).
- **samples/**: Sample integration clients (M2M, Device Flow, Impersonation).
- **Tests/**: Comprehensive test suites (System, Integration, Unit).

---

## 🚀 Key Features

### 🔐 OpenID Connect & OAuth 2.0
Powered by **OpenIddict**:
- **Flows**: Authorization Code + PKCE, Client Credentials, Device Flow.
- **Security**: Refresh token rotation with configurable reuse leeway, secure session management.
- **Standard Discovery**: Full OIDC metadata at `/.well-known/openid-configuration`.

### 🛡️ Admin & Identity Management
- **RBAC**: Hierarchical roles and fine-grained permissions.
- **Impersonation**: Secure "Login As" feature for administrative support.
- **Observability**: Prometheus metrics and structured audit logging.
- **Bot Protection**: Integrated Cloudflare Turnstile support.

---

## 🛠️ Tech Stack

- **Backend**: .NET 10+, EF Core (SQL Server/PostgreSQL), OpenIddict, SignalR.
- **Frontend**: Vue.js 3, Vite, Tailwind CSS, Headless UI.
- **Testing**: xUnit, FluentAssertions, Playwright, Vitest.

---

## ⚡ Quick Start

1. **Prerequisites**: Docker Desktop, .NET 10 SDK+, Node.js.
2. **Infrastructure**:
   ```bash
   docker compose -f docker-compose.dev.yml up -d
   ```
3. **Run**:
   ```bash
   dotnet run --project Web.IdP
   ```
4. **Admin UI**:
   Navigate to `https://localhost:7035` and login with default dev credentials.

---

## ⚖️ License

Distributed under the **MIT License**. See `LICENSE` for more information.

---

## 🤖 AI-Assisted Development
This project was developed with significant assistance from advanced AI coding agents. While following industry standards, we recommend a secondary human security audit for production environments. Code is provided "AS IS".
