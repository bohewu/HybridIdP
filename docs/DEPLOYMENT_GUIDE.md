# HybridIdP Deployment Guide

This guide covers the deployment of HybridIdP using Docker Compose. The easiest way to deploy is using the **interactive setup wizard**, which handles configuration, security secrets, and certificates automatically.

## Table of Contents
1. [Prerequisites](#prerequisites)
2. [Quick Start (Recommended)](#quick-start-interactive-wizard)
3. [Deployment Modes](#deployment-modes)
4. [Verification](#verification)
5. [Advanced / Manual Configuration](#advanced--manual-configuration)
   - [Manual .env Setup](#manual-env-setup)
   - [Manual Certificate Generation](#manual-certificate-generation)
   - [Database & Redis Options](#database--redis-options)

---

## Prerequisites
- **Docker** & **Docker Compose** (v2+)
- **OpenSSL** (optional, for generating certificates via wizard)

---

## Quick Start (Interactive Wizard)

We provide an interactive script to generate your `.env` configuration, secure passwords, and OpenIddict certificates.

### 1. Run the Setup Script

**Windows (PowerShell):**
```powershell
cd deployment
.\setup-env.ps1
```

**Linux/macOS (Bash):**
```bash
cd deployment
chmod +x setup-env.sh
./setup-env.sh
```

### 2. Follow the Prompts
The wizard will ask for:
1.  **Deployment Mode**: Choose the architecture that fits your infrastructure (see [Deployment Modes](#deployment-modes)).
2.  **Database**: Choose SQL Server or PostgreSQL (Internal Docker or External connection).
3.  **Security**: It will auto-generate strong passwords for DBs and Certs.
4.  **Certificates**: It can generate self-signed certificates using OpenSSL automatically.

### 3. Start the Application
The script will output the exact command to run based on your choices. Typically:

```bash
docker compose -f docker-compose.nginx.yml --env-file .env up -d
```

---

## Deployment Modes

| Mode | Description | Components |
|------|-------------|------------|
| **A. Nginx Reverse Proxy** (Recommended) | Fully compliant setup with SSL termination. | `nginx` + `idp` + `db` + `redis` |
| **B. Internal / Load Balancer** | For when you have an existing robust LB (AWS ALB, Azure Gateway) handling SSL. | `idp` + `db` + `redis` |
| **C. Split-Host + Internal DB** | Advanced security. Reverse Proxy is on Host A, IdP+DB on Host B. | `nginx-gateway` + `idp` + `db` + `redis` |
| **D. Split-Host + External DB** | **Production Preferred**. RP on Host A, IdP on Host B, DB on dedicated server. | `nginx-gateway` + `idp` + `redis` |

### Split-Host Security
If you choose Mode C or D, the wizard will ask for:
- **Internal IP**: The IP of the host machine to bind the gateway to (e.g., `192.168.1.20`). This prevents exposure on public interfaces.
- **Proxy Host IP**: The IP of your external Reverse Proxy (Host A) to trust for forwarding headers.

> [!TIP]
> For a deep dive into hardening the Split-Host architecture (DMZ v.s. Trusted Zone), read the [Split-Host Security Guide](../deployment/SPLIT_HOST_SECURITY.md).

---

## Verification

1.  **Check Containers**:
    ```bash
    docker compose ps
    ```
2.  **Health Check**:
    ```bash
    curl -k http://localhost:8080/health  # Port depends on mode (80/443/8080)
    ```
3.  **OpenID Discovery**:
    Navigate to `https://your-domain/.well-known/openid-configuration` and ensure it returns JSON data.

---

## Advanced / Manual Configuration

If you prefer not to use the wizard, follow these steps.

### Manual .env Setup
1.  Copy `.env.example` to `.env`.
2.  Fill in the values. NOTABLE variables:
    -   `ASPNETCORE_ENVIRONMENT`: `Production`
    -   `DATABASE_PROVIDER`: `SqlServer` or `PostgreSQL`
    -   `Proxy__Enabled`: `true` if behind any proxy.
    -   `Proxy__KnownProxies`: CIDR ranges or specific IPs of your proxy.

### Manual Certificate Generation
HybridIdP requires two certificates: **Encryption** and **Signing**.

```bash
mkdir -p deployment/certs
cd deployment/certs

# Generate Encryption Cert
openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -nodes -keyout encryption.key -out encryption.crt -subj "/CN=HybridIdP Encryption"
openssl pkcs12 -export -out encryption.pfx -inkey encryption.key -in encryption.crt -password pass:YOUR_PASSWORD

# Generate Signing Cert
openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -nodes -keyout signing.key -out signing.crt -subj "/CN=HybridIdP Signing"
openssl pkcs12 -export -out signing.pfx -inkey signing.key -in signing.crt -password pass:YOUR_PASSWORD
```

> [!IMPORTANT]
> You must update `ENCRYPTION_CERT_PASSWORD` and `SIGNING_CERT_PASSWORD` in your `.env` file to match the passwords used above.

### Database & Redis Options
-   **Redis**: Set `Redis__Enabled=false` to use In-Memory caching (not recommended for multi-instance production).
-   **External DB**: Update `ConnectionStrings__...` with your real server details.


