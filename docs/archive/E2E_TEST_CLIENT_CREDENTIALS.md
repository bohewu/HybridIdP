# E2E Test Client Credentials

This document contains the OAuth client credentials for E2E testing, created via the admin UI on November 16, 2025.

## Client Details

- **Client ID**: `testclient-public`
- **Display Name**: Test Client (Public)
- **Client Type**: Public (SPA, mobile apps, desktop apps that cannot securely store secrets)
- **Application Type**: Web

## Client Secret

⚠️ **Public clients do not have a client secret.** They use PKCE (Proof Key for Code Exchange) for secure authorization code flow without requiring a secret.

## Configuration

### Redirect URIs

- `https://localhost:7001/signin-oidc`

### Post Logout Redirect URIs

- `https://localhost:7001/signout-callback-oidc`

### Enabled Endpoints

- Authorization endpoint
- Token endpoint
- Logout endpoint

### Grant Types

- Authorization code
- Refresh token

### Permissions (Scopes in Config)

- OpenID
- Profile
- Email
- Roles

### Allowed Scopes

- OpenID (identity scope)
- Profile (identity scope)
- Email (identity scope)
- Roles (API resource scope)

## Usage

This client is configured for use with the TestClient application at `https://localhost:7001` for E2E session management testing.

To use this client, update `TestClient/Program.cs`:

```csharp
options.ClientId = "testclient-public";
options.UsePkce = true; // REQUIRED for public clients
options.ResponseType = "code";
// No ClientSecret for public clients
```

## Notes

- Public clients MUST use PKCE (Proof Key for Code Exchange) for security
- The authorization code flow with PKCE is suitable for SPAs, mobile apps, and applications that cannot securely store secrets
- This client uses the same redirect URIs as before: `https://localhost:7001/signin-oidc` and `https://localhost:7001/signout-callback-oidc`
