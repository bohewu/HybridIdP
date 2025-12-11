# 🗄️ Database Configuration Guide

> HybridIdP 支援雙資料庫：**Microsoft SQL Server** 與 **PostgreSQL**。本文件說明如何設定、切換、部署資料庫。

## 📋 目錄

- [快速開始](#快速開始)
- [資料庫架構](#資料庫架構)
- [本地開發環境](#本地開發環境)
- [Production 部署](#production-部署)
- [Migration 管理](#migration-管理)
- [測試資料設定](#測試資料設定)
- [故障排除](#故障排除)

> **⚠️ 重要提醒**: 
> 1. 專案有獨立的 migrations 專案（`Infrastructure.Migrations.SqlServer` 和 `Infrastructure.Migrations.Postgres`）
> 2. 兩個資料庫都使用相同的 `ApplicationDbContext`
> 3. 執行 EF Core 命令時**必須在正確的 migrations 專案目錄**並指定 `--context ApplicationDbContext` 參數

---

## 🚀 快速開始

### 使用 SQL Server (預設)

```powershell
# 1. 啟動 Docker 容器
docker-compose -f docker-compose.dev.yml up -d

# 2. 套用 Migrations（注意：是 Infrastructure.Migrations.SqlServer，不是 Infrastructure）
cd Infrastructure.Migrations.SqlServer
dotnet ef database update --startup-project ..\Web.IdP --context ApplicationDbContext

# 3. 註冊 TestClient (用於 E2E 測試)
Get-Content ..\create-testclient-mssql.sql | docker exec -i hybrididp-mssql-service-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P 'YourStrong!Passw0rd' -d hybridauth_idp -C

# 4. 啟動應用程式
cd ..\Web.IdP
dotnet run
```

### 使用 PostgreSQL

```powershell
# 1. 啟動 Docker 容器
docker-compose -f docker-compose.dev.yml up -d

# 2. 設定環境變數
$env:DATABASE_PROVIDER="PostgreSQL"

# 3. 套用 Migrations（注意：是 Infrastructure.Migrations.Postgres，不是 Infrastructure）
cd Infrastructure.Migrations.Postgres
dotnet ef database update --startup-project ..\Web.IdP --context ApplicationDbContext

# 4. 註冊 TestClient (用於 E2E 測試)
Get-Content ..\create-testclient.sql | docker exec -i hybrididp-postgres-service-1 psql -U user -d hybridauth_idp

# 5. 啟動應用程式
cd ..\Web.IdP
dotnet run
```

---

## 🏗️ 資料庫架構

### 為什麼支援兩種資料庫？

- **SQL Server**: 企業標準、Windows 生態系統整合、高可用性方案成熟
- **PostgreSQL**: 開源免費、跨平台、雲端友善、成本效益高

### 架構設計原則

1. **Migrations 分離**: 
   - `Infrastructure.Migrations.SqlServer/` - SQL Server 專用
   - `Infrastructure.Migrations.Postgres/` - PostgreSQL 專用
   - 避免 EF Core 偵測衝突

2. **Provider 選擇**: 
   - 環境變數 `DATABASE_PROVIDER` 優先
   - `appsettings.json` 中 `DatabaseProvider` 為後備
   - 預設: `SqlServer`

3. **Connection Strings**:
   - `SqlServerConnection` - SQL Server 連線字串
   - `PostgreSqlConnection` - PostgreSQL 連線字串

### 專案結構

```
HybridIdP/
├── Infrastructure/                    # 核心 DbContext
│   └── ApplicationDbContext.cs
├── Infrastructure.Migrations.SqlServer/  # SQL Server Migrations
│   ├── SqlServerDbContextFactory.cs
│   └── Migrations/
│       └── 20251124061302_InitialCreate.cs
├── Infrastructure.Migrations.Postgres/   # PostgreSQL Migrations
│   ├── PostgresDbContextFactory.cs
│   └── Migrations/
│       └── 20251124073027_InitialCreate.cs
├── Web.IdP/                          # Startup 專案
│   └── Program.cs                    # Provider 選擇邏輯
└── docker-compose.yml                # 本地開發資料庫
```

---

## 💻 本地開發環境

### Docker Compose 設定

```yaml
# docker-compose.yml
services:
  # SQL Server (Port 1433)
  mssql-service:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD=YourStrong!Passw0rd
    ports:
      - "1433:1433"
    volumes:
      - mssql-data:/var/opt/mssql

  # PostgreSQL (Port 5432)
  postgres-service:
    image: postgres:17
    environment:
      - POSTGRES_USER=user
      - POSTGRES_PASSWORD=password
      - POSTGRES_DB=hybridauth_idp
    ports:
      - "5432:5432"
    volumes:
      - postgres-data:/var/lib/postgresql/data

  # Redis (Port 6379)
  redis-service:
    image: redis:alpine
    ports:
      - "6379:6379"
```

### 環境變數設定

**方式 1: PowerShell Session**
```powershell
# 使用 SQL Server
$env:DATABASE_PROVIDER="SqlServer"

# 使用 PostgreSQL
$env:DATABASE_PROVIDER="PostgreSQL"
```

**方式 2: appsettings.Development.json**
```json
{
  "DatabaseProvider": "SqlServer",  // 或 "PostgreSQL"
  "ConnectionStrings": {
    "SqlServerConnection": "Server=localhost,1433;Database=hybridauth_idp;User Id=SA;Password=YourStrong!Passw0rd;Encrypt=False;TrustServerCertificate=True",
    "PostgreSqlConnection": "Host=localhost;Port=5432;Database=hybridauth_idp;Username=user;Password=password"
  }
}
```

**方式 3: User Secrets (推薦開發環境)**
```powershell
cd Web.IdP
dotnet user-secrets set "DatabaseProvider" "PostgreSQL"
dotnet user-secrets set "ConnectionStrings:PostgreSqlConnection" "Host=localhost;Port=5432;Database=hybridauth_idp;Username=user;Password=password"
```

---

## 🚢 Production 部署

### 環境變數設定 (推薦)

**原因:**
- ✅ 不會將敏感資料 commit 到 Git
- ✅ 符合 12-Factor App 原則
- ✅ 支援 Container Orchestration (Kubernetes, Docker Swarm)
- ✅ 易於在 CI/CD 中設定

#### Azure App Service

```bash
# Azure CLI
az webapp config appsettings set \
  --resource-group MyResourceGroup \
  --name MyAppName \
  --settings \
    DATABASE_PROVIDER=SqlServer \
    ConnectionStrings__SqlServerConnection="Server=myserver.database.windows.net;Database=hybridauth_idp;User Id=admin;Password=SecurePassword123!;Encrypt=True"
```

**Azure Portal:**
1. App Service > Configuration > Application Settings
2. 新增:
   - `DATABASE_PROVIDER` = `SqlServer`
   - `ConnectionStrings__SqlServerConnection` = `Server=...`
3. Save > Restart

#### Docker / Docker Compose

```yaml
# docker-compose.production.yml
services:
  web:
    image: hybrididp:latest
    environment:
      - DATABASE_PROVIDER=PostgreSQL
      - ConnectionStrings__PostgreSqlConnection=Host=prod-db;Database=hybridauth_idp;Username=idp_user;Password=${DB_PASSWORD}
    env_file:
      - .env.production  # 敏感資料存放處
```

**.env.production** (不要 commit!)
```bash
DB_PASSWORD=SuperSecureProductionPassword!
```

#### Kubernetes

```yaml
# deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: hybrididp
spec:
  template:
    spec:
      containers:
      - name: web
        image: hybrididp:latest
        env:
        - name: DATABASE_PROVIDER
          value: "SqlServer"
        - name: ConnectionStrings__SqlServerConnection
          valueFrom:
            secretKeyRef:
              name: db-secret
              key: connection-string
---
apiVersion: v1
kind: Secret
metadata:
  name: db-secret
type: Opaque
stringData:
  connection-string: "Server=prod-sql;Database=hybridauth_idp;User Id=sa;Password=ProductionPassword!"
```

**建立 Secret:**
```bash
kubectl create secret generic db-secret \
  --from-literal=connection-string="Server=prod-sql;Database=hybridauth_idp;User Id=sa;Password=ProductionPassword!"
```

#### Linux Systemd Service

```ini
# /etc/systemd/system/hybrididp.service
[Unit]
Description=HybridIdP Identity Provider
After=network.target

[Service]
Type=notify
WorkingDirectory=/opt/hybrididp
ExecStart=/usr/bin/dotnet Web.IdP.dll
Environment="ASPNETCORE_ENVIRONMENT=Production"
Environment="DATABASE_PROVIDER=PostgreSQL"
Environment="ConnectionStrings__PostgreSqlConnection=Host=localhost;Database=hybridauth_idp;Username=idp_user;Password=ProductionPassword"
User=hybrididp
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
```

### Azure Key Vault (企業推薦)

**優勢:**
- 🔐 中央化密鑰管理
- 📜 完整審計日誌
- 🔄 密鑰輪換支援
- 🛡️ Managed Identity 整合

**設定步驟:**

1. **安裝套件**
```powershell
cd Web.IdP
dotnet add package Azure.Identity
dotnet add package Azure.Extensions.AspNetCore.Configuration.Secrets
```

2. **修改 Program.cs**
```csharp
// Program.cs (在 var builder = WebApplication.CreateBuilder(args); 之後)
if (builder.Environment.IsProduction())
{
    var keyVaultName = builder.Configuration["KeyVault:Name"];
    var keyVaultUri = new Uri($"https://{keyVaultName}.vault.azure.net/");
    
    builder.Configuration.AddAzureKeyVault(
        keyVaultUri,
        new DefaultAzureCredential());
}
```

3. **Key Vault 設定**
```bash
# 建立 Key Vault
az keyvault create \
  --name MyHybridIdPVault \
  --resource-group MyResourceGroup \
  --location eastus

# 設定 Secrets
az keyvault secret set \
  --vault-name MyHybridIdPVault \
  --name "DATABASE-PROVIDER" \
  --value "SqlServer"

az keyvault secret set \
  --vault-name MyHybridIdPVault \
  --name "ConnectionStrings--SqlServerConnection" \
  --value "Server=myserver.database.windows.net;Database=hybridauth_idp;..."
```

4. **App Service Managed Identity**
```bash
# 啟用 Managed Identity
az webapp identity assign \
  --resource-group MyResourceGroup \
  --name MyAppName

# 授權存取 Key Vault
az keyvault set-policy \
  --name MyHybridIdPVault \
  --object-id <managed-identity-principal-id> \
  --secret-permissions get list
```

5. **appsettings.Production.json**
```json
{
  "KeyVault": {
    "Name": "MyHybridIdPVault"
  }
}
```

### AWS Secrets Manager

```csharp
// 安裝套件
// dotnet add package Amazon.Extensions.Configuration.SystemsManager

if (builder.Environment.IsProduction())
{
    builder.Configuration.AddSystemsManager($"/hybrididp/{builder.Environment.EnvironmentName}");
}
```

**設定 Secrets:**
```bash
aws secretsmanager create-secret \
  --name /hybrididp/Production/DATABASE_PROVIDER \
  --secret-string "SqlServer"

aws secretsmanager create-secret \
  --name /hybrididp/Production/ConnectionStrings__SqlServerConnection \
  --secret-string "Server=prod-rds.amazonaws.com;Database=hybridauth_idp;..."
```

---

## 🔄 Migration 管理

### 新增 Migration

**SQL Server:**
```powershell
cd Infrastructure.Migrations.SqlServer; dotnet ef migrations add YourMigrationName --startup-project ..\Web.IdP; cd ..
```

**PostgreSQL:**
```powershell
$env:DATABASE_PROVIDER="PostgreSQL"; cd Infrastructure.Migrations.Postgres; dotnet ef migrations add YourMigrationName --startup-project ..\Web.IdP; cd ..; $env:DATABASE_PROVIDER=$null
```

> **重要**: PostgreSQL migrations 需要設定 `DATABASE_PROVIDER` 環境變數，否則會使用預設的 SQL Server 設定。

### 檢查 Migration 狀態

**SQL Server:**
```powershell
cd Infrastructure.Migrations.SqlServer; dotnet ef migrations list --startup-project ..\Web.IdP; cd ..
```

**PostgreSQL:**
```powershell
$env:DATABASE_PROVIDER="PostgreSQL"; cd Infrastructure.Migrations.Postgres; dotnet ef migrations list --startup-project ..\Web.IdP; cd ..; $env:DATABASE_PROVIDER=$null
```

### 套用 Migration

**SQL Server:**
```powershell
cd Infrastructure.Migrations.SqlServer; dotnet ef database update --startup-project ..\Web.IdP --context ApplicationDbContext; cd ..
```

**PostgreSQL:**
```powershell
$env:DATABASE_PROVIDER="PostgreSQL"; cd Infrastructure.Migrations.Postgres; dotnet ef database update --startup-project ..\Web.IdP --context ApplicationDbContext; cd ..; $env:DATABASE_PROVIDER=$null
```

> **注意**: `--context` 參數必須指定，因為 Infrastructure 專案包含兩個 DbContext。

### 重新產生 Migration (清空資料庫)

**SQL Server:**
```powershell
# 1. 刪除舊 Migrations
cd Infrastructure.Migrations.SqlServer
Remove-Item -Recurse Migrations\

# 2. 刪除資料庫 (Docker)
docker exec hybrididp-mssql-service-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P 'YourStrong!Passw0rd' -C -Q "DROP DATABASE hybridauth_idp; CREATE DATABASE hybridauth_idp;"

# 3. 重新產生 Migration
dotnet ef migrations add InitialCreate --startup-project ..\Web.IdP

# 4. 套用 Migration
dotnet ef database update --startup-project ..\Web.IdP

# 5. 註冊 TestClient (E2E 測試用)
Get-Content ..\create-testclient-mssql.sql | docker exec -i hybrididp-mssql-service-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P 'YourStrong!Passw0rd' -d hybridauth_idp -C
```

**PostgreSQL:**
```powershell
# 1. 刪除舊 Migrations
cd Infrastructure.Migrations.Postgres
Remove-Item -Recurse Migrations\

# 2. 刪除資料庫 (Docker)
docker exec hybrididp-postgres-service-1 psql -U user -d postgres -c "DROP DATABASE hybridauth_idp;"
docker exec hybrididp-postgres-service-1 psql -U user -d postgres -c "CREATE DATABASE hybridauth_idp;"

# 3. 重新產生 Migration
dotnet ef migrations add InitialCreate --startup-project ..\Web.IdP --context ApplicationDbContext

# 4. 套用 Migration
dotnet ef database update --startup-project ..\Web.IdP --context ApplicationDbContext

# 5. 註冊 TestClient (E2E 測試用)
Get-Content ..\create-testclient.sql | docker exec -i hybrididp-postgres-service-1 psql -U user -d hybridauth_idp
```

---

## 🧪 測試資料設定

### E2E 測試所需資料

E2E 測試需要 TestClient OAuth 應用程式註冊。

**SQL Server:**
```powershell
Get-Content create-testclient-mssql.sql | docker exec -i hybrididp-mssql-service-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P 'YourStrong!Passw0rd' -d hybridauth_idp -C
```

**PostgreSQL:**
```powershell
Get-Content create-testclient.sql | docker exec -i hybrididp-postgres-service-1 psql -U user -d hybridauth_idp
```

**驗證:**
```powershell
# SQL Server
docker exec hybrididp-mssql-service-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P 'YourStrong!Passw0rd' -d hybridauth_idp -C -Q "SELECT ClientId, DisplayName FROM OpenIddictApplications WHERE ClientId = 'testclient-public'"

# PostgreSQL
docker exec hybrididp-postgres-service-1 psql -U user -d hybridauth_idp -c "SELECT \"ClientId\", \"DisplayName\" FROM \"OpenIddictApplications\" WHERE \"ClientId\" = 'testclient-public'"
```

### 預設測試使用者

由 `DataSeeder` 自動建立:
- **Email:** admin@hybridauth.local
- **Password:** Admin@123
- **Role:** Admin (所有權限)

### 執行 E2E 測試

```powershell
# Terminal 1: IdP
cd Web.IdP
dotnet run

# Terminal 2: TestClient
cd TestClient
dotnet run

# Terminal 3: E2E Tests
cd e2e
npm test
```

**測試結果應該:**
- 總測試: 78 個
- 通過: 68+ 個
- 失敗: < 10 個 (UI timing 問題)

---

## 🔧 故障排除

### 問題 1: EF Core 偵測到 Pending Model Changes

**症狀:**
```
The model was not created by the same version of EF Core as this tooling...
```

**原因:** 兩個 Migration 專案在同一目錄下，EF Core 掃描到多個 Migration 資料夾。

**解決方案:** ✅ **已實作** - 使用獨立 Migration 專案:
- `Infrastructure.Migrations.SqlServer/`
- `Infrastructure.Migrations.Postgres/`

### 問題 2: TestClient 認證失敗

**症狀:**
```
E2E tests timeout waiting for #Input_Login
OAuth flow fails with "invalid_client"
```

**原因:** `testclient-public` 未在資料庫中註冊。

**解決方案:**
```powershell
# SQL Server
Get-Content create-testclient-mssql.sql | docker exec -i hybrididp-mssql-service-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P 'YourStrong!Passw0rd' -d hybridauth_idp -C

# PostgreSQL
Get-Content create-testclient.sql | docker exec -i hybrididp-postgres-service-1 psql -U user -d hybridauth_idp
```

### 問題 3: Connection String 錯誤

**症狀:**
```
A network-related or instance-specific error occurred...
could not connect to server: Connection refused
```

**檢查清單:**
1. Docker 容器是否執行?
   ```powershell
   docker ps
  # 應該看到 hybrididp-mssql-service-1 或 hybrididp-postgres-service-1
   ```

2. Port 是否正確?
   - SQL Server: `1433`
   - PostgreSQL: `5432`

3. 連線字串格式正確?
   - SQL Server: `Server=localhost,1433;...`
   - PostgreSQL: `Host=localhost;Port=5432;...`

4. 環境變數是否設定?
   ```powershell
   $env:DATABASE_PROVIDER
   # 應該顯示 "SqlServer" 或 "PostgreSQL"
   ```

### 問題 4: "No migrations were found in assembly" 錯誤

**症狀:**
```
No migrations were found in assembly 'Infrastructure'. 
A migration needs to be added before the database can be updated.
```

**原因 1:** 在錯誤的目錄執行命令（migrations 在 `Infrastructure.Migrations.SqlServer` 或 `Infrastructure.Migrations.Postgres`，不是 `Infrastructure`）

**原因 2:** `IDesignTimeDbContextFactory` 沒有正確設定 `MigrationsAssembly`（已在 Infrastructure\DesignTime\ApplicationDbContextFactory.cs 中修正）

**解決方案:**
```powershell
# ❌ 錯誤 1 - 在 Infrastructure 目錄執行
cd Infrastructure
dotnet ef database update --startup-project ..\Web.IdP --context ApplicationDbContext

# ❌ 錯誤 2 - 缺少 --context 參數
dotnet ef database update --startup-project ..\Web.IdP

# ✅ 正確方式 1 - 切換到正確的 migrations 專案目錄
cd Infrastructure.Migrations.SqlServer
dotnet ef database update --startup-project ..\Web.IdP --context ApplicationDbContext

# ✅ 正確方式 2 - 使用 --project 參數指定 migrations 專案
cd C:\repos\HybridIdP
dotnet ef database update --project Infrastructure.Migrations.SqlServer --startup-project Web.IdP --context ApplicationDbContext

# PostgreSQL 同理
cd Infrastructure.Migrations.Postgres
dotnet ef database update --startup-project ..\Web.IdP --context ApplicationDbContext
```

**記憶要點:**
- 必須在 `Infrastructure.Migrations.SqlServer` 或 `Infrastructure.Migrations.Postgres` 目錄
- **不是** `Infrastructure` 目錄
- 或使用 `--project` 參數明確指定 migrations 專案

> **重要**: 所有 EF Core 命令都必須：
> 1. 在正確的 migrations 專案目錄執行（`Infrastructure.Migrations.SqlServer` 或 `Infrastructure.Migrations.Postgres`）
> 2. 加上 `--context ApplicationDbContext` 參數（兩個資料庫都用相同的 context 名稱）
> 
> **範例:**
> - `dotnet ef migrations add YourMigration --startup-project ..\Web.IdP --context ApplicationDbContext`
> - `dotnet ef database update --startup-project ..\Web.IdP --context ApplicationDbContext`
> - `dotnet ef migrations list --startup-project ..\Web.IdP --context ApplicationDbContext`

### 問題 5: Migration 套用失敗

**症狀:**
```
Build failed with 1 error(s).
Could not execute because the specified command or file was not found.
```

**解決方案:**
```powershell
# 確保在正確的目錄
cd Infrastructure.Migrations.SqlServer  # 或 Postgres

# 確保專案可建置
dotnet build

# 確保 EF Core CLI 已安裝
dotnet tool install --global dotnet-ef
dotnet tool update --global dotnet-ef

# 重試 Migration (記得加 --context)
dotnet ef database update --startup-project ..\Web.IdP --context ApplicationDbContext
```

### 問題 6: SQL Server QUOTED_IDENTIFIER 錯誤

**症狀:**
```
INSERT failed because the following SET options have incorrect settings: 'QUOTED_IDENTIFIER'
```

**解決方案:** ✅ **已實作** - `create-testclient-mssql.sql` 包含:
```sql
SET QUOTED_IDENTIFIER ON;
GO
```

### 問題 7: Git Clone 後舊資料庫與新 Migrations 不同步

**情境描述:**

當您重新 `git clone` 專案到新環境，但 Docker 容器中的舊資料庫仍然存在時，執行 `dotnet ef database update` 可能會遇到以下問題:

1. **Migration 歷史不一致**：舊資料庫的 `__EFMigrationsHistory` 表可能與新程式碼的 migrations 不一致
2. **Schema 不匹配**：資料庫結構可能與最新的程式碼不符
3. **測試資料過期**：舊的測試資料（如 TestClient）可能與新程式碼不相容

**症狀:**
```
The model for context 'ApplicationDbContext' has pending changes...
There is already an object named 'AspNetUsers' in the database
The database is already up to date
No migrations were applied. The database is already up to date.
```

**解決方案：選擇以下任一方法**

#### 方法 1: 完全重置資料庫 (推薦 - 最乾淨)

這會刪除所有舊資料，從頭開始建立資料庫。

**SQL Server:**
```powershell
# 1. 停止應用程式（如果正在執行）
# Ctrl+C 終止 dotnet run

# 2. 刪除並重建資料庫
docker exec hybrididp-mssql-service-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P 'YourStrong!Passw0rd' -C -Q "DROP DATABASE IF EXISTS hybridauth_idp; CREATE DATABASE hybridauth_idp;"

# 3. 切換到正確的 migrations 專案目錄
cd Infrastructure.Migrations.SqlServer

# 4. 重新套用所有 migrations
dotnet ef database update --startup-project ..\Web.IdP --context ApplicationDbContext

# 5. 註冊 TestClient（E2E 測試需要）
cd ..
Get-Content create-testclient-mssql.sql | docker exec -i hybrididp-mssql-service-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P 'YourStrong!Passw0rd' -d hybridauth_idp -C

# 6. 重新啟動應用程式
cd Web.IdP
dotnet run
```

**PostgreSQL:**
```powershell
# 1. 停止應用程式（如果正在執行）
# Ctrl+C 終止 dotnet run

# 2. 刪除並重建資料庫
docker exec hybrididp-postgres-service-1 psql -U user -d postgres -c "DROP DATABASE IF EXISTS hybridauth_idp;"
docker exec hybrididp-postgres-service-1 psql -U user -d postgres -c "CREATE DATABASE hybridauth_idp;"

# 3. 切換到正確的 migrations 專案目錄
cd Infrastructure.Migrations.Postgres

# 4. 重新套用所有 migrations
dotnet ef database update --startup-project ..\Web.IdP --context ApplicationDbContext

# 5. 註冊 TestClient（E2E 測試需要）
cd ..
Get-Content create-testclient.sql | docker exec -i hybrididp-postgres-service-1 psql -U user -d hybridauth_idp

# 6. 重新啟動應用程式
cd Web.IdP
dotnet run
```

#### 方法 2: 強制同步 Migration 歷史記錄

如果您想保留現有資料（例如測試用戶），可以強制將 migration 歷史標記為「已套用」，而不實際執行 SQL。

**警告:** 只有當您確定資料庫結構已經與最新程式碼一致時才使用此方法！

```powershell
# SQL Server
cd Infrastructure.Migrations.SqlServer

# 查看哪些 migrations 尚未套用
dotnet ef migrations list --startup-project ..\Web.IdP --context ApplicationDbContext

# 如果顯示「Pending」的 migration，但您確定資料庫已經是最新的
# 可以手動在資料庫中插入 migration 記錄（⚠️ 高風險操作）
docker exec hybrididp-mssql-service-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P 'YourStrong!Passw0rd' -d hybridauth_idp -C -Q "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20251124061302_InitialCreate', '9.0.0')"

# PostgreSQL 同理
docker exec hybrididp-postgres-service-1 psql -U user -d hybridauth_idp -c "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('20251124073027_InitialCreate', '9.0.0')"
```

#### 方法 3: 建立全新的資料庫（使用不同名稱）

如果您想保留舊資料庫作為參考，可以建立新的資料庫：

```powershell
# 修改 appsettings.Development.json 或設定環境變數
# SQL Server
$env:ConnectionStrings__SqlServerConnection = "Server=localhost,1433;Database=hybridauth_idp_new;User Id=SA;Password=YourStrong!Passw0rd;Encrypt=False;TrustServerCertificate=True"

# PostgreSQL
$env:ConnectionStrings__PostgreSqlConnection = "Host=localhost;Port=5432;Database=hybridauth_idp_new;Username=user;Password=password"

# 然後按照正常流程套用 migrations
cd Infrastructure.Migrations.SqlServer  # 或 Postgres
dotnet ef database update --startup-project ..\Web.IdP --context ApplicationDbContext
```

**最佳實務建議:**

1. **開發環境**：建議使用**方法 1 (完全重置)**，確保每次都有乾淨的環境
2. **保留測試資料**：如果需要保留特定測試資料，考慮將資料匯出成 SQL script，重置後再匯入
3. **Docker Volume 管理**：如果經常遇到此問題，可以在 `docker-compose down` 時加上 `-v` 參數刪除 volumes:
   ```powershell
   docker-compose down -v  # 刪除所有 volumes，包括資料庫資料
   docker-compose up -d    # 重新建立全新環境
   ```
4. **檢查 Migration 歷史**：每次 clone 後先執行 `dotnet ef migrations list` 確認狀態

**驗證資料庫已正確更新:**
```powershell
# SQL Server - 檢查 migrations 歷史
docker exec hybrididp-mssql-service-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P 'YourStrong!Passw0rd' -d hybridauth_idp -C -Q "SELECT * FROM __EFMigrationsHistory"

# PostgreSQL - 檢查 migrations 歷史
docker exec hybrididp-postgres-service-1 psql -U user -d hybridauth_idp -c "SELECT * FROM \"__EFMigrationsHistory\""

# 檢查測試用戶是否存在（應用程式啟動後自動建立）
# SQL Server
docker exec hybrididp-mssql-service-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P 'YourStrong!Passw0rd' -d hybridauth_idp -C -Q "SELECT Email FROM AspNetUsers WHERE Email = 'admin@hybridauth.local'"

# PostgreSQL
docker exec hybrididp-postgres-service-1 psql -U user -d hybridauth_idp -c "SELECT \"Email\" FROM \"AspNetUsers\" WHERE \"Email\" = 'admin@hybridauth.local'"
```

### 問題 8: PostgreSQL "operator does not exist: character varying = uuid"

**情境描述:**

當在 PostgreSQL 上執行 migrations 時，可能會遇到類型比較錯誤。

**症狀:**
```
42883: operator does not exist: character varying = uuid
POSITION: 118
WHERE "Name" IN ('openid', 'profile', 'email',
FROM "OpenIddictScopes"
```

**原因:**

PostgreSQL migrations 中存在類型不匹配：
- 某些表（如 `ScopeExtensions.ScopeId`, `ScopeClaim.ScopeId`）使用 `character varying`（字串）
- `OpenIddictScopes.Id` 使用 `uuid` 類型
- PostgreSQL 無法直接比較這兩種類型

**解決方案:**

此問題已在 migration 檔案中修復。如果您遇到此問題，請確保：

1. **確認 migration 檔案已包含類型轉換**：
   ```powershell
   # 檢查 20251205140958_AddIsPublicToScopeExtension.cs
   code Infrastructure.Migrations.Postgres\Migrations\20251205140958_AddIsPublicToScopeExtension.cs
   ```

2. **SQL 查詢應包含 CAST**：
   ```sql
   WHERE "ScopeId" IN (
       SELECT CAST("Id" AS TEXT)  -- 必須有這個 CAST
       FROM "OpenIddictScopes" 
       WHERE "Name" IN ('openid', 'profile', 'email', 'roles')
   );
   ```

3. **如果問題仍然存在，手動修復**：
   - 編輯 migration 檔案
   - 在所有將 UUID 與 VARCHAR 比較的地方添加 `CAST("Id" AS TEXT)`
   - 重新套用 migrations

4. **完全重置並重新套用**：
   ```powershell
   # 刪除並重建資料庫
   docker exec hybrididp-postgres-service-1 psql -U user -d postgres -c "DROP DATABASE IF EXISTS hybridauth_idp;"
   docker exec hybrididp-postgres-service-1 psql -U user -d postgres -c "CREATE DATABASE hybridauth_idp;"
   
   # 設定環境變數並套用所有 migrations
   cd Infrastructure.Migrations.Postgres
   $env:DATABASE_PROVIDER="PostgreSQL"
   dotnet ef database update --startup-project ..\Web.IdP --context ApplicationDbContext
   ```

**預防措施:**

未來在建立新的 migrations 時，如果需要比較 `OpenIddictScopes.Id` 與字串欄位：
- 始終使用 `CAST("Id" AS TEXT)` 進行類型轉換
- 或考慮將相關外鍵欄位改為 `uuid` 類型（需要重新設計 schema）

---

## 📊 性能考量

### 索引策略

兩種資料庫都使用相同的 EF Core 配置，OpenIddict 會自動建立所需索引:
- `OpenIddictApplications.ClientId` (唯一索引)
- `OpenIddictAuthorizations.Subject + ClientId`
- `AspNetUsers.Email` (唯一索引)
- `AspNetUsers.NormalizedEmail`

### Connection Pooling

**SQL Server:**
```json
"SqlServerConnection": "Server=...;Max Pool Size=100;Min Pool Size=5;..."
```

**PostgreSQL:**
```json
"PostgreSqlConnection": "Host=...;Maximum Pool Size=100;Minimum Pool Size=5;..."
```

### Production 建議

1. **使用 Connection Pooling** (預設已啟用)
2. **設定適當的 Timeout**:
   - Command Timeout: 30 秒
   - Connection Timeout: 15 秒
3. **監控 Connection Pool**:
   - Azure: Application Insights
   - AWS: CloudWatch
   - Self-hosted: Prometheus + Grafana

---

## 📝 快速參考命令

### SQL Server 常用命令

```powershell
# 檢查當前目錄
pwd

# ⚠️ 重要：必須切換到 Infrastructure.Migrations.SqlServer 目錄（不是 Infrastructure）
cd C:\repos\HybridIdP\Infrastructure.Migrations.SqlServer

# 列出 migrations（驗證設定正確）
dotnet ef migrations list --startup-project ..\Web.IdP --context ApplicationDbContext

# 套用 migrations
dotnet ef database update --startup-project ..\Web.IdP --context ApplicationDbContext

# 或者從專案根目錄執行（使用 --project 參數）
# cd C:\repos\HybridIdP
# dotnet ef database update --project Infrastructure.Migrations.SqlServer --startup-project Web.IdP --context ApplicationDbContext

# 建立新 migration
dotnet ef migrations add MigrationName --startup-project ..\Web.IdP --context ApplicationDbContext

# 列出所有 migrations
dotnet ef migrations list --startup-project ..\Web.IdP --context ApplicationDbContext

# 移除最後一個 migration
dotnet ef migrations remove --startup-project ..\Web.IdP --context ApplicationDbContext

# 回滾到指定 migration
dotnet ef database update PreviousMigrationName --startup-project ..\Web.IdP --context ApplicationDbContext

# 重置資料庫（移除所有 migrations）
dotnet ef database update 0 --startup-project ..\Web.IdP --context ApplicationDbContext
```

### PostgreSQL 常用命令

```powershell
# 檢查當前目錄
pwd

# ⚠️ 重要：必須切換到 Infrastructure.Migrations.Postgres 目錄（不是 Infrastructure）
cd C:\repos\HybridIdP\Infrastructure.Migrations.Postgres

# 列出 migrations（驗證設定正確）
dotnet ef migrations list --startup-project ..\Web.IdP --context ApplicationDbContext

# 套用 migrations（注意：PostgreSQL 也是用 ApplicationDbContext，不是 ApplicationDbContext）
dotnet ef database update --startup-project ..\Web.IdP --context ApplicationDbContext

# 或者從專案根目錄執行（使用 --project 參數）
# cd C:\repos\HybridIdP
# dotnet ef database update --project Infrastructure.Migrations.Postgres --startup-project Web.IdP --context ApplicationDbContext

# 建立新 migration
dotnet ef migrations add MigrationName --startup-project ..\Web.IdP --context ApplicationDbContext

# 列出所有 migrations
dotnet ef migrations list --startup-project ..\Web.IdP --context ApplicationDbContext

# 移除最後一個 migration
dotnet ef migrations remove --startup-project ..\Web.IdP --context ApplicationDbContext

# 回滾到指定 migration
dotnet ef database update PreviousMigrationName --startup-project ..\Web.IdP --context ApplicationDbContext

# 重置資料庫（移除所有 migrations）
dotnet ef database update 0 --startup-project ..\Web.IdP --context ApplicationDbContext
```

### 記憶口訣

**所有 EF Core 命令都要加 `--context`！**

- SQL Server → `--context ApplicationDbContext`
- PostgreSQL → `--context ApplicationDbContext`

---

## 🔗 相關文件

- [DEVELOPMENT_GUIDE.md](./DEVELOPMENT_GUIDE.md) - 開發工作流程
- [PROJECT_STATUS.md](./archive/historical/PROJECT_STATUS.md) - 專案進度 (archived)
- [ARCHITECTURE.md](./ARCHITECTURE.md) - 架構說明

---

**建立時間:** 2025-11-24  
**最後更新:** 2025-12-11  
**維護者:** HybridIdP Team  
**版本:** 1.3
