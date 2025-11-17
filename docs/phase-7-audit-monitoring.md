---
title: "Phase 7: Audit & Monitoring System"
owner: HybridIdP Team
last-updated: 2025-11-17
percent-complete: 100
---

# Phase 7: Audit & Monitoring System

Phase 7 將實作完整的稽核與監控系統，分為多個子階段以控制開發複雜度與 token 消耗

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
