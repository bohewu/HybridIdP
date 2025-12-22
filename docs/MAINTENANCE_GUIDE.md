# HybridIdP Maintenance Guide

運維指南：備份、Log 管理、監控設定。

---

## 目錄

- [備份策略](#備份策略)
- [Log 管理](#log-管理)
- [Loki + Grafana 設定](#loki--grafana-設定)
- [VictoriaLogs 設定 (輕量替代方案)](#victorialogs-設定-輕量替代方案)
- [健康檢查](#健康檢查)
- [常見維運任務](#常見維運任務)

---

## 備份策略

### 需要備份的項目

| 項目 | 位置 | 重要性 | 備份頻率 |
|------|------|--------|----------|
| **資料庫** | SQL Server/PostgreSQL | 🔴 關鍵 | 每日 |
| **憑證** | `deployment/certs/` | 🔴 關鍵 | 變更時 |
| **.env** | `deployment/.env` | 🔴 關鍵 | 變更時 |
| **Nginx 設定** | `deployment/nginx/` | 🟡 重要 | 變更時 |
| **Docker Logs** | Container logs | 🟢 可重建 | 每日 |

### 使用 backup.sh

```bash
cd deployment
chmod +x backup.sh

# 執行備份
./backup.sh /backups/hybrididp

# 設定每日自動備份 (crontab)
crontab -e
# 加入：
0 2 * * * /path/to/deployment/backup.sh /backups/hybrididp
```

備份內容：
- `certs/` - OpenIddict 憑證
- `.env` - 環境設定（含密碼）
- `nginx/` - Nginx 設定
- Docker container logs

### 還原步驟

```bash
# 解壓備份
tar -xzf hybrididp_backup_20250101_020000.tar.gz

# 還原憑證和設定
cp -r 20250101_020000/certs deployment/
cp 20250101_020000/.env deployment/
cp -r 20250101_020000/nginx deployment/

# 重啟服務
docker compose -f docker-compose.xxx.yml down
docker compose -f docker-compose.xxx.yml up -d
```

---

## Log 管理

### Log Rotation 設定

所有 Docker Compose 檔案已設定自動 log rotation：

| 服務 | 單檔大小 | 保留數量 | 總容量 |
|------|----------|----------|--------|
| idp-service | 100MB | 30 | ~3GB |
| nginx-gateway | 50MB | 10 | ~500MB |
| mssql-service | 50MB | 10 | ~500MB |
| postgres-service | 50MB | 10 | ~500MB |
| redis-service | 20MB | 5 | ~100MB |

### 手動查看 Logs

```bash
# 查看即時 log
docker compose logs -f idp-service

# 查看最後 100 行
docker compose logs --tail 100 idp-service

# 匯出 log 到檔案
docker logs idp-service > idp-service.log 2>&1
```

### Log 檔案位置

```bash
# Docker log 位置 (Linux)
/var/lib/docker/containers/<container-id>/<container-id>-json.log
```

---

## Loki + Grafana 設定

### 架構

```
┌────────────┐    ┌────────────┐    ┌────────────┐
│ idp-service│───►│   Loki     │───►│  Grafana   │
│ nginx      │    │ (Log Store)│    │ (UI)       │
│ redis      │    └────────────┘    └────────────┘
└────────────┘
```

### 快速部署

創建 `docker-compose.logging.yml`：

```yaml
services:
  loki:
    image: grafana/loki:2.9.0
    ports:
      - "3100:3100"
    command: -config.file=/etc/loki/local-config.yaml
    volumes:
      - loki-data:/loki

  grafana:
    image: grafana/grafana:latest
    ports:
      - "3000:3000"
    environment:
      - GF_SECURITY_ADMIN_USER=admin
      - GF_SECURITY_ADMIN_PASSWORD=${GRAFANA_PASSWORD:-admin}
      - GF_USERS_ALLOW_SIGN_UP=false
    volumes:
      - grafana-data:/var/lib/grafana
    depends_on:
      - loki

  promtail:
    image: grafana/promtail:2.9.0
    volumes:
      - /var/lib/docker/containers:/var/lib/docker/containers:ro
      - /var/run/docker.sock:/var/run/docker.sock
      - ./promtail-config.yml:/etc/promtail/config.yml
    command: -config.file=/etc/promtail/config.yml
    depends_on:
      - loki

volumes:
  loki-data:
  grafana-data:
```

創建 `promtail-config.yml`：

```yaml
server:
  http_listen_port: 9080
  grpc_listen_port: 0

positions:
  filename: /tmp/positions.yaml

clients:
  - url: http://loki:3100/loki/api/v1/push

scrape_configs:
  - job_name: containers
    static_configs:
      - targets:
          - localhost
        labels:
          job: containerlogs
          __path__: /var/lib/docker/containers/*/*log

    pipeline_stages:
      - json:
          expressions:
            output: log
            stream: stream
            attrs:
      - json:
          expressions:
            tag:
          source: attrs
      - regex:
          expression: (?P<container_name>(?:[a-zA-Z0-9][a-zA-Z0-9_.-]+))
          source: tag
      - labels:
          container_name:
      - output:
          source: output
```

### 啟動

```bash
docker compose -f docker-compose.logging.yml up -d
```

### 設定 Grafana

1. 開啟 `http://your-host:3000`
2. 登入 (admin / 你設定的密碼)
3. **Connections** → **Data Sources** → **Add data source**
4. 選擇 **Loki**
5. URL: `http://loki:3100`
6. **Save & Test**

### 常用查詢

```logql
# 查看 idp-service logs
{container_name=~".*idp.*"}

# 篩選錯誤
{container_name=~".*idp.*"} |= "error"

# 查看登入事件
{container_name=~".*idp.*"} |= "Login"

# 最近 1 小時的 500 錯誤
{container_name=~".*idp.*"} |= "500" | json
```

---

## VictoriaLogs 設定 (輕量替代方案)

VictoriaLogs 來自 VictoriaMetrics 團隊，資源消耗極低，適合資源有限的環境。

### Loki vs VictoriaLogs

| 考量 | Loki | VictoriaLogs |
|------|------|--------------|
| **RAM 消耗** | 中等 | ✅ 極低 (5-10x 更少) |
| **查詢速度** | 快 | ✅ 更快 |
| **壓縮效率** | 好 | ✅ 更好 (10-30x) |
| **成熟度** | ✅ 成熟 | 較新 (2023) |
| **Grafana 整合** | ✅ 原生 | ✅ 支援 |

### 快速部署

創建 `docker-compose.logging-victorialogs.yml`：

```yaml
services:
  victorialogs:
    image: victoriametrics/victoria-logs:latest
    ports:
      - "9428:9428"
    volumes:
      - vlogs-data:/vlogs
    command:
      - -storageDataPath=/vlogs
      - -retentionPeriod=90d
      - -syslog.listenAddr.tcp=:514

  vector:
    image: timberio/vector:latest-alpine
    volumes:
      - /var/lib/docker/containers:/var/lib/docker/containers:ro
      - ./vector.toml:/etc/vector/vector.toml:ro
    depends_on:
      - victorialogs

  grafana:
    image: grafana/grafana:latest
    ports:
      - "3000:3000"
    environment:
      - GF_SECURITY_ADMIN_USER=admin
      - GF_SECURITY_ADMIN_PASSWORD=${GRAFANA_PASSWORD:-admin}
      - GF_USERS_ALLOW_SIGN_UP=false
    volumes:
      - grafana-data:/var/lib/grafana
    depends_on:
      - victorialogs

volumes:
  vlogs-data:
  grafana-data:
```

創建 `vector.toml`：

```toml
[sources.docker_logs]
type = "docker_logs"

[transforms.parse]
type = "remap"
inputs = ["docker_logs"]
source = '''
.timestamp = now()
.container = .container_name
'''

[sinks.victorialogs]
type = "http"
inputs = ["parse"]
uri = "http://victorialogs:9428/insert/jsonline?_stream_fields=container"
encoding.codec = "json"
framing.method = "newline_delimited"
```

### 啟動

```bash
docker compose -f docker-compose.logging-victorialogs.yml up -d
```

### 設定 Grafana

1. 開啟 `http://your-host:3000`
2. 安裝 VictoriaLogs 插件：
   ```bash
   docker exec -it grafana grafana-cli plugins install victoriametrics-logs-datasource
   docker compose restart grafana
   ```
3. **Connections** → **Data Sources** → **Add data source**
4. 選擇 **VictoriaLogs**
5. URL: `http://victorialogs:9428`
6. **Save & Test**

### 常用查詢

```
# 查看所有 logs
*

# 篩選容器
container:idp-service

# 關鍵字搜尋
"error" OR "exception"

# 組合查詢
container:idp-service AND "Login"
```

---

## 健康檢查

### Endpoints

| Endpoint | 用途 |
|----------|------|
| `/health` | 整體健康狀態 |
| `/metrics` | Prometheus metrics (需授權) |

### 監控腳本

```bash
#!/bin/bash
# health-check.sh

URL="https://idp.example.com/health"

response=$(curl -s -o /dev/null -w "%{http_code}" "$URL")

if [ "$response" != "200" ]; then
    echo "ALERT: HybridIdP health check failed (HTTP $response)"
    # 發送通知...
fi
```

---

## 常見維運任務

### 更新應用程式

```bash
cd deployment

# 拉取最新程式碼
git pull

# 重建並重啟
docker compose -f docker-compose.xxx.yml build
docker compose -f docker-compose.xxx.yml up -d
```

### 更新憑證

```bash
# 1. 生成新憑證 (參考 DEPLOYMENT_GUIDE.md)
# 2. 放入 deployment/certs/
# 3. 重啟服務
docker compose -f docker-compose.xxx.yml restart idp-service
```

### 清理 Docker

```bash
# 清理未使用的 images
docker image prune -a

# 清理未使用的 volumes (小心！)
docker volume prune

# 查看磁碟使用
docker system df
```

### 檢視資源使用

```bash
# 容器資源使用
docker stats

# 系統資源
htop
df -h
```
