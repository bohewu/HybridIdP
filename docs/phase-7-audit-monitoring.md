---
title: "Phase 7: Audit & Monitoring System"
owner: HybridIdP Team
last-updated: 2025-11-19
percent-complete: 100
---

# Phase 7: Audit & Monitoring System

Phase 7 將實作完整的稽核與監控系統，分為多個子階段以控制開發複雜度與 token 消耗

## ✅ Phase 7.5: OpenTelemetry Observability (2025-11-19)

**目標：** 實作 OpenTelemetry 分散式追蹤與 Prometheus metrics 監控
**完成時間：** 2025-11-19

**Commits:**

- `8d40875` - Add OpenTelemetry observability with Prometheus metrics endpoint
- `7ba2ffe` - Add IP whitelist protection for Prometheus /metrics endpoint

**功能範圍：**

✅ **OpenTelemetry 配置**

- 安裝 8 個 OpenTelemetry 套件 (Runtime, Process, ASP.NET Core, HttpClient, EF Core instrumentation)
- 配置服務名稱與版本 (HybridAuthIdP v1.0.0)
- 整合至 Program.cs 的服務註冊流程

✅ **分散式追蹤 (Distributed Tracing)**

- ASP.NET Core HTTP 請求追蹤（過濾 Vite dev server 路徑)
- HttpClient 出站請求追蹤
- Entity Framework Core 資料庫查詢追蹤（包含 SQL statements）
- Console exporter 用於開發環境偵錯

✅ **Metrics 收集**

- **Runtime Metrics**: GC collections, heap size, allocations, JIT compilation, thread pool, assemblies
- **Process Metrics**: Memory usage (physical & virtual), CPU time, thread count
- **ASP.NET Core Metrics**: Kestrel connections, TLS handshakes, HTTP requests, routing
- **DNS Lookup Metrics**: DNS 查詢時間與成功率

✅ **Prometheus Integration**

- `/metrics` endpoint 以 Prometheus 格式匯出 metrics
- IP 白名單保護機制（支援 CIDR notation）
- Development: 允許 localhost + 私有網段 (10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16)
- Production: 僅允許 localhost (127.0.0.1, ::1)
- 非白名單 IP 回傳 403 Forbidden
- 記錄所有被阻擋的訪問嘗試

✅ **安全性保護**

- PrometheusIpWhitelistMiddleware 中間件
- 支援 IPv4 和 IPv6 地址驗證
- CIDR 網段匹配演算法
- 環境變數配置 (Observability:PrometheusEnabled, Observability:AllowedIPs)
- 安全測試腳本 (`test-metrics-security.ps1`)

**配置檔案：**

- `Web.IdP/Program.cs` - OpenTelemetry 註冊與 IP 白名單中間件
- `Web.IdP/appsettings.json` - Production 白名單配置
- `Web.IdP/appsettings.Development.json` - Development 白名單配置
- `Web.IdP/Middleware/PrometheusIpWhitelistMiddleware.cs` - IP 白名單中間件
- `test-metrics-security.ps1` - 安全測試腳本

**測試結果：**

- ✅ Localhost (127.0.0.1, ::1) 訪問成功
- ✅ Prometheus metrics 格式正確輸出
- ✅ IP 白名單中間件正常運作
- ✅ 編譯無錯誤，僅有既存的 2 個 nullable 警告

**未來增強（選用）：**

- 自訂業務 metrics (IMetricsService)：登入次數、token 發行、授權請求等
- OpenTelemetry exporter 至 OTLP/Jaeger/Zipkin
- 單元測試覆蓋

---

## Phase 7.1: 基礎稽核日誌架構 (Audit Logging Infrastructure)

**目標：** 建立事件驅動的稽核日誌系統
**預估 token：** ~3000
**預估時間：** 2-3 天

**功能範圍：**

- 定義 AuditEvent 實體與相關 DTOs
- 實作 IAuditService 介面與 AuditService
- 建立 Domain Events 系統
- 新增 EF Core 遷移與索引優化
- 單元測試覆蓋 (100% passing)

**API Endpoints:**

- `GET /api/admin/audit/events` - 查詢稽核事件
- `POST /api/admin/audit/events/{id}/export` - 匯出特定事件

---

## Phase 7.2: 稽核日誌檢視器 UI (Audit Log Viewer UI)

**目標：** 建立管理員稽核日誌檢視介面
**預估 token：** ~2500
**預估時間：** 2 天

**功能範圍：**

- Vue.js 稽核日誌列表元件
- 進階篩選功能 (日期範圍、事件類型、使用者)
- 分頁與排序
- 匯出功能 (CSV/Excel)
- 即時更新機制

**UI 組件：**

- AuditLogViewer.vue
- AuditLogFilters.vue
- AuditLogExport.vue

---

## Phase 7.3: 異常登入管理 UI (Abnormal Login Management UI)

**目標：** 實作異常登入的手動管理介面
**預估 token：** ~2000
**預估時間：** 1-2 天

**功能範圍：**

- 顯示被標記為異常的登入記錄
- 管理員批准/拒絕異常登入
- IP 白名單管理
- 安全警報通知系統
- 整合至現有使用者管理介面

**UI 組件：**

- AbnormalLoginManager.vue
- LoginHistoryViewer.vue
- SecurityAlerts.vue

---

## Phase 7.4: 即時活動儀表板 (Real-time Activity Dashboard)

**目標：** 建立即時安全監控儀表板
**預估 token：** ~3000
**預估時間：** 3 天

**功能範圍：**

- WebSocket/SignalR 即時更新
- 安全指標視覺化 (圖表與統計)
- 活躍工作階段監控
- 失敗登入嘗試追蹤

更多細節請參閱 `docs/archive/PROJECT_STATUS_FULL.md` 中相應段落。
