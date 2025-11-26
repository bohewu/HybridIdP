---
title: "Project Progress Summary"
owner: HybridIdP Team
last-updated: 2025-11-21
---

# 專案進度摘要

此檔案為快速進度總覽，列出每個 Phase 的完成狀態與指向詳細說明的連結。

- Phase 1 — PostgreSQL & EF Core: 100% — `docs/phase-1-database-ef-core.md`
- Phase 2 — OpenIddict & OIDC Flow: 100% — `docs/phase-2-openiddict-oidc.md`
- Phase 3 — Admin UI: 100% — `docs/admin-ui-phase-3.md`
- Phase 4 — User/Role/Client & Permissions: 100% — `docs/phase-4-user-role-client.md`
- Phase 5 — Security / i18n / Consent / API Resources: 96% — `docs/phase-5-security-i18n-consent.md`
- Phase 6 — Code Quality & Tests: 90% — `docs/phase-6-code-quality-tests.md`
- Phase 7 — Audit & Monitoring: 100% — `docs/phase-7-audit-monitoring.md`
- Phase 8 — e2e Test Refactor: 100% — `docs/phase-8-e2e-refactor.md`
- Phase 9 — Scope Authorization & Management: 0% — `docs/phase-9-scope-authorization.md`
- Phase 10 — Person & Identity: 0% — `docs/phase-10-person-identity.md`

Backlog & Technical Debt: `docs/backlog-and-debt.md`

Notes & Guidelines: `docs/notes-and-guidelines.md`

說明：

- 每個 Phase 檔案包含該 Phase 的摘要、已完成要點與重要檔案路徑。
- 如需更完整的歷史紀錄或截圖證據，請參閱 `docs/PROJECT_STATUS.md`（Archive）。

近期更新紀錄：

## 2025-11-21: E2E Test Coverage Expansion (47 tests total, 100% passing)

**新增功能測試覆蓋 (Admin UI Features):**

1. **Settings E2E Tests** (2 tests)
   - ✅ Branding CRUD - update app name/product name, verify persistence
   - ✅ Validation - empty field handling

2. **Security Policies E2E Tests** (3 tests)
   - ✅ Password requirements CRUD - minLength, maxFailedAttempts
   - ✅ Validation - min/max bounds testing
   - ✅ Account lockout configuration - lockoutDuration changes

3. **Claims E2E Tests** (2 tests)
   - ✅ Claims CRUD - create/update/delete custom claims (with permission management)
   - ✅ Standard claim protection - verify system claims immutability

4. **Audit Log E2E Tests** (5 tests)
   - ✅ Viewer load and pagination
   - ✅ Filter by event type
   - ✅ Search by user
   - ✅ Refresh functionality
   - ✅ Date range filter (graceful fallback)

**技術改進:**

- 動態權限管理：測試中臨時添加 Claims.Create/Update/Delete 權限到 Admin 角色
- Modal 表單選擇器：使用 `modalForm.locator()` 避免選到頁面其他元素
- 錯誤處理：10 秒 API 響應超時，快速失敗機制

**測試狀態:**

- 總測試數：35 → 47 (+12 tests, +34%)
- 通過率：47/47 (100%)
- 執行時間：~42s (4 parallel workers)

**待完成 E2E 測試:**

- [ ] Users UI CRUD tests
- [ ] User Sessions management tests  
- [ ] Dashboard metrics tests

---

## 2025-11-26: Phase 9 Scope Authorization & Management - Planning Complete

**新 Phase 架構:**

- Phase 9 改為：Scope Authorization & Management (全新功能)
- Phase 10 改為：Person & Identity (原 Phase 9)

**Phase 9 Sub-phases 規劃:**

1. **Phase 9.1: Consent Page Required Scope Support**
   - 新增 ClientRequiredScope entity 儲存 per-client required scopes
   - Consent page UI 顯示 required scope 為 disabled (不可取消勾選)
   - Server-side 驗證防止竄改
   - 測試：Unit + Integration + E2E

2. **Phase 9.2: Scope Authorization Handler & Policy Provider**
   - 實作 ScopeRequirement, ScopeAuthorizationHandler, ScopeAuthorizationPolicyProvider
   - 支援屬性語法：`[Authorize(Policy = "RequireScope:api:company:read")]`
   - 測試：Unit + Integration (in-memory)

3. **Phase 9.3: OpenID Userinfo Endpoint Scope Protection**
   - 保護 `/connect/userinfo` 需要 openid scope (OIDC 規範)
   - E2E 測試完整 HTTPS flow

4. **Phase 9.4: Client Scope Management UI Optimization**
   - 重構 client 註冊的 scope 設定 UI
   - 支援 Allowed / Required 雙欄位設定
   - 搜尋、分頁功能應對大量 custom scopes
   - 驗證：required scope 必須在 allowed scopes 中

5. **Phase 9.5: Modal/Dialog UX Consistency**
   - 檢視所有 admin UI 的 modals
   - 確保 ESC 鍵可關閉
   - 右上角 close icon 一致性
   - E2E 測試 modal 行為

6. **Phase 9.6: E2E Testing & Documentation**
   - 完整 scope authorization flow E2E 測試
   - 文件：ARCHITECTURE.md, SCOPE_AUTHORIZATION.md
   - 開發者指南與範例

**目前狀態:**

- 已復原所有暫存變更 (working tree clean)
- Phase 9 詳細計畫已撰寫
- Phase 10 (Person Identity) 已重新編號
- 準備開始 Phase 9.1 實作

