# HybridAuth IdP Sample Projects

This directory contains sample applications that demonstrate how to integrate with the HybridAuth IdP using various OAuth 2.0 and OpenID Connect flows.

> [!CAUTION]
> **DEVELOPMENT ONLY SECRETS**
> The sample projects in this directory contain hardcoded client IDs and secrets (e.g., `testclient-m2m` / `m2m-test-secret-2024`). These are intended for local development and demonstration purposes **ONLY**. Never use these credentials or patterns in a production environment.

## Available Samples

1.  **TestClient**: A standard OIDC web application demonstrating the Authorization Code Flow with PKCE.
2.  **TestClient.M2M**: A console application demonstrating the Client Credentials flow for machine-to-machine communication.
3.  **TestClient.Device**: A console application demonstrating the Device Authorization Flow (for input-constrained devices).
4.  **TestClient.Impersonation**: A sample demonstrating the Token Exchange / Impersonation flow.

## Security Best Practices for Production

When moving from these samples to a production application:

1.  **Secrets Management**: Use secure secret stores like Azure Key Vault, AWS Secrets Manager, or HashiCorp Vault. Never hardcode secrets in source code or `appsettings.json`.
2.  **Redirect URIs**: Ensure that redirect URIs are strictly validated and use HTTPS.
3.  **HTTPS**: Always use TLS 1.2+ for all communication between your application and the IdP.
4.  **Token Storage**: Store access and refresh tokens securely. For web apps, use secure, HTTP-only cookies.
5.  **Scope Minimization**: Only request the scopes that your application absolutely needs (Principle of Least Privilege).

## Local Setup

To run most samples, you need the HybridAuth IdP running locally at `https://localhost:7035`.

```bash
# Start the IdP
cd Web.IdP
dotnet run

# In another terminal, run a sample
cd samples/TestClient
dotnet run
```
