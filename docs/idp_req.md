好的，這兩項都是非常精確且重要的修正，能讓 AI Agent 的執行更符合標準化實踐。

1.  **`docker compose` (V2 Command)**：使用現代的 Docker CLI 語法。
2.  **`dotnet new` Templates**：確保專案結構是透過 .NET 樣板標準建立，而不是手動建立。

我已將這兩項修正更新到以下的 AI Agent 執行計畫中。

---

## 專案：混合式身份驗證 IdP (Project: "HybridAuthIdP")
## 技術棧：.NET 8, ASP.NET Core, EF Core, OpenIddict 6.x, Vue 3
## 架構：Clean Architecture
## 關鍵需求：TDD, i18n (en-US, zh-TW), 階段性確認, `dotnet new`, `docker compose`

---

### 階段 0：專案架構與 TDD/i18n 基礎 (The Skeleton)

**目標：** 透過 `dotnet new` 樣板建立 Clean Architecture 結構，設定 Docker 環境，並注入 TDD 測試專案與 i18n 基礎設施。

1.  **建立解決方案 (Solution)：**
    * `dotnet new sln -n HybridAuthIdP`
2.  **建立核心專案 (Core Projects)：**
    * `dotnet new classlib -n Core.Domain`
    * `dotnet new classlib -n Core.Application`
    * (將專案加入 Solution)
    * `Core.Domain` (Entities: `ApplicationUser`, `UserAlias`, `PasswordHistory`, `SystemPolicy`)
    * `Core.Application` (Interfaces: `ILegacyAuthService`, `IPolicyService`, `IEmailSender`, `IApplicationDbContext`, `IJitProvisioningService`; DTOs: `LegacyUserDto`)
3.  **建立基礎設施專案 (Infrastructure Project)：**
    * `dotnet new classlib -n Infrastructure`
    * (將專案加入 Solution)
    * (Implementations: `ApplicationDbContext`, `LegacyAuthService`, `PolicyService`, `FakeEmailSender`, Identity components)
4.  **建立展示層專案 (Presentation Project)：**
    * `dotnet new webapp -n Web.IdP` (註：使用 `webapp` 樣板以支援 Razor Pages)
    * (將專案加入 Solution)
    * (Includes: `Program.cs`, Pages, API, Vue ClientApp)
5.  **建立測試專案 (Test Projects)：**
    * `dotnet new xunit -n Tests.Application.UnitTests`
    * `dotnet new xunit -n Tests.Infrastructure.IntegrationTests`
    * (將專案加入 Solution)
6.  **建立 Docker 環境：**
    * 建立 `docker-compose.yml` (包含 `idp-service`, `db-service`, `redis-service`)。
    * 建立 `Web.IdP/Dockerfile`。
7.  **(i18n) 注入本地化基礎設施 (`Web.IdP/Program.cs`)：**
    * `builder.Services.AddLocalization(...)`
    * `builder.Services.AddMvc().AddViewLocalization().AddDataAnnotationsLocalization();`
    * 設定 `supportedCultures = { "en-US", "zh-TW" }`
    * `app.UseRequestLocalization(...)`
8.  **(i18n) 建立資源檔資料夾：** (`Web.IdP/Resources`)

---
#### 🧪 階段 0：驗收標準

* **單元測試：** `dotnet build` 整個解決方案，必須成功。
* **整合測試：** `docker compose up` 必須成功啟動所有服務 (IdP, DB, Redis)。
* **(i18n) 程式碼審查：** 檢查 `Web.IdP/Program.cs` 是否已包含 `AddLocalization` 和 `UseRequestLocalization` 的設定。

---
#### 🚩 **階段 0 確認**

**Agent 動作：** 暫停。
**Agent 提問：** 「階段 0 (專案架構與 TDD/i18n 基礎) 已完成並通過驗收。專案均已透過 `dotnet new` 樣板建立，`docker compose` 可運作，i18n 中介軟體已配置。**請問是否可以開始執行階段 1 (本地帳號 OIDC 核心)？**」
---

### 階段 1：本地帳號 OIDC 核心 (The "Demoable" IdP)

**目標：** 實現「本地帳號」的完整 OIDC 登入/登出流程 (i18n)。

1.  **實作 `ApplicationUser`** (`Core.Domain`)。
2.  **實作 `DbContext`** (`Infrastructure`)。
3.  **設定 `Web.IdP` (`Program.cs`)：**
    * 注入 `DbContext`、`ASP.NET Identity`、`OpenIddict 6.x`、`FakeEmailSender`。
    * 硬編碼一個測試 Client (用於本階段測試)。
    * 執行資料庫遷移 (Migration)。
4.  **(i18n) 建立 UI 頁面與資源檔 (`Web.IdP`)：**
    * 建立 `Pages/Account/Login.cshtml`, `Logout.cshtml`, `Register.cshtml`, `Consent.cshtml`。
    * **[i18n]** 為上述頁面建立 `.resx` 資源檔 (for `zh-TW`, `en-US`)。
    * **[i18n]** 確保所有 `.cshtml` 頁面使用 `@Localizer["Key"]`。
5.  **(i18n) 實作 UI 後端邏輯 (`.cshtml.cs`)：**
    * 實作 `Register` (`UserManager.CreateAsync`)。
    * 實作 `Login` (`SignInManager.PasswordSignInAsync`)。
    * 實作 `Logout` (OIDC 登出)。
    * 實作 `Consent` (OIDC 同意)。
    * **[i18n]** 確保所有後端錯誤訊息 (e.g., `ModelState`) 均使用 `_localizer["Key"]`。
6.  **建立測試用戶端 (Test Client)：**
    * `dotnet new mvc -n TestClient` (建立一個獨立的 MVC 專案)。

---
#### 🧪 階段 1：驗收標準

* **整合/手動測試 (關鍵)：**
    * **[測試 1-6]** 執行「本地帳號 OIDC 核心」測試流程 (註冊 -> 登入 -> 同意 -> 導回 Client -> 登入成功)。
    * **[測試 7 (i18n)]** 造訪 `/Account/Login?culture=zh-TW` / `en-US`，頁面**必須**顯示正確語系。
    * **[測試 8 (i18n)]** 提交空表單，`ModelState` 驗證錯誤訊息**必須**是 i18n。

---
#### 🚩 **階段 1 確認**

**Agent 動作：** 暫停。
**Agent 提問：** 「階段 1 (本地帳號 OIDC 核心) 已完成並通過驗收。使用者現在可以使用本地帳號，透過 OIDC 流程登入測試 App，且登入/同意介面完整支援 i18n。**請問是否可以開始執行階段 2 (JIT 佈建與混合驗證)，我們將在此階段啟動 TDD 流程？**」
---

### 階段 2：JIT 佈建與混合驗證 (TDD 驅動)

**目標：** **(TDD)** 建立 JIT 佈建服務；**(整合)** 替換登入邏輯為「舊系統驗證」。

1.  **定義介面與 DTO (`Core.Application`)：**
    * `ILegacyAuthService` (`Task<LegacyUserDto> ValidateAsync(...)`)
    * `LegacyUserDto` (包含 `IsAuthenticated`, `IdCardNumber`, `FullName` 等)
    * **[TDD 目標]** `IJitProvisioningService` (定義 `Task<ApplicationUser> ProvisionUserAsync(LegacyUserDto dto)`)
2.  **[TDD Red] 建立失敗測試 (`Tests/Application.UnitTests`)：**
    * 建立 `JitProvisioningServiceTests.cs`。
    * Mock `UserManager<ApplicationUser>`。
    * **[Red 1]** 建立測試 `ProvisionUser_When_User_Is_New_Should_Call_CreateAsync`。
    * **[Red 2]** 建立測試 `ProvisionUser_When_User_Exists_Should_Call_UpdateAsync`。
3.  **[TDD Green] 實作業務邏輯 (`Core.Application` / `Infrastructure`)：**
    * 建立 `JitProvisioningService` (實作 `IJitProvisioningService`)。
    * 注入 `UserManager`。
    * 實作 `ProvisionUserAsync` 邏輯，**直到 [Red 1] 和 [Red 2] 測試通過**。
4.  **實作 `LegacyAuthService` (`Infrastructure`)：**
    * 實作 `ILegacyAuthService`。
    * 5.  **修改登入邏輯 (`Web.IdP/Pages/Account/Login.cshtml.cs`)：**
    * 注入 `ILegacyAuthService` 和 `IJitProvisioningService`。
    * **[核心邏輯]** 替換 `SignInManager.PasswordSignInAsync`。
        1.  呼叫 `_legacyAuthService.ValidateAsync()`。
        2.  如果失敗，回傳 `_localizer["InvalidLoginAttempt"]`。
        3.  如果成功，呼叫 `ApplicationUser user = await _jitProvisioningService.ProvisionUserAsync(dto)`。
        4.  呼叫 `await signInManager.SignInAsync(user, isPersistent: false)`。
6.  **實作 Claims Factory (`Infrastructure/Identity`)：**
    * 建立 `MyUserClaimsPrincipalFactory`，覆寫 `GenerateClaimsAsync`。
    * 在 `Program.cs` 中註冊。

---
#### 🧪 階段 2：驗收標準

* **單元測試 (`Application.UnitTests`)：**
    * **[測試 1]** `JitProvisioningServiceTests.cs` 中的所有 TDD 測試**必須** 100% 通過。
* **整合/手動測試：**
    * **[測試 2-4]** 使用「舊系統」有效帳號登入，成功建立/更新 `AspNetUsers` 資料表，並導回 Client。
    * **[測試 5]** 登入 Client 後，檢查 Token，**必須**包含 `FullName`, `Department` 等自訂 Claims。
    * **[測試 6 (i18n)]** 使用**錯誤**的舊系統密碼登入，頁面**必須**顯示 i18n 的「無效的帳號或密碼」錯誤訊息。

---
#### 🚩 **階段 2 確認**

**Agent 動作：** 暫停。
**Agent 提問：** 「階段 2 (JIT 佈建與混合驗證) 已完成並通過驗收。JIT 服務已通過 TDD 測試，系統現在使用舊系統 API 進行驗證，能即時佈建使用者，並將 Claims 注入 Token。**請問是否可以開始執行階段 3 (Admin API 與管理介面)？**」
---

### 階段 3：Admin API 與管理介面 (The "Management" Layer)

**目標：** 建立管理 Client, Scope, Claims 的後端 API 與前端 UI 基礎。

1.  **建立 API Controllers (`Web.IdP/Api/Admin/`)：**
    * `ClientsController.cs` (CRUD `OpenIddictApplication`)
    * `ScopesController.cs` (CRUD `OpenIddictScope`)
    * `ClaimsController.cs` (管理「全域 Claim 定義」)
2.  **實作 API Endpoints (CRUD)：**
    * `ScopesController` 需提供 `[POST] {scopeId}/claims` 端點，用於**綁定** Scope 可用的 Claims。
3.  **移除硬編碼 Client：** 移除 `Program.cs` 中的測試 Client。
4.  **設定 Vue 3 MPA (`Web.IdP/`)：**
    * 設定 `Vite.AspNetCore` 中介軟體。
    * 建立 `ClientApp/` (Vue 3 + Vite + Tailwind)。
    * 建立 Admin UI 頁面 (Clients, Scopes) 並呼叫後端 API。

---
#### 🧪 階段 3：驗收標準

* **整合/手動測試：**
    * **[測試 1]** 透過 Admin UI (或 Postman) 成功建立一個 Client。
    * **[測試 2]** 透過 Admin UI 成功建立一個 Scope (例如 `my_api_scope`)。
    * **[測試 3]** 透過 Admin UI 將 `full_name` Claim 綁定到 `my_api_scope`。
    * **[測試 4]** 使用 [測試 1] 的 Client 登入，並請求 `my_api_scope`。
    * **[測試 5]** 檢查 Token，**必須**包含 `full_name`。

---
#### 🚩 **階段 3 確認**

**Agent 動作：** 暫停。
**Agent 提問：** 「階段 3 (Admin API 與管理介面) 已完成並通過驗收。Admin 現在可以透過 UI 動態管理 Client 和 Scope (包含 Claims 綁定)。**請問是否可以開始執行階段 4 (動態安全策略)，我們將在此階段重度使用 TDD？**」
---

### 階段 4：動態安全策略 (TDD 驅動)

**目標：** **(TDD)** 建立動態安全策略驗證器；**(整合)** 確保 Identity 錯誤訊息 i18n。

1.  **(i18n) 建立 `MultiLingualIdentityErrorDescriber` (`Infrastructure/Identity`)：**
    * 建立此類別 (繼承 `IdentityErrorDescriber`)。
    * 注入 `IStringLocalizer`，覆寫 `PasswordTooShort`, `DuplicateUserName` 等方法，回傳 i18n 錯誤。
    * 建立對應的 `.resx` 資源檔。
2.  **[TDD Red] 建立失敗測試 (`Tests/Infrastructure.UnitTests`)：**
    * 建立 `DynamicPasswordValidatorTests.cs`。
    * Mock `IPolicyService` 和 `PasswordHasher<ApplicationUser>`。
    * **[Red 1]** 建立測試 `Validate_When_Policy_Requires_10_Chars_And_Password_Is_8_Should_Fail`。
    * **[Red 2]** 建立測試 `Validate_When_Password_Is_In_History_Should_Fail`。
    * **[Red 3]** 建立測試 `Validate_When_Password_Is_Ok_Should_Success`。
3.  **[TDD Green] 實作 `DynamicPasswordValidator` (`Infrastructure/Identity`)：**
    * 實作 `IPasswordValidator<ApplicationUser>`。
    * 注入 `IPolicyService`、`PasswordHasher`、`IStringLocalizer`。
    * 實作 `ValidateAsync` 邏輯，**直到 [Red 1-3] 測試通過**。
    * 驗證失敗時，回傳 i18n 的 `IdentityError`。
4.  **建立 Admin UI & API (`Web.IdP`)：**
    * `PoliciesController.cs` (API `Get`/`Put`)，用於 Admin UI 更新 `SystemPolicy` 資料表。
    * Vue UI 頁面 (`/admin/settings/policies`)。
5.  **註冊動態驗證器 (`Web.IdP/Program.cs`)：**
    * `builder.Services.AddIdentity(...)`
    * `.AddPasswordValidator<DynamicPasswordValidator>()`
    * `.AddErrorDescriber<MultiLingualIdentityErrorDescriber>()`
6.  **實作密碼期限：**
    * * 在「變G8密碼」邏輯中檢查 `PasswordMinAge`。
    * 在 `Login.cshtml.cs` 中檢查 `PasswordMaxAge`。
    * 變更密碼成功後，更新 `user.LastPasswordChangedDate` 並儲存 `PasswordHistory`。

---
#### 🧪 階段 4：驗收標準

* **單元測試 (`Tests/Infrastructure.UnitTests`)：**
    * **[測試 1]** `DynamicPasswordValidatorTests.cs` 中的所有 TDD 測試**必須** 100% 通過。
* **整合/手動測試：**
    * **[測試 2-5]** 驗證「動態策略」與「密碼歷史」：Admin UI (設長度 15) -> 註冊/改密碼 (10 碼) 失敗 -> (改回 8) -> 成功 -> (再改回) -> 失敗 (歷史)。
    * **[測試 6 (i18n)]** 在 [測試 2] 中，**必須**顯示 i18n 的錯誤訊息 (例如 "密碼長度至少需 15 碼。")。
    * **[測試 7 (i18n)]** 嘗試註冊重複帳號，**必須**顯示 i18n 的「帳號已被使用」錯誤。

---
#### 🚩 **階段 4 確認**

**Agent 動作：** 暫停。
**Agent 提問：** 「階段 4 (動態安全策略) 已完成並通過 TDD 驗收。密碼複雜度、歷史、期限均可由 Admin 動態設定，且所有 Identity 相關的錯誤訊息均已支援 i18n。**請問是否可以開始執行階段 5 (Production 強化)？**」
---

### 階段 5：Production 強化

**目標：** 補完所有基礎設施，使其可上線。

1.  **實作 Email 服務 (`Infrastructure`)：**
    * 建立 `SmtpEmailSender` (實作 `IEmailSender`)。
    * 建立 `IEmailPolicyService` + Admin UI/API (用於管理 SMTP 設定)。
    * 在 `Program.cs` 中，根據環境變數注入 `FakeEmailSender` 或 `SmtpEmailSender`。
2.  **整合 Redis：**
    * `AddStackExchangeRedisCache`。
    * OpenIddict `.UseRedis()`。
3.  **整合 Token 清理：**
    * `AddQuartz()` + OpenIddict `.UseQuartz()`。
4.  **實作稽核 (Auditing)：**
    * ---
#### 🧪 階段 5：驗收標準

* **整合/手動測試：**
    * **[測試 1]** Admin UI 設定 SMTP -> 點擊「測試寄送」 -> 成功收到 Email。
    * **[測試 2]** 執行「忘記密碼」流程 (本地帳號) -> 成功收到重設信件。
    * **[測試 3]** 檢查 Redis CLI (`MONITOR`)，應能看到 OpenIddict 讀寫快取。
    * **[測試 4]** 檢查資料庫，`OpenIddictTokens` 中過期的 Token 應被 Quartz Job 自動刪除。

---
#### 🚩 **階段 5 確認**

**Agent 動作：** 暫停。
**Agent 提問：** 「階段 5 (Production 強化) 已完成並通過驗收。Email 服務、Redis 快取、Token 自動清理均已配置完成。**專案已達 Production-Ready 狀態。請問是否還有後續任務？**」
---