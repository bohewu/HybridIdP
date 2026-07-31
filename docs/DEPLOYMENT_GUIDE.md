# HybridIdP Deployment Guide

This guide covers the deployment of HybridIdP using Docker Compose. The easiest way to deploy is using the **interactive setup wizard**, which handles configuration, security secrets, and certificates automatically.

## Table of Contents
1. [Prerequisites](#prerequisites)
2. [Quick Start (Recommended)](#quick-start-interactive-wizard)
3. [Deployment Modes](#deployment-modes)
4. [Production Configuration Contract](#production-configuration-contract)
5. [One-Time Operational First Administrator](#one-time-operational-first-administrator)
6. [Verification](#verification)
7. [Advanced / Manual Configuration](#advanced--manual-configuration)
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

| Mode | Compose file | Description | Components |
|------|--------------|-------------|------------|
| **A. Nginx Reverse Proxy** (Recommended) | `docker-compose.nginx.yml` | Fully compliant setup with SSL termination. | `nginx` + `idp` + `db` + `redis` |
| **B. Internal / Load Balancer** | `docker-compose.internal.yml` | For when an existing LB handles SSL. | `idp` + `db` + `redis` |
| **C. Split-Host Direct** | `docker-compose.splithost.yml` | Reverse proxy is on Host A; IdP, DB, and Redis are on Host B. | `idp` + `db` + `redis` |
| **D. Split-Host Local Nginx** | `docker-compose.splithost-nginx.yml` | Gateway and IdP are isolated on Host B. | `nginx-gateway` + `idp` + `db` + `redis` |
| **E. Split-Host Local Nginx + External DB** | `docker-compose.splithost-nginx-nodb.yml` | Gateway and IdP are on Host B; the database is external. | `nginx-gateway` + `idp` + `redis` |

### Split-Host Security
If you choose Mode C or D, the wizard will ask for:
- **Internal IP**: The IP of the host machine to bind the gateway to (e.g., `192.168.1.20`). This prevents exposure on public interfaces.
- **Proxy Host IP**: The IP of your external Reverse Proxy (Host A) to trust for forwarding headers.

### IP Allowlist Configuration (Split-Host)
For Split-Host deployments, you need to configure the Nginx IP allowlist:

1. **Copy the example file**:
   ```bash
   cd deployment/nginx
   cp ip-allowlist.conf.example ip-allowlist.conf
   ```

2. **Edit `ip-allowlist.conf`** to allow your reverse proxy's IP:
   ```nginx
   allow 192.168.1.10;    # Your Host A IP
   deny all;
   ```

> [!NOTE]
> The `ip-allowlist.conf` file is gitignored, so your customizations won't be overwritten by updates.

> [!TIP]
> For a deep dive into hardening the Split-Host architecture (DMZ v.s. Trusted Zone), read the [Split-Host Security Guide](../deployment/SPLIT_HOST_SECURITY.md).

---

## Production Configuration Contract

Use the setup scripts to create the operator-managed values in `deployment/.env`, or start from `.env.example` and supply the values through an approved secret-management process. Production compose does not provide a database-password fallback.

All modes require non-empty `DATABASE_PROVIDER`, `ConnectionStrings__SqlServerConnection`, `ConnectionStrings__PostgreSqlConnection`, `ENCRYPTION_CERT_PASSWORD`, and `SIGNING_CERT_PASSWORD`. `DATABASE_PROVIDER` selects the provider used by the IdP, but both database connection-string variables are required by the compose contract because both are passed into the container.

| Compose file | Additional required non-empty values |
|--------------|--------------------------------------|
| `docker-compose.internal.yml` | `ConnectionStrings__RedisConnection`, `MSSQL_SA_PASSWORD`, `POSTGRES_PASSWORD` |
| `docker-compose.nginx.yml` | `ConnectionStrings__RedisConnection`, `MSSQL_SA_PASSWORD`, `POSTGRES_PASSWORD` |
| `docker-compose.splithost.yml` | `ConnectionStrings__RedisConnection`, `MSSQL_SA_PASSWORD`, `POSTGRES_PASSWORD`, `INTERNAL_IP`, `PROXY_HOST_IP` |
| `docker-compose.splithost-nginx.yml` | `ConnectionStrings__RedisConnection`, `MSSQL_SA_PASSWORD`, `POSTGRES_PASSWORD`, `INTERNAL_IP` |
| `docker-compose.splithost-nginx-nodb.yml` | `INTERNAL_IP` |

The external-database mode sets its Redis connection to the local Redis service, so it does not require `ConnectionStrings__RedisConnection`, `MSSQL_SA_PASSWORD`, or `POSTGRES_PASSWORD`. The other four modes contain both database services; therefore their SQL Server and PostgreSQL initialization passwords are required even when only one provider is selected.

Absent or empty required values fail compose validation before `deploy-idp.sh` can pull, build, or start a service. Diagnostics identify the missing variable name; they must never reveal its supplied value. Correct the named input in the operator-managed environment file and rerun the command.

### Data-Service Network Exposure

By default, MSSQL, PostgreSQL, and Redis are available only on their Docker networks: no database or Redis port is published on the host. Named `mssql-data`, `postgres-data`, `redis-data`, and (where used) `dataprotection-keys` volumes persist independently of this host-port choice.

For temporary local diagnostics on a mode that includes the internal MSSQL, PostgreSQL, and Redis services, explicitly include the local override after the selected compose file:

```bash
cd deployment
docker compose -f docker-compose.nginx.yml -f docker-compose.local-ports.yml --env-file .env up -d
```

`docker-compose.local-ports.yml` exposes the existing `mssql-service` (`1433`), `postgres-service` (`5432`), and `redis-service` (`6379`) on loopback only (`127.0.0.1`). It is an opt-in override, not a production default, and is not applicable to `docker-compose.splithost-nginx-nodb.yml`, which has no internal database services. Remove the override from the compose command to return to the default private posture. Do not delete or recreate volumes when adding or removing this override.

### Image and Local-Source Deployments

For GHCR, `deployment/docker-compose.ghcr-image.yml` requires `IDP_IMAGE` and sets `pull_policy: always`. Use the existing deployment flow with a non-secret image placeholder or an approved image tag:

```bash
cd deployment
./deploy-idp.sh --source ghcr --image ghcr.io/<owner>/hybrididp-idp-service:main
```

After validation, the script pulls the selected `idp-service` image and runs `up -d --no-build`, which recreates the service when the image changes. Update the host by rerunning this flow; do not reset the database or remove volumes to update an image. For local source, the same script retains its local build behavior: `--source local` runs `up -d --build`, while `--no-cache` performs the local no-cache build before startup.

The IdP applies EF Core migrations during startup before normal seed processing. Back up the database before deploying an image that contains schema changes and allow the new container to complete startup before sending traffic. The Email MFA attempt-limit migration is additive: it adds a non-null counter with a default of `0`, preserves existing users and credentials, and does not require a database reset or volume replacement.

---

## One-Time Operational First Administrator

This optional capability is disabled by default and is for a genuinely fresh deployment only. It is not a migration, reset, repair, or account-recovery mechanism. Existing deployments leave it disabled and need no migration, database reset, or marker removal. It is also distinct from the fixed privileged Development/Test fixture (`SeedData__PrivilegedTestAdminBootstrap__Enabled`), which remains test-only, and from normal post-login administrator management.

### Prepare Host-Side Configuration

Use an approved secret-management process on the deployment host. Generate 32 random bytes out of band, encode them as unpadded base64url, and use the resulting 43-character value as the raw bootstrap token. Compute its SHA-256 digest in hexadecimal and choose a short, absolute UTC expiry. Keep the raw token only in the operator's protected, in-memory or secret-manager workflow; configuration stores only the digest and expiry.

Set these host-side values for the one-time window. The committed [`.env.example`](../deployment/.env.example) intentionally contains placeholders only.

```text
OperationalAdminBootstrap__Enabled=true
OperationalAdminBootstrap__TokenSha256Digest=<SHA256_HEX_DIGEST>
OperationalAdminBootstrap__ExpiresAtUtc=<ABSOLUTE_UTC_EXPIRY>
```

Do not put the raw token, its digest, or the administrator password in source control. Do not use a command that echoes any of them, or place them in a URL, query string, shell history, process list, or application logs. The expiry must be an absolute UTC value; a missing, expired, or non-UTC expiry leaves the capability unavailable.

The endpoint requires HTTPS. If TLS terminates at a reverse proxy, configure only the actual trusted proxy IPs or CIDRs through the existing `Proxy__Enabled` and `Proxy__KnownProxies` model so forwarded HTTPS is accepted through the forwarding middleware's known-proxy/known-network trust set. Do not treat a caller source IP as authorization: it is rate-limiting defense in depth only.

### Perform the One-Time Request

From a protected operator client, make one HTTPS `POST` request to `/api/operational-bootstrap/admin` with `Content-Type: application/json` and the raw token only in this dedicated header:

```text
X-HybridAuth-Bootstrap-Token: <43_CHARACTER_BASE64URL_TOKEN>
```

The JSON body contains operator-chosen, unique values and must be handled without exposing its password:

```json
{
  "email": "<UNIQUE_ADMIN_EMAIL>",
  "name": "<UNIQUE_ADMIN_NAME>",
  "password": "<OPERATOR_CHOSEN_ADMIN_PASSWORD>"
}
```

For SQL Server and PostgreSQL, the operation uses a serializable, all-or-nothing transaction. A successful request creates exactly one initial Admin account and the system-owned completion marker. A replay, a request after expiry, an existing or ambiguous identity state, invalid prerequisite Admin role, or an uncertain outcome remains closed and returns the generic unavailable result. A successful response is `201` with `operational_bootstrap_completed`; the generic unavailable response is `404` with `operational_bootstrap_unavailable`.

Never retry a request when the response is lost or uncertain. Do not attempt to make the capability reusable by deleting identities, resetting the database, removing the marker, reusing the token, or promoting another account. The marker is system-owned and cannot be changed through the ordinary settings API.

### Clean Up and Continue Operations

Immediately after a confirmed success, disable the capability and remove the three bootstrap configuration values (including digest and expiry) from host-side configuration or secret storage. Retain the database and its completion marker. Sign in with the new administrator and use the normal authenticated administrator management surface for all later user and role changes.

For a future Docker deployment, use the released image together with host-side configuration and secrets, then follow the normal image pull/recreate workflow described above. This documentation does not instruct an image pull, publish, or deployment now.

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
    > [!IMPORTANT]
    > If you configured `INTERNAL_IP` to a specific IP (e.g., `192.168.x.x`), `curl localhost` will fail because the service is strictly bound to that IP. Use the configured IP instead: `curl -k http://192.168.x.x:8080/health`.
3.  **OpenID Discovery**:
    Navigate to `https://your-domain/.well-known/openid-configuration` and ensure it returns JSON data.

---

## Reverse Proxy & SignalR Support

If you are running HybridIdP behind an additional reverse proxy (e.g., BunkerWeb, Traefik, or another Nginx instance), ensure your proxy is configured to forward WebSocket upgrade headers correctly.

### WebSocket Configuration
The Nginx configuration included in this repository handles WebSocket upgrades via the `Connection` and `Upgrade` headers. Your external proxy must also forward these headers:

- `Upgrade`: `$http_upgrade`
- `Connection`: `Upgrade` (or derived from `$http_upgrade`)

### Sticky Sessions (Scaling)
SignalR requires sticky sessions (session affinity) when scaling the IdP service to multiple instances. Ensure your load balancer or reverse proxy routes requests from the same client to the same backend instance based on a cookie or IP hash.

> [!NOTE]
> Single-instance deployments (default) do not require sticky sessions.

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

### Alternative: Using Step-CA
If you prefer `step-ca`:

```bash
# Encryption Cert
step ca certificate "HybridIdP Encryption" encryption.crt encryption.key --kty RSA --size 4096
step certificate p12 encryption.pfx encryption.crt encryption.key --password=YOUR_PASSWORD

# Signing Cert
step ca certificate "HybridIdP Signing" signing.crt signing.key --kty RSA --size 4096
step certificate p12 signing.pfx signing.crt signing.key --password=YOUR_PASSWORD
```

### Alternative: Using OpenSSL
```bash
# Generate Encryption Cert
openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -nodes -keyout encryption.key -out encryption.crt -subj "/CN=HybridIdP Encryption"
openssl pkcs12 -export -out encryption.pfx -inkey encryption.key -in encryption.crt -password pass:YOUR_PASSWORD

# Generate Signing Cert
openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -nodes -keyout signing.key -out signing.crt -subj "/CN=HybridIdP Signing"
openssl pkcs12 -export -out signing.pfx -inkey signing.key -in signing.crt -password pass:YOUR_PASSWORD
```

> [!IMPORTANT]
> You must update `ENCRYPTION_CERT_PASSWORD` and `SIGNING_CERT_PASSWORD` in your `.env` file to match the passwords used above. **Always wrap the values in single quotes (e.g., `'password'`) to prevent issues with special characters like `#`.**

### Database & Redis Options
-   **Redis**: Set `Redis__Enabled=false` to use In-Memory caching (not recommended for multi-instance production).
-   **External DB**: Update `ConnectionStrings__...` with your real server details.

### Troubleshooting Manual Setup
**"Permission denied" when reading .env:**
If you see an error like `open .env: permission denied`, you may have created the file as `root` (e.g., via `sudo`) or set strict permissions (e.g., `chmod 600`) while the file is owned by another user.
Ensure your current user owns the file:
```bash
sudo chown $(whoami) deployment/.env
```
If your **Step-CA** is running on a different machine (e.g., **Host C** with IP `192.168.1.50`) and using an internal hostname (e.g., `ca.internal`), Docker containers might fail to resolve it.

**Option A: Manual Host Mapping (Recommended for Static IPs)**
Directly map the hostname to Host C's IP.
```yaml
services:
  idp-service:
    extra_hosts:
      - "ca.internal:192.168.1.50" # Map ca.internal to Host C IP
```

**Option B: Custom Internal DNS**
If you have an Internal DNS Server (e.g., AD DC) that knows where `ca.internal` is:
```yaml
services:
  idp-service:
    dns:
      - 192.168.1.1  # Your Internal DNS Server IP
      - 8.8.8.8      # Fallback

### Connecting to External Docker Networks (Legacy Services)
If your IdP needs to connect to another service running in a different Docker Compose project (e.g., a **Legacy Service**), you can join its network without modifying the main `docker-compose.yml`.

1.  **Identify the Network Name**: Run `docker network ls` to find the network name (e.g., `legacy_backend`).
2.  **Create `deployment/docker-compose.override.yml`**:
    ```yaml
    services:
      idp-service:
        networks:
          - legacy_network

    networks:
      legacy_network:
        external: true
        name: legacy_backend  # Actual name from 'docker network ls'
    ```
    > **Note**: `docker-compose.override.yml` is git-ignored, so your local setting won't be overwritten by updates.

3.  **Start with Override**:
    You must explicitly include both files when starting:
    ```bash
    docker compose -f docker-compose.splithost-nginx.yml -f docker-compose.override.yml --env-file .env up -d
    ```
```


