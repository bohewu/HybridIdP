# E2E Tests (Playwright)

This folder contains Playwright tests for the HybridIdP solution.

Prerequisites:

- Node.js 18+
- Browsers: `npm run install:browsers`
- The apps are running locally:
  - IdP: <https://localhost:7035>
  - TestClient: <https://localhost:7001>

Install dependencies:

```powershell
cd .\e2e
npm install
npm run install:browsers
```

Run tests (headless):

```powershell
npm test
```

Run tests (headed):

```powershell
npm run test:headed
```

Notes:

- Self-signed HTTPS is accepted via `ignoreHTTPSErrors` in config.
- Tests assume IdP and TestClient are already started.

Test user credentials:

- 基本測試用戶：`admin@hybridauth.local` / `Admin@123` (Admin 角色)
