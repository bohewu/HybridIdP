# HybridAuth IdP 功能與未來增強

## 🎯 簡介

本文件整合了 HybridAuth IdP 的現有功能說明、未來增強計畫以及特定功能（如 MFA 和 Cloudflare Turnstile）的詳細要求。旨在提供一個集中化的視圖，以便理解專案的發展方向和各項功能的實作細節。

---

## 1. Cloudflare Turnstile 整合

### 概述

Turnstile 已整合到**登入**和**註冊**頁面，提供可選的 CAPTCHA 保護，以防範自動化攻擊和濫用。此整合設計為在禁用時優雅降級，確保應用程式在沒有 Turnstile 憑證的情況下仍能正常運作。

### 配置

Turnstile 設定在 `appsettings.json` 中配置：

```json
// See docs/examples/features_turnstile_config.json.example
```

#### 設定

- **Enabled** (bool): 設定為 `true` 以在登入和註冊頁面啟用 Turnstile CAPTCHA。設定為 `false` 則禁用（預設）。
- **SiteKey** (string): 您的 Cloudflare Turnstile 網站金鑰（向用戶顯示的公開金鑰）。
- **SecretKey** (string): 您的 Cloudflare Turnstile 秘密金鑰（用於伺服器端驗證）。

#### 獲取 Turnstile 金鑰

1. 登入您的 [Cloudflare 儀表板](https://dash.cloudflare.com/)。
2. 導航到側邊欄中的 **Turnstile**。
3. 點擊 **新增網站**。
4. 配置您的網站：
   - **網站名稱**: 例如 "HybridAuthIdP"
   - **網域**: 開發環境為 `localhost`，或您的生產網域。
   - **小工具模式**: 選擇 "Managed"（推薦）或 "Non-interactive"。
5. 點擊 **建立**。
6. 複製 **網站金鑰** 和 **秘密金鑰**，並將它們添加到您的 `appsettings.json` 或環境變數中。

#### 範例配置 (啟用)

```json
// See docs/examples/features_turnstile_enabled_config.json.example
```

### 運作方式

#### 啟用時

1.  **登入和註冊頁面**在表單欄位下方顯示 Turnstile 小工具。
2.  用戶必須在提交表單前完成 CAPTCHA 挑戰。
3.  表單提交時：
    -   客戶端將 Turnstile 回應令牌發送到伺服器。
    -   伺服器使用 `ITurnstileService` 與 Cloudflare 的 API 驗證令牌。
    -   如果驗證失敗，表單將被拒絕並顯示錯誤訊息。

#### 禁用時

-   Turnstile 小工具**不會**在登入和註冊頁面渲染。
-   Turnstile 腳本**不會**加載。
-   伺服器端驗證**被跳過**，表單提交正常進行。

### 服務實作

Turnstile 驗證服務實作於：

-   **介面**: `Core.Application/ITurnstileService.cs`
-   **實作**: `Infrastructure/TurnstileService.cs`

服務在 `Web.IdP/Program.cs` 中註冊：

```csharp
// See docs/examples/features_turnstile_service_registration.cs.example
```

#### 驗證邏輯

`TurnstileService.ValidateTokenAsync` 方法：

1.  檢查 Turnstile 是否啟用；如果沒有，則返回 `true` (通過)。
2.  將 Turnstile 回應令牌和可選的遠端 IP 發送到 `https://challenges.cloudflare.com/turnstile/v0/siteverify`。
3.  解析 JSON 回應並返回驗證結果。

### 測試

#### 測試 Turnstile 禁用 (預設)

1.  確保 `appsettings.json` 中 `"Turnstile:Enabled": false`。
2.  啟動 IdP: `dotnet run --launch-profile https`
3.  導航到 `https://localhost:7035/Account/Login` 或 `/Account/Register`。
4.  驗證 Turnstile 小工具**沒有**出現。
5.  提交表單；它應該在沒有 CAPTCHA 驗證的情況下正常運作。

#### 測試 Turnstile 啟用

1.  從 Cloudflare 獲取 Turnstile 金鑰（參見上文）。
2.  更新 `appsettings.json`:

```json
// See docs/examples/features_turnstile_enabled_test_config.json.example
```

3.  啟動 IdP: `dotnet run --launch-profile https`
4.  導航到 `https://localhost:7035/Account/Login` 或 `/Account/Register`。
5.  驗證 Turnstile 小工具**出現**在提交按鈕之前。
6.  完成 CAPTCHA 並提交表單。
7.  如果您跳過 CAPTCHA 或它失敗，您應該會看到錯誤訊息。

### 生產環境考量

-   **安全秘密**: 不要將 `SiteKey` 和 `SecretKey` 提交到版本控制。使用環境變數或秘密管理服務（例如 Azure Key Vault、AWS Secrets Manager）。
-   **網域白名單**: 確保您的生產網域已添加到 Cloudflare 中的 Turnstile 網站配置。
-   **速率限制**: Turnstile 有助於防止濫用，但請考慮在您的伺服器上實施額外的速率限制。

### 未來增強

-   將 Turnstile 添加到其他敏感表單（例如密碼重置、帳戶恢復）。
-   支援 Turnstile 的「隱形」模式，以提供無縫的用戶體驗。
-   記錄和監控 Turnstile 驗證失敗，以獲取安全洞察。

---

## 2. 多因素認證 (MFA)

### 概述

本節概述了在 HybridAuthIdP 專案中實施多因素認證 (MFA) 的要求。此功能計劃在未來版本中實施，不屬於初始實施的一部分。

### Phase X: 多因素認證

**目標:** 透過添加第二個認證因素來增強用戶安全性。初始實施將專注於基於時間的一次性密碼 (TOTP)。

**完成定義:**
-   用戶可以透過自助服務門戶啟用和禁用其帳戶的 MFA。
-   啟用 MFA 後，登入流程要求用戶在密碼驗證後輸入 TOTP。
-   如果用戶丟失其 MFA 設備，IdP 提供一種機制來恢復其帳戶。

#### 步驟:

1.  **擴展用戶實體和 DbContext:**
    -   在 `Core.Domain` 中的 `ApplicationUser` 添加屬性以支援 MFA：
        ```csharp
        // See docs/examples/features_mfa_application_user_extension.cs.example
        ```
    -   更新 `ApplicationDbContext` 並創建新的資料庫遷移。

2.  **實施 TOTP 邏輯:**
    -   在 `Core.Application` 中添加服務（例如 `ITotpService`），並在 `Infrastructure` 中實施。
    -   此服務將負責：
        -   為用戶生成新的秘密金鑰。
        -   生成 QR 碼 URI（例如 `otpauth://totp/...`）。
        -   根據秘密金鑰驗證用戶提供的 TOTP 碼。

3.  **創建用戶自助服務 UI 以進行 MFA 管理:**
    -   在「用戶帳戶管理」門戶（來自 Phase 6）中，添加一個新的 MFA 部分。
    -   **啟用 MFA 流程:**
        1.  用戶點擊「啟用 MFA」。
        2.  系統生成秘密金鑰並將其顯示為 QR 碼和手動輸入金鑰。
        3.  用戶使用認證器應用程式（例如 Google Authenticator、Authy）掃描 QR 碼。
        4.  用戶從其應用程式輸入 TOTP 以驗證並啟用 MFA。
        5.  系統向用戶提供一組一次性恢復碼。
    -   **禁用 MFA 流程:**
        1.  用戶必須進行認證（可能帶有 TOTP 碼）才能禁用 MFA。

4.  **將 MFA 整合到登入流程中:**
    -   在 `Web.IdP/Pages/Account/Login.cshtml.cs` 中，在成功密碼登入後，檢查用戶是否已啟用 MFA。
    -   如果啟用 MFA，將用戶重定向到新的 `LoginWith2fa.cshtml` 頁面以輸入其 TOTP 碼。
    -   使用 `SignInManager.TwoFactorSignInAsync()` 完成登入。

5.  **實施帳戶恢復:**
    -   為用戶創建一個流程，以便在他們無法訪問其 MFA 設備時使用其恢復碼。

### Agent 驗證 Phase X:

-   **Action:** 暫停執行。
-   **Question:** 「Phase X (多因素認證) 已完成。用戶現在可以註冊基於 TOTP 的 MFA，使用第二個因素登入，並恢復其帳戶。**還有其他任務嗎？**」

---

## 3. 未來增強功能 (通用)

### 3.1. 高級 Claims 轉換

**目標:** 支援超越簡單屬性映射的複雜 Claims 值轉換。

**描述:** 實施一個靈活的 Claims 轉換引擎，允許管理員為 Claims 值定義自定義邏輯。這超越了 Phase 3.9A 中的基本屬性映射。

**用例:**
-   **計算 Claims:** 組合多個屬性（例如 `full_name` = `FirstName + " " + LastName`）
-   **條件 Claims:** 僅在滿足條件時包含 Claims（例如 `is_premium` = true 如果 `SubscriptionLevel == "Premium"`）
-   **格式轉換:** 轉換值（例如 `phone_number` 格式化為國際格式）
-   **外部資料來源:** 從外部 API 或資料庫獲取額外 Claims
-   **組/角色映射:** 將內部角色映射到外部 Claims 值（例如 `Admin` → `company_admin`）

**實施考量:**
-   創建 `ClaimTransformation` 實體，帶有轉換規則
-   支援轉換類型：
    -   **JavaScript 表達式:** 評估 JavaScript 以實現複雜邏輯
    -   **模板字串:** 使用佔位符，例如 `{FirstName} {LastName}`
    -   **查找表:** 從字典映射值（例如部門代碼 → 名稱）
    -   **外部 API 呼叫:** 從 REST 端點獲取資料
-   用於定義轉換的 UI，帶有測試/預覽功能
-   快取外部 API 結果以避免性能影響
-   安全性: 沙盒 JavaScript 執行以防止程式碼注入

**範例轉換:**
```csharp
// See docs/examples/features_claims_transformation_examples.cs.example
```

### 3.2. 動態客戶端註冊 (DCR)

**目標:** 允許客戶端透過標準 OAuth 2.0 動態客戶端註冊協議 (RFC 7591) 自行註冊。

**描述:** 實施 OAuth 2.0 動態客戶端註冊端點，以實現自動化客戶端註冊，無需手動管理員干預。這對於需要以程式設計方式創建自己的 OIDC 客戶端的 SaaS 平台非常有用。

**實施考量:**
-   創建端點: `POST /connect/register`
-   支援標準 DCR 元資料欄位 (redirect_uris, grant_types, response_types 等)
-   認證選項:
    -   **開放註冊:** 允許任何客戶端註冊（帶有速率限制）
    -   **基於令牌:** 註冊需要初始訪問令牌
    -   **白名單網域:** 僅允許來自批准網域的註冊
-   為自動註冊的客戶端分配預設權限
-   管理員審查/批准工作流程選項
-   支援客戶端元資料管理端點 (RFC 7592)
-   速率限制和濫用預防
-   自動清理未使用的客戶端

**參考:** https://datatracker.ietf.org/doc/html/rfc7591

### 3.3. Session 管理與單點登出 (SLO)

**目標:** 提供全面的 Session 管理並支援所有連接客戶端的單點登出。

**描述:** 實施 Session 追蹤和單點登出 (SLO) 功能，以便當用戶從一個應用程式登出時，他們會自動從所有共享相同 IdP Session 的應用程式登出。

**實施考量:**
-   **Session 追蹤:**
    -   在資料庫/Redis 中儲存活動 Session，帶有過期時間
    -   追蹤用戶登入到哪些客戶端
    -   Session 超時和閒置超時配置
    -   管理員 UI 以查看和撤銷用戶 Session
    - 已實作：管理員使用者 Session 檢視支援分頁 (page/pageSize/total/pages) 與基礎 Session 清單
    - 已實作：每筆 Session 現在包含 `CreatedAt` (授權建立時間) 與 `ExpiresAt` (來自最新有效 Token 過期時間，若可推導)
    - 已實作：撤銷單一 Session 與「全部撤銷」邏輯（包含授權與其關聯 Token）
    
-   **單點登出:**
    -   支援 OIDC RP-Initiated Logout (前台通道和後台通道)
    -   實施 `end_session_endpoint`，帶有 `id_token_hint`
    -   為每個用戶 Session 維護登入客戶端列表
    -   透過以下方式向所有客戶端發送登出通知：
        -   **前台通道:** 隱形 iframe 觸發客戶端登出
        -   **後台通道:** 直接 HTTP POST 到客戶端登出端點
    
-   **管理員功能:**
    -   查看每個用戶的活動 Session
    -   強制登出特定 Session 或用戶的所有 Session
    -   Session 活動日誌（登入時間、IP 位址、用戶代理、客戶端應用程式）
    - 已實作：分頁列表 (`/api/users/{id}/sessions`) 回傳結構含 `items`, `page`, `pageSize`, `pages`, `total`
    - 已實作：前端 Vue 組件支援切換每頁筆數與顯示總數
    - 已實作：最佳努力時間戳：`CreatedAt` 為授權建立 UTC 時間；`ExpiresAt` 取關聯有效 Token 中最大過期 UTC 時間
    
-   **用戶功能:**
    -   「在這些設備上登入」視圖
    -   「登出所有其他 Session」按鈕
    -   可疑登入警報

**OIDC 規範參考:** https://openid.net/specs/openid-connect-rpinitiated-1_0.html

### 3.4. Token 內省與撤銷

**目標:** 提供客戶端驗證 Token 和撤銷訪問/刷新 Token 的端點。

**描述:** 實施 OAuth 2.0 Token 內省 (RFC 7662) 和 Token 撤銷 (RFC 7009) 端點，以允許資源伺服器驗證訪問 Token，並允許客戶端在需要時撤銷 Token。

**實施考量:**
-   **內省端點 (`/connect/introspect`):**
    -   驗證訪問 Token 並返回 Token 元資料
    -   返回: `active`、`scope`、`client_id`、`username`、`exp` 等
    -   需要客戶端認證（只有授權的資源伺服器才能內省）
    -   快取內省結果以提高性能
    
-   **撤銷端點 (`/connect/revoke`):**
    -   允許客戶端撤銷訪問 Token 和刷新 Token
    -   撤銷 Token 家族中的所有相關 Token（如果刷新 Token 被撤銷）
    -   Token 撤銷的審計日誌
    
-   **管理員功能:**
    -   查看每個用戶的活動 Token
    -   撤銷特定 Token 或用戶的所有 Token
    -   Token 審計日誌（發布、使用、撤銷）

**參考:**
-   RFC 7662: https://datatracker.ietf.org/doc/html/rfc7662
-   RFC 7009: https://datatracker.ietf.org/doc/html/rfc7009

### 3.5. 設備流程 (RFC 8628)

**目標:** 支援輸入受限設備（智慧電視、物聯網設備、CLI 工具）的認證。

**描述:** 實施 OAuth 2.0 設備授權授予 (設備流程)，允許用戶在沒有網頁瀏覽器或鍵盤的設備上進行認證。

**用戶體驗:**
1.  設備顯示代碼（例如「ABCD-1234」）和 URL（例如「https://idp.example.com/device」）
2.  用戶在手機/電腦上訪問 URL 並輸入代碼
3.  用戶認證並批准設備
4.  設備接收訪問 Token 和刷新 Token

**實施考量:**
-   端點: `/connect/device_authorization`、`/connect/token`（帶有 device_code 授予）
-   設備代碼生成和儲存（短過期時間，例如 10 分鐘）
-   用戶代碼應人性化（簡短、易於輸入）
-   輪詢配置（間隔、減慢回應）
-   管理員 UI 以查看待處理的設備授權
-   安全性: 速率限制、代碼過期、最大重試次數

**參考:** https://datatracker.ietf.org/doc/html/rfc8628

### 3.6. 逐步認證與 ACR 值

**目標:** 支援敏感操作的不同認證級別。

**描述:** 實施認證上下文類別參考 (ACR) 值，以允許客戶端請求特定的認證保證級別。用戶可能需要重新認證或對敏感操作使用更強的認證。

**用例:**
-   銀行應用程式要求重新認證才能進行電匯
-   管理員操作需要 MFA，即使用戶已登入
-   不同的認證級別：僅密碼 (acr=1)、密碼+MFA (acr=2)、生物識別 (acr=3)

**實施考量:**
-   支援標準 ACR 值（例如 `urn:mace:incommon:iap:silver`、`urn:mace:incommon:iap:gold`）
-   定義自定義 ACR 級別：
    -   `level1`: 密碼認證
    -   `level2`: 密碼 + TOTP MFA
    -   `level3`: 密碼 + 硬體金鑰 (WebAuthn)
-   在 Session 中追蹤認證時間戳和級別
-   如果 ACR 不符合或 Session 過舊，則強制重新認證
-   在 ID Token 中包含 `acr` Claims
-   每個客戶端的 ACR 策略管理員配置

**OIDC 規範參考:** https://openid.net/specs/openid-connect-core-1_0.html#acrSemantics

### 3.7. WebAuthn / 無密碼認證

**目標:** 支援 FIDO2/WebAuthn 實現無密碼和防釣魚認證。

**描述:** 實施 WebAuthn 認證，允許用戶使用生物識別（指紋、Face ID）、安全金鑰（YubiKey）或平台認證器登入，而不是密碼。

**實施考量:**
-   為用戶註冊 WebAuthn 憑證
-   在資料庫中儲存憑證公開金鑰
-   實施 WebAuthn 註冊和認證儀式
-   支援每個用戶多個憑證（YubiKey + Face ID）
-   如果 WebAuthn 不可用，則回退到密碼
-   管理員選項，為特定角色強制執行 WebAuthn
-   用於管理已註冊認證器的 UI

**庫:**
-   Fido2NetLib (ASP.NET Core): https://github.com/passwordless-lib/fido2-net-lib

### 3.8. 審計日誌 UI 與高級報告

**目標:** 提供帶有可搜索 UI 和報告功能的全面審計追蹤。

**描述:** 將 Phase 3.10 的審計日誌基礎擴展為一個功能齊全的審計日誌查看器，帶有高級過濾、搜索和報告功能。

**功能:**
-   **審計日誌查看器:**
    -   按用戶、操作類型、實體、日期範圍搜索
    -   按嚴重性過濾（資訊、警告、錯誤）
    -   匯出為 CSV/JSON 以進行合規性報告
    -   鑽取以查看完整的事件詳細資訊
    
-   **報告:**
    -   用戶登入活動報告
    -   失敗登入嘗試報告
    -   客戶端創建/修改報告
    -   權限更改報告
    -   合規性報告（GDPR、SOC2 等）
    
-   **警報:**
    -   針對可疑活動的電子郵件/Webhook 警報
    -   多次失敗登入嘗試
    -   權限提升嘗試
    -   客戶端配置更改

### 3.9. 內容安全策略 (CSP)

**目標:** 透過緩解跨站腳本 (XSS) 和其他程式碼注入攻擊來增強應用程式的安全性。

**描述:** 為 `Web.IdP` 應用程式實施內容安全策略 (CSP) 標頭。此策略將定義一個受信任內容來源（腳本、樣式表、圖像、字體等）的白名單，瀏覽器被允許加載和執行這些來源。任何來自未明確允許的來源的內容都將被瀏覽器阻止。

**實施考量:**
-   從嚴格的策略開始，並在需要時逐步放鬆，最初使用僅報告模式。
-   識別腳本、樣式、圖像和其他資產的所有合法來源，包括來自第三方庫（例如 Cloudflare Turnstile、Vue.js、Vite）的來源。
-   在 `Program.cs` 或透過中介軟體配置 CSP 標頭。

### 3.10. 用戶電子郵件驗證

**目標:** 透過確保註冊的電子郵件地址有效且歸用戶所有來提高帳戶安全性和資料品質。

**描述:** 為新用戶註冊實施電子郵件驗證流程。用戶註冊後，將向其提供的地址發送一封包含唯一驗證連結的電子郵件。用戶將無法完全登入或使用某些功能，直到其電子郵件地址透過點擊此連結成功驗證。

**實施考量:**
-   使用 `EmailConfirmed` 等屬性擴展 `ApplicationUser` 實體。
-   生成用於電子郵件驗證的唯一、有時效的令牌。
-   為驗證連結創建電子郵件模板。
-   實施 Razor Page (`/Account/VerifyEmail`) 來處理驗證連結並確認用戶的電子郵件。
-   修改登入流程以檢查 `EmailConfirmed` 狀態。

---

## 4. 使用者模擬 (User Impersonation)

### 概述
使用者模擬允許獲授權的管理員以任何使用者身份登入，以便重現問題並提供直接支援。此功能受到嚴格的權限控制與審計日誌保護。

### 使用方式
1. **開始模擬**: 在管理後台的**使用者列表**中，點擊目標使用者的「...」選單，選擇「Login As」。
2. **模擬期間**: 畫面最上方會顯示警告橫幅「You are currently impersonating {User}」，提供切換回管理員的按鈕。
3. **停止模擬**: 點擊橫幅中的「Switch Back」按鈕。

### 安全與權限
- **權限要求**: 必須具備 `Permissions.Users.Impersonate`。
- **防範越權**: 管理員無法模擬其他管理員，以防止權限提升。
- **審計追蹤**: 模擬期間的所有操作都會記錄 `Actor` (模擬者) 的身分。

---
**Last Updated**: 2025-12-19
