# Next Session: Person Multi-Account E2E Tests

## 當前狀態

✅ **已完成：**
- Person Multi-Account Architecture 已實作完成
- JitProvisioningService 重構完成並通過所有單元測試 (8/8)
- 資料庫 migrations 已建立並套用 (SQL Server & PostgreSQL)
- Person 實體已新增 Email 和 PhoneNumber 欄位
- ExternalAuthResult DTO 已建立
- 支援身分證件優先匹配 (NationalId/Passport/ResidentCertificate)
- Email 備援匹配機制已實作

## 下一步任務：Person Multi-Account E2E Tests

### 任務目標

為 Person Multi-Account Architecture 撰寫 E2E 測試，驗證以下場景：

### 測試場景

#### 1. **首次外部登入 - 建立 Person + ApplicationUser + AspNetUserLogins**
```typescript
test('First-time external login should create Person and ApplicationUser with external login', async ({ page }) => {
  // Given: 一個新的外部認證使用者（AD/Google/Facebook）
  // When: 首次登入
  // Then: 
  //   - 建立新的 Person 記錄
  //   - 建立新的 ApplicationUser 並連結到 Person
  //   - 建立 AspNetUserLogins 記錄連結外部認證
});
```

#### 2. **重複登入 - 更新現有使用者資訊**
```typescript
test('Repeat login should update existing ApplicationUser information', async ({ page }) => {
  // Given: 已存在的外部認證使用者
  // When: 再次登入（可能資訊有更新，如 Department 變更）
  // Then: 
  //   - 不建立新的 Person 或 ApplicationUser
  //   - 更新 ApplicationUser 的資訊（Email, Department, JobTitle 等）
});
```

#### 3. **同一個 Person，不同 Provider - 使用相同 Person**
```typescript
test('Same person with different provider should reuse existing Person', async ({ page }) => {
  // Given: 
  //   - User 已用 AD 帳號登入過（john.doe@company.com）
  //   - 現在用 Google 帳號登入（同樣 Email: john.doe@company.com）
  // When: 用 Google 登入
  // Then: 
  //   - 不建立新的 Person
  //   - 建立新的 ApplicationUser 連結到同一個 Person
  //   - 建立新的 AspNetUserLogins 記錄
  //   - 確認 PersonId 相同
});
```

#### 4. **身分證件匹配 - 優先使用身分證件判斷同一個 Person**
```typescript
test('Identity document matching should take priority over email matching', async ({ page }) => {
  // Given: 
  //   - User A 已登入，NationalId = "A123456789", Email = "old@mail.com"
  //   - User B 用不同 Provider 登入，NationalId = "A123456789", Email = "new@mail.com"
  // When: User B 登入
  // Then: 
  //   - 雖然 Email 不同，但因為 NationalId 相同
  //   - 應該使用同一個 Person 記錄
  //   - 建立新的 ApplicationUser 連結到該 Person
});
```

#### 5. **無 Email 的使用者 - Username 使用 Provider_ProviderKey 格式**
```typescript
test('User without email should use Provider_ProviderKey as username', async ({ page }) => {
  // Given: 外部認證沒有提供 Email
  // When: 登入
  // Then: 
  //   - Username = "{Provider}_{ProviderKey}"
  //   - EmailConfirmed = false
  //   - 成功建立 Person 和 ApplicationUser
});
```

#### 6. **Admin UI 查詢 - 顯示同一個 Person 的所有帳號**
```typescript
test('Admin UI should display all accounts linked to same Person', async ({ page }) => {
  // Given: 
  //   - 一個 Person 有 3 個帳號（AD, Google, Local）
  // When: 在 Admin UI 查詢該 Person
  // Then: 
  //   - 能看到該 Person 的所有 ApplicationUser 記錄
  //   - 能看到每個 ApplicationUser 的 external login provider
});
```

### 測試資料準備

建議在 `setup-test-api-resources.ps1` 或建立新的測試資料腳本：

```powershell
# create-multi-account-test-data.ps1

# Person 1: John Doe (有 AD 和 Google 兩個帳號)
# - AD Account: john.doe@ad
# - Google Account: google-id-123

# Person 2: Jane Smith (只有 Local 密碼帳號)
# - Local Account: jane.smith@company.com

# Person 3: Bob Wang (有身分證件，用於測試身分證件匹配)
# - NationalId: A123456789
# - First login: AD with old@mail.com
```

### 實作步驟

1. **準備測試資料腳本**
   - 建立 `scripts/create-multi-account-test-data.ps1`
   - 或修改現有的 `setup-test-api-resources.ps1`

2. **撰寫 E2E 測試**
   - 路徑: `e2e/person-multi-account.spec.ts`
   - 使用 Playwright
   - 涵蓋上述 6 個場景

3. **API 測試**（選用）
   - 如果有 Admin API endpoints 可以查詢 Person 和 Accounts
   - 撰寫 API 測試驗證資料正確性

4. **執行測試**
   ```powershell
   # PostgreSQL E2E Test
   pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-e2e-postgres.ps1 -UpCompose -StartServices -NormalizePermissions -SeedApiResources -TimeoutSeconds 600
   
   # 或單獨執行 Person Multi-Account 測試
   npx playwright test person-multi-account
   ```

### 預期結果

所有測試通過，驗證：
- ✅ Person Multi-Account Architecture 運作正常
- ✅ 身分證件優先匹配邏輯正確
- ✅ Email 備援匹配邏輯正確
- ✅ 外部登入連結正確建立
- ✅ Admin UI 能正確顯示 Person 的所有帳號

### 參考文件

- `docs/PERSON_MULTI_ACCOUNT_ARCHITECTURE.md` - 完整架構說明
- `docs/REFACTOR_JIT_PROVISIONING_PROMPT.md` - JIT Provisioning 重構細節
- `e2e/README.md` - E2E 測試執行指南

### 重要提醒

⚠️ **測試前確認：**
1. 確保資料庫已套用最新的 migrations
2. 確認 JitProvisioningService 的 DI 註冊正確（需要 IApplicationDbContext）
3. 測試資料應該隔離，避免影響其他測試

---

## Prompt for AI Agent (下一個 Session)

```
我需要為 Person Multi-Account Architecture 撰寫 E2E 測試。

背景：
- 已完成 JitProvisioningService 重構，支援 Person Multi-Account 架構
- 一個 Person 可以有多個 ApplicationUser（不同的登入方式）
- 支援身分證件優先匹配和 Email 備援匹配
- 資料庫 migrations 已套用完成

請參考 `docs/NEXT_SESSION_PERSON_E2E_TESTS.md` 文件，協助我：

1. 檢視現有的 E2E 測試架構（e2e/ 目錄）
2. 準備測試資料（可能需要新建或修改現有的 setup scripts）
3. 撰寫 6 個 E2E 測試場景（如文件所列）
4. 執行測試並確保全部通過

重點測試場景：
- 首次外部登入建立完整記錄
- 重複登入更新資訊
- 同 Email 不同 Provider 使用同一 Person
- 身分證件匹配優先於 Email
- 無 Email 使用者的 Username 生成
- Admin UI 顯示同一 Person 的所有帳號

請先閱讀相關文件，然後提出測試實作計畫。
```
