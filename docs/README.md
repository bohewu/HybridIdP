# 📚 HybridIdP 文件指南

> 本目錄包含 HybridIdP 專案的所有文件。本指南幫助你快速找到需要的資訊。

## 🎯 快速導航

### 新 Session 開始時

**第一步：閱讀 [`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md)**
- 📖 **用途：** 工作流程總覽、開發規範、測試指南
- 🎯 **適合：** 新 session、不熟悉專案的開發者
- ⏱️ **閱讀時間：** 10-15 分鐘
- 📌 **必讀理由：** 了解如何使用其他文件，避免迷失方向

### 開始開發前

**第二步：查看 [`PROJECT_STATUS.md`](./PROJECT_STATUS.md)**
- 📖 **用途：** 待辦事項和下一步計畫、已完成功能摘要
- 🎯 **適合：** 確認當前任務、規劃下一步、了解專案歷史
- ⏱️ **閱讀時間：** 3-5 分鐘
- 📌 **更新頻率：** 每完成一個 Phase 更新一次

注意：專案已將大檔拆分以利維護與查閱。最新進度摘要請參見 `docs/PROJECT_PROGRESS.md`，各 Phase 的詳細說明已拆分至 `docs/phase-*.md`（例如 `docs/phase-5-security-i18n-consent.md`）。如需深入內容，請由 `PROJECT_PROGRESS.md` 點入對應 Phase 的檔案查閱。

**第三步：參考 [`ARCHITECTURE.md`](./ARCHITECTURE.md)**
- 📖 **用途：** 架構決策、技術棧詳解、安全考量
- 🎯 **適合：** 實作 API、UI、理解系統設計時查閱
- ⏱️ **閱讀時間：** 按需查閱（不需全部閱讀）
- 📌 **包含內容：**
  - Hybrid 架構說明
  - 技術棧詳解
  - 安全架構
  - 樣式策略
  - 效能考量
  - MPA 結構

### 測試功能時

**第四步：參考 [`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md) 中的測試指南**
- 📖 **用途：** 開發和測試指南
- 🎯 **適合：** 啟動環境、執行測試
- ⏱️ **閱讀時間：** 3-5 分鐘
- 📌 **包含內容：**
  - 環境啟動步驟
  - 測試流程
  - 常見錯誤排除（⚠️ Vite 管理警告）

### 查看功能與未來計畫

**隨時查看 [`FEATURES.md`](./FEATURES.md)**
- 📖 **用途：** 已實作功能細節、未來增強功能
- 🎯 **適合：** 了解特定功能（如 Turnstile、MFA）的實作細節和未來規劃
- ⏱️ **閱讀時間：** 3-5 分鐘
- 📌 **更新頻率：** 新功能實作後更新

---

## 📋 文件分類

### 🌟 核心文件（高頻使用）

| 文件 | 用途 | 更新頻率 | Token 大小 |
|------|------|----------|-----------|
| [`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md) | 開發工作流程、規範、測試指南 | 穩定 | ~2000 行 |
| [`PROJECT_STATUS.md`](./PROJECT_STATUS.md) | 專案進度、待辦事項、已完成摘要 | 每 Phase 更新 | ~1000 行 |

**總 Token 消耗（核心文件）：** ~3000 行
- ✅ 按需查閱，減少 50-70% token 消耗

### 📚 參考文件（按需查閱）

| 文件 | 用途 | 何時查閱 |
|------|--------|----------|
| [`idp_req_details.md`](./idp_req_details.md) | 完整需求文件 | 需要細節規格時 |
| [`ARCHITECTURE.md`](./ARCHITECTURE.md) | 架構決策與技術棧詳解 | 了解架構原因時 |
| [`FEATURES.md`](./FEATURES.md) | 功能細節與未來增強 | 實作特定功能時 |
| `docs/examples/` | 程式碼範例 | 實作時參考 |

---

## 🔄 文件更新流程

### 完成一個 Phase 後

**步驟 1：更新 [`PROJECT_STATUS.md`](./PROJECT_STATUS.md)**
```markdown
- [x] Phase 4.5: Role Management UI  # 標記為完成
```

**步驟 2：更新 [`PROJECT_STATUS.md`](./PROJECT_STATUS.md) 中的已完成摘要**
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
git add docs/PROJECT_STATUS.md
git commit -m "docs: Update progress - Phase 4.5 completed"
```

### 發現新的最佳實踐時

**更新 [`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md) 中的相關章節**
- 新增範本
- 更新常見陷阱
- 提供範例程式碼

### 工作流程改變時

**更新 [`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md) 中的工作流程章節**
- 修改開發流程
- 更新 Git 策略
- 調整檢查清單

---

## 💡 使用建議

### 給 AI Agent

**新 Session 啟動時：**
```
請讀取以下文件來了解專案：
1. docs/DEVELOPMENT_GUIDE.md - 工作流程、規範、測試指南
2. docs/PROJECT_STATUS.md - 確認下一步
3. docs/ARCHITECTURE.md - 查閱需要的架構說明
```

**開始實作時：**
```
請參考 docs/DEVELOPMENT_GUIDE.md 中的：
- API 實作範本
- UI 實作範本
- Tailwind CSS 設定（⚠️ 重要）
```

**測試時：**
```
請參考 docs/DEVELOPMENT_GUIDE.md 執行測試
注意 Vite 管理警告！
```

### 給開發者

1. **首次接觸專案：** 按順序閱讀 DEVELOPMENT_GUIDE.md → ARCHITECTURE.md → PROJECT_STATUS.md
2. **日常開發：** 只需查看 PROJECT_STATUS.md 和 DEVELOPMENT_GUIDE.md
3. **需要細節：** 再查閱 idp_req_details.md 或 FEATURES.md 相關 Phase

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
- DEVELOPMENT_GUIDE.md (約 1000 行)
- PROJECT_STATUS.md (約 500 行)
= 約 1500 行

開發時查閱:
- DEVELOPMENT_GUIDE.md (按需查閱，不需全讀)
- 平均查閱 200 行

Token 消耗: ~700 行 × 每次
節省: ~53%
```

**實際節省更多：**
- 熟悉專案後，只需 PROJECT_STATUS.md (500 行)
- 查閱範本時，只看需要的 section
- 節省可達 **60-70%**

---

## ⚠️ 重要提醒

### 文件同步

- ✅ **DO:** 完成 Phase 立即更新 [`PROJECT_STATUS.md`](./PROJECT_STATUS.md)
- ❌ **DON'T:** 累積多個 Phase 再一次更新

### 保持簡潔

- ✅ **DO:** [`PROJECT_STATUS.md`](./PROJECT_STATUS.md) 中的已完成摘要使用 3-5 行摘要
- ❌ **DON'T:** 複製完整程式碼到文件中

### Tailwind CSS 警告

- ⚠️ **每個新 Vue SPA 必須：**
  1. 創建 `style.css`
  2. `main.js` 中 `import './style.css'`
  3. 參考 [`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md) 範本

### Vite 管理

- ⚠️ **絕對不要：**
  1. 重複執行 `npm run dev`
  2. 開發時執行 `npm run build`
  3. 詳見 [`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md) 中的 Vite 管理章節

---

## 🆘 找不到資訊？

### 檢查順序

1. **[`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md)** - 流程、實作、測試問題？
2. **[`ARCHITECTURE.md`](./ARCHITECTURE.md)** - 架構、技術棧問題？
3. **[`FEATURES.md`](./FEATURES.md)** - 特定功能問題？
4. **[`idp_req_details.md`](./idp_req_details.md)** - 需求細節？

### 常見問題

**Q: 下一步要做什麼？**
→ 查看 [`PROJECT_STATUS.md`](./PROJECT_STATUS.md)

**Q: 怎麼實作 API？**
→ 查看 [`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md) > API 實作範本

**Q: Tailwind CSS 不工作？**
→ 查看 [`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md) > 常見陷阱 #1

**Q: Vite 出錯？**
→ 查看 [`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md) > 最常見錯誤

**Q: 需要完整需求？**
→ 查看 [`idp_req_details.md`](./idp_req_details.md) 對應 Phase

---

**建立時間：** 2025-11-04  
**維護者：** HybridIdP Team  
**版本：** 1.0

**記住：先讀 [`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md)，就知道該讀什麼！** 🚀
