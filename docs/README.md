# 📚 HybridIdP 文件指南

> 本目錄包含 HybridIdP 專案的所有文件。本指南幫助你快速找到需要的資訊。

## 🎯 快速導航

### 新 Session 開始時

**第一步：閱讀 [`WORKFLOW.md`](./WORKFLOW.md)**
- 📖 **用途：** 工作流程總覽和文件使用指南
- 🎯 **適合：** 新 session、不熟悉專案的開發者
- ⏱️ **閱讀時間：** 5-10 分鐘
- 📌 **必讀理由：** 了解如何使用其他文件，避免迷失方向

### 開始開發前

**第二步：查看 [`progress_todo.md`](./progress_todo.md)**
- 📖 **用途：** 待辦事項和下一步計畫
- 🎯 **適合：** 確認當前任務、規劃下一步
- ⏱️ **閱讀時間：** 2-3 分鐘
- 📌 **更新頻率：** 每完成一個 Phase 更新一次

**第三步：參考 [`implementation_guidelines.md`](./implementation_guidelines.md)**
- 📖 **用途：** 開發規範、範本、最佳實踐
- 🎯 **適合：** 實作 API、UI、測試時查閱
- ⏱️ **閱讀時間：** 按需查閱（不需全部閱讀）
- 📌 **包含內容：**
  - Hybrid 架構說明
  - API 實作範本
  - UI 實作範本
  - Tailwind CSS 設定
  - 測試範本
  - 常見陷阱

### 測試功能時

**第四步：參考 [`dev_testing_guide.md`](./dev_testing_guide.md)**
- 📖 **用途：** 開發和測試指南
- 🎯 **適合：** 啟動環境、執行測試
- ⏱️ **閱讀時間：** 3-5 分鐘
- 📌 **包含內容：**
  - 環境啟動步驟
  - 測試流程
  - 常見錯誤排除（⚠️ Vite 管理警告）

### 查看進度

**隨時查看 [`progress_completed.md`](./progress_completed.md)**
- 📖 **用途：** 已完成功能摘要
- 🎯 **適合：** 了解專案歷史、避免重複實作
- ⏱️ **閱讀時間：** 3-5 分鐘
- 📌 **更新頻率：** 每完成一個 Phase 新增摘要

---

## 📋 文件分類

### 🌟 核心文件（高頻使用）

| 文件 | 用途 | 更新頻率 | Token 大小 |
|------|------|----------|-----------|
| [`WORKFLOW.md`](./WORKFLOW.md) | 工作流程指南 | 穩定 | ~300 行 |
| [`implementation_guidelines.md`](./implementation_guidelines.md) | 開發規範和範本 | 穩定 | ~900 行 |
| [`progress_todo.md`](./progress_todo.md) | 待辦事項 | 每 Phase 更新 | ~300 行 |
| [`progress_completed.md`](./progress_completed.md) | 已完成摘要 | 每 Phase 更新 | ~200 行 |
| [`dev_testing_guide.md`](./dev_testing_guide.md) | 測試指南 | 穩定 | ~200 行 |

**總 Token 消耗（核心文件）：** ~1900 行 vs 原本 `idp_req_details.md` 1284 行
- ✅ 但現在**不需要每次全部讀取**
- ✅ 按需查閱，減少 50-70% token 消耗

### 📚 參考文件（按需查閱）

| 文件 | 用途 | 何時查閱 |
|------|------|----------|
| [`idp_req_details.md`](./idp_req_details.md) | 完整需求文件 | 需要細節規格時 |
| [`architecture_hybrid_bootstrap_vue.md`](./architecture_hybrid_bootstrap_vue.md) | 架構決策記錄 | 了解架構原因時 |
| [`idp_vue_mpa_structure.md`](./idp_vue_mpa_structure.md) | MPA 結構說明 | 設定 Vite 時 |
| [`turnstile_integration.md`](./turnstile_integration.md) | Turnstile 整合 | 實作 CAPTCHA 時 |
| [`idp_mfa_req.md`](./idp_mfa_req.md) | MFA 需求 | 實作 MFA 時 |

### 🗂️ 歷史記錄（很少使用）

| 文件 | 用途 |
|------|------|
| `idp_req.md` | 早期需求文件 |
| `idp_future_enhancements.md` | 未來增強功能 |
| `phase_3.2_dashboard_rewrite_plan.md` | Dashboard 重寫計畫 |
| `test_results_failure_scenarios.md` | 測試結果記錄 |

---

## 🔄 文件更新流程

### 完成一個 Phase 後

**步驟 1：更新 `progress_todo.md`**
```markdown
- [x] Phase 4.5: Role Management UI  # 標記為完成
```

**步驟 2：更新 `progress_completed.md`**
```markdown
## Phase 4.5: Role Management UI ✅

**完成時間：** 2025-11-XX

**功能摘要：**
- Role CRUD 完整實作
- Permission 分配管理
- ...（3-5 行摘要）

**API Endpoints:**
- GET /api/admin/roles
- ...

**驗證結果：**
- ✅ ...
```

**步驟 3：Commit**
```bash
git add docs/progress_*.md
git commit -m "docs: Update progress - Phase 4.5 completed"
```

### 發現新的最佳實踐時

**更新 `implementation_guidelines.md`**
- 新增範本
- 更新常見陷阱
- 提供範例程式碼

### 工作流程改變時

**更新 `WORKFLOW.md`**
- 修改開發流程
- 更新 Git 策略
- 調整檢查清單

---

## 💡 使用建議

### 給 AI Agent

**新 Session 啟動時：**
```
請讀取以下文件來了解專案：
1. docs/WORKFLOW.md - 工作流程
2. docs/progress_todo.md - 確認下一步
3. docs/implementation_guidelines.md - 查閱需要的範本
```

**開始實作時：**
```
請參考 docs/implementation_guidelines.md 中的：
- API 實作範本
- UI 實作範本
- Tailwind CSS 設定（⚠️ 重要）
```

**測試時：**
```
請參考 docs/dev_testing_guide.md 執行測試
注意 Vite 管理警告！
```

### 給開發者

1. **首次接觸專案：** 按順序閱讀 WORKFLOW.md → implementation_guidelines.md
2. **日常開發：** 只需查看 progress_todo.md 和 implementation_guidelines.md
3. **需要細節：** 再查閱 idp_req_details.md 相關 Phase

---

## 📊 Token 效率對比

### 之前（單一大文件）

```
每次 session 必須讀取:
- idp_req_details.md (1284 行)
- dev_testing_guide.md (200 行)
= 約 1484 行

Token 消耗: ~1500 行 × 每次
```

### 現在（模組化文件）

```
新 session 閱讀:
- WORKFLOW.md (300 行)
- progress_todo.md (300 行)
= 約 600 行

開發時查閱:
- implementation_guidelines.md (按需查閱，不需全讀)
- 平均查閱 200 行

Token 消耗: ~800 行 × 每次
節省: ~46%
```

**實際節省更多：**
- 熟悉專案後，只需 progress_todo.md (300 行)
- 查閱範本時，只看需要的 section
- 節省可達 **60-70%**

---

## ⚠️ 重要提醒

### 文件同步

- ✅ **DO:** 完成 Phase 立即更新 progress 文件
- ❌ **DON'T:** 累積多個 Phase 再一次更新

### 保持簡潔

- ✅ **DO:** `progress_completed.md` 使用 3-5 行摘要
- ❌ **DON'T:** 複製完整程式碼到文件中

### Tailwind CSS 警告

- ⚠️ **每個新 Vue SPA 必須：**
  1. 創建 `style.css`
  2. `main.js` 中 `import './style.css'`
  3. 參考 `implementation_guidelines.md` 範本

### Vite 管理

- ⚠️ **絕對不要：**
  1. 重複執行 `npm run dev`
  2. 開發時執行 `npm run build`
  3. 詳見 `dev_testing_guide.md`

---

## 🆘 找不到資訊？

### 檢查順序

1. **WORKFLOW.md** - 流程問題？
2. **implementation_guidelines.md** - 實作問題？
3. **dev_testing_guide.md** - 測試問題？
4. **idp_req_details.md** - 需求細節？

### 常見問題

**Q: 下一步要做什麼？**
→ 查看 `progress_todo.md`

**Q: 怎麼實作 API？**
→ 查看 `implementation_guidelines.md` > API 實作範本

**Q: Tailwind CSS 不工作？**
→ 查看 `implementation_guidelines.md` > 常見陷阱 #1

**Q: Vite 出錯？**
→ 查看 `dev_testing_guide.md` > 最常見錯誤

**Q: 需要完整需求？**
→ 查看 `idp_req_details.md` 對應 Phase

---

**建立時間：** 2025-11-04  
**維護者：** HybridIdP Team  
**版本：** 1.0

**記住：先讀 WORKFLOW.md，就知道該讀什麼！** 🚀
