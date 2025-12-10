 # HybridIdP Deployment Guide

This guide covers the deployment of HybridIdP using Docker Compose, supporting both **Internal** (behind Load Balancer) and **Nginx Reverse Proxy** modes.

## Table of Contents
1. [Prerequisites](#prerequisites)
2. [Certificate Generation](#certificate-generation)
3. [Configuration](#configuration)
4. [Deployment Modes](#deployment-modes)
   - [Mode A: Nginx Reverse Proxy (Recommended)](#mode-a-nginx-reverse-proxy-recommended)
   - [Mode B: Internal / Load Balancer](#mode-b-internal--load-balancer)
5. [Database & Redis](#database--redis)

---

## Prerequisites
- Docker & Docker Compose
- OpenSSL (for generating certificates)

## Certificate Generation
HybridIdP uses **OpenIddict**, which requires valid X.509 certificates to sign and encrypt tokens. 

1. Create a `certs` directory in `deployment/`:
   ```bash
   mkdir -p deployment/certs
   cd deployment/certs
   ```

2. Generate Self-Signed Certificates (for Testing/Staging):
   > **Note**: For production, valid certificates from a CA (like Let's Encrypt) are recommended for your domain.

   **Generate Encryption Certificate:**
   ```bash
   openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 \
     -nodes -keyout encryption.key -out encryption.crt \
     -subj "/CN=HybridIdP Encryption"
   
   openssl pkcs12 -export -out encryption.pfx -inkey encryption.key -in encryption.crt \
     -password pass:changeit
   ```

   **Generate Signing Certificate:**
   ```bash
   openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 \
     -nodes -keyout signing.key -out signing.crt \
     -subj "/CN=HybridIdP Signing"
   
   openssl pkcs12 -export -out signing.pfx -inkey signing.key -in signing.crt \
     -password pass:changeit
   ```

   **Generate SSL Certificate (for Nginx/Localhost):**
   ```bash
   openssl req -x509 -newkey rsa:4096 -sha256 -days 365 -nodes \
     -keyout localhost.key -out localhost.crt \
     -subj "/CN=localhost" \
     -addext "subjectAltName=DNS:localhost,IP:127.0.0.1"
   ```

---

## Configuration

1. Copy the example environment file:
   ```bash
   cd deployment
   cp .env.example .env
   ```

2. Edit `.env` and configure:
   - `ASPNETCORE_ENVIRONMENT`: Set to `Production`.
   - `DATABASE_PROVIDER`: `SqlServer` or `PostgreSQL`.
   - `Redis__Enabled`: `true` or `false`.
   - `Proxy__Enabled`: `true` if running behind a proxy.
   - `Proxy__KnownProxies`: IPs of your Load Balancer/Nginx.
   - `ENCRYPTION_CERT_PASSWORD` & `SIGNING_CERT_PASSWORD`: Passwords used when creating PFX files.

### Configuration via Environment Variables

The application uses `appsettings.json` for structure, but values should be overridden using Environment Variables in production. ASP.NET Core uses result to double underscore `__` to override nested keys.

**Example: Configuring Turnstile**

To override the `Turnstile` settings in `appsettings.json`:

```json
"Turnstile": {
  "SiteKey": "default-site-key",
  "SecretKey": "default-secret-key"
}
```

Set the following environment variables in your `.env` file:

```bash
Turnstile__SiteKey=0x4AAAAAAABBBBBBBB
Turnstile__SecretKey=0x4AAAAAAABBBBBBBB
```

> [!NOTE]
> The double underscore `__` is critical. It separates the parent section (`Turnstile`) from the child key (`SiteKey`).

---

## Deployment Modes

### Mode A: Nginx Reverse Proxy (Recommended)
Uses Nginx to handle SSL termination and forward traffic to HybridIdP.

**Files**: `docker-compose.nginx.yml`, `nginx/nginx.conf`

**Run:**
```bash
docker compose -f docker-compose.nginx.yml --env-file .env up -d
```

### Mode B: Internal / Load Balancer
Runs HybridIdP and Databases only. Assumes you have an external Load Balancer (AWS ALB, Azure App Gateway, etc.) handling SSL.

**Files**: `docker-compose.internal.yml`

**Run:**
```bash
docker compose -f docker-compose.internal.yml --env-file .env up -d
```

### Mode C: External Database
For connecting to an existing external database (e.g., Azure SQL, Amazon RDS, or a dedicated VM) instead of containerized instances.

1.  **Configure `.env`**:
    *   Set `ConnectionStrings__SqlServerConnection` or `ConnectionStrings__PostgreSqlConnection` to point to your external host.
    *   Example: `Server=my-azure-sql.database.windows.net;Database=HybridAuthIdP;...`
2.  **Modify Docker Compose**:
    *   You can comment out the `depends_on` sections for `mssql-service` or `postgres-service` in your chosen YAML file.
    *   Optionally, remove the `mssql-service` and `postgres-service` blocks entirely if you don't need them.

---

## Database & Redis

### Switching Databases
To switch between SQL Server and PostgreSQL:
1. Set `DATABASE_PROVIDER` in `.env`.
2. Ensure the correct connection string variable is set.
3. The Docker Compose files include both database services, but the application will only connect to the one configured.

### Redis Caching
- **Enable**: Set `Redis__Enabled=true` in `.env`.
- **Disable**: Set `Redis__Enabled=false`. The application will fall back to In-Memory caching.
