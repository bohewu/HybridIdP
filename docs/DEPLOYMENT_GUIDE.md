 # HybridIdP Deployment Guide

This guide covers the deployment of HybridIdP using Docker Compose, supporting multiple deployment modes.

## Table of Contents
1. [Prerequisites](#prerequisites)
2. [Certificate Generation](#certificate-generation)
3. [Configuration](#configuration)
4. [Deployment Modes](#deployment-modes)
   - [Mode A: Nginx Reverse Proxy (Recommended)](#mode-a-nginx-reverse-proxy-recommended)
   - [Mode B: Internal / Load Balancer](#mode-b-internal--load-balancer)
   - [Mode C: Split-Host + Internal DB](#mode-c-split-host--internal-db)
   - [Mode D: Split-Host + External DB](#mode-d-split-host--external-db)
5. [SSH Tunnel Testing](#ssh-tunnel-testing)
6. [Database & Redis](#database--redis)

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

### Quick Setup (Recommended)

Use the interactive setup script to generate a `.env` file with secure passwords:

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

The script will:
- Prompt for deployment mode:
  1. **Nginx Reverse Proxy** - Recommended, includes SSL termination
  2. **Internal/Load Balancer** - No SSL container, external LB handles SSL
  3. **Split-Host + Internal DB** - Docker DB included, Nginx gateway
  4. **Split-Host + External DB** - External DB server, minimal containers
- Prompt for database provider (SqlServer/PostgreSQL)
- For external DB mode: prompt for DB host, name, user, password
- Generate secure random passwords
- Optionally generate OpenIddict certificates
- Create a complete `.env` file
- Output the correct `docker compose` command to run

### Manual Configuration

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

### Mode C: Split-Host + Internal DB

For scenarios where a reverse proxy runs on a separate host from HybridIdP, with databases running in Docker.

**Files**: `docker-compose.splithost-nginx.yml`

```
┌─────────────────────┐          ┌─────────────────────────────────┐
│  Host A (Proxy)     │   HTTP   │  Host B (Docker)                │
│  Nginx/BunkerWeb    │ ───────► │  nginx-gateway:8080             │
│  192.168.1.10       │  :8080   │    └─► idp-service              │
│  (SSL Termination)  │          │    └─► mssql/postgres + redis   │
└─────────────────────┘          └─────────────────────────────────┘
```

**Run:**
```bash
docker compose -f docker-compose.splithost-nginx.yml --env-file .env up -d
```

### Mode D: Split-Host + External DB

Most secure production configuration: external database, minimal Docker containers.

**Files**: `docker-compose.splithost-nginx-nodb.yml`

```
┌─────────────────────┐          ┌─────────────────────────────────┐
│  Host A (Proxy)     │   HTTP   │  Host B (Docker)                │
│  Nginx/BunkerWeb    │ ───────► │  nginx-gateway:8080             │
│  192.168.1.10       │  :8080   │    └─► idp-service              │
│  (SSL Termination)  │          │    └─► redis (session cache)    │
└─────────────────────┘          └──────────┬──────────────────────┘
                                             │ TCP:1433
                                             ▼
                                    External SQL Server
```

**setup-env 會詢問外部 DB 連線資訊：**
```
SQL Server Host (e.g., db.example.com,1433): your-db-host,1433
Database Name [hybridauth_idp]: hybridauth_idp
Database User [idp_app]: idp_app
Database Password: ********
```

**生成的連線字串格式：**
```bash
ConnectionStrings__SqlServerConnection=Server=your-db-host,1433;Database=hybridauth_idp;User Id=idp_app;Password=xxx;Encrypt=True;TrustServerCertificate=True
```

**Run:**
```bash
docker compose -f docker-compose.splithost-nginx-nodb.yml --env-file .env up -d
```

> [!TIP]
> For SQL Server Contained Database mode, create a contained user in your DB:
> ```sql
> ALTER DATABASE hybridauth_idp SET CONTAINMENT = PARTIAL;
> USE hybridauth_idp;
> CREATE USER idp_app WITH PASSWORD = 'YourSecurePassword!';
> ALTER ROLE db_owner ADD MEMBER idp_app;
> ```

### Mode D: Split-Host Deployment (Reverse Proxy on Host A, App on Host B)

For scenarios where a reverse proxy (Nginx, BunkerWeb, Traefik, etc.) runs on a separate host from HybridIdP.

**Files**: `docker-compose.splithost.yml`

> [!TIP]
> For detailed step-by-step instructions, see [SPLIT_HOST_SECURITY.md](../deployment/SPLIT_HOST_SECURITY.md)

```
┌─────────────────────┐          ┌─────────────────────┐
│  Host A (Proxy)     │   HTTP   │  Host B (App)       │
│  Nginx/BunkerWeb    │ ───────► │  HybridIdP          │
│  192.168.1.10       │  :8080   │  192.168.1.20       │
│  (SSL Termination)  │          │  (Internal Mode)    │
└─────────────────────┘          └─────────────────────┘
```

#### Security Considerations

> [!CAUTION]
> Docker bypasses UFW/firewalld by default! Use iptables + IP binding for proper security.

##### Recommended: iptables + Interface Binding

1. **Bind to internal IP** in `.env`:
   ```bash
   INTERNAL_IP=192.168.1.20
   ```

2. **Add iptables rule** to only allow Host A:
   ```bash
   # Only allow reverse proxy (192.168.1.10) to connect to port 8080
   iptables -I DOCKER-USER -i eth0 -p tcp --dport 8080 ! -s 192.168.1.10 -j DROP
   
   # Persist the rule (Ubuntu/Debian)
   apt install iptables-persistent
   netfilter-persistent save
   ```

##### How Security Layers Work Together

| Layer | Component | Purpose |
|-------|-----------|---------|
| Network | iptables `DOCKER-USER` | Only allow specific source IPs |
| Network | `INTERNAL_IP` binding | Only listen on internal interface |
| Application | `Proxy__KnownProxies` | Trust forwarded headers from specific IPs only |
| Application | `ForwardedHeadersMiddleware` | Process `X-Forwarded-For`, `X-Forwarded-Proto` |

#### Host B (HybridIdP) Configuration

1. Configure `.env`:
   ```bash
   # Bind to internal interface only
   INTERNAL_IP=192.168.1.20
   
   # Trust reverse proxy for forwarded headers
   PROXY_HOST_IP=192.168.1.10
   ```

2. Run:
   ```bash
   docker compose -f docker-compose.splithost.yml --env-file .env up -d
   ```

#### Host A (Reverse Proxy) Configuration

Configure your reverse proxy to forward to Host B. **No plugins required** - standard nginx includes `ngx_http_realip_module`:

```nginx
# Nginx example
upstream hybrididp {
    server 192.168.1.20:8080;
}

server {
    listen 443 ssl;
    server_name idp.example.com;

    # SSL configuration...

    location / {
        proxy_pass http://hybrididp;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

> [!IMPORTANT]
> The `X-Forwarded-Proto` header is critical. If not set correctly, HybridIdP will generate `http://` URLs instead of `https://`, breaking OAuth flows.

---

## SSH Tunnel Testing

在設定 DNS 之前，可以使用 SSH Tunnel 直接測試 Docker 容器是否正常運作。

### 步驟

1. **建立 SSH Tunnel** (在你的電腦上執行):
   ```bash
   # 連接到 Host 的 nginx-gateway (port 8080)
   ssh -L 8080:localhost:8080 user@your-host-ip
   ```

2. **瀏覽器測試**:
   ```
   http://localhost:8080         # 首頁
   http://localhost:8080/Admin   # Admin UI
   ```

3. **確認沒問題後，設定 DNS**:
   ```
   id.yourcompany.com → A記錄 → Host IP
   ```

### 驗證清單

| 檢查項目 | 預期結果 |
|----------|----------|
| 首頁載入 | 看到登入頁面 |
| Admin UI | 能夠登入 Admin Dashboard |
| OIDC 設定 | `/.well-known/openid-configuration` 回傳 JSON |
| 健康檢查 | `/health` 回傳正常 |

---

## 驗證部署

### 快速驗證 (在 Host 上)

```bash
# 檢查容器狀態
docker compose ps

# 檢查健康狀態
curl -k http://localhost:8080/health

# 查看 logs（如果有問題）
docker compose logs -f idp-service
```

### 完整驗證

| 檢查項目 | 命令 / 方法 |
|----------|-------------|
| 容器運行 | `docker compose ps` |
| 健康狀態 | `curl http://localhost:8080/health` |
| OIDC 設定 | `curl http://localhost:8080/.well-known/openid-configuration` |
| 登入頁面 | 瀏覽器開啟 `http://localhost:8080` |
| DB 連線 | 查看 logs 無連線錯誤 |

### 常見問題

| 問題 | 解決方案 |
|------|----------|
| 容器不斷重啟 | `docker compose logs idp-service` 查看錯誤 |
| 無法連接 DB | 檢查 `.env` 的連線字串和網路 |
| 502 Bad Gateway | idp-service 未啟動，檢查 logs |
| HTTP 而非 HTTPS | 確認 `X-Forwarded-Proto` header 設定 |

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

