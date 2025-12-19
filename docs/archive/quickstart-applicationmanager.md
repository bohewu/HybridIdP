# Application Manager — 快速開始

這份文件提供 Application Manager (應用管理) 的快速上手步驟，適合 ApplicationManager 角色的使用者。

## 1. 登入

- 前往 IdP 登入頁面並使用 ApplicationManager 帳號登入。

## 2. 檢視你的 Clients 與 Scopes

- 在 Dashboard 上可以看到「My Clients」與「My Scopes」摘要，點選對應按鈕可進入管理頁面。

## 3. 建立新的 Client

1. 點選 `Manage Clients` → `Create Client`。
2. 填寫 Client 名稱、Redirect URI，並設定需要的 scopes。
3. 儲存後，系統會顯示 ClientId 與（如有）Client Secret，請妥善保管。

## 4. 建立 Scope

1. 點選 `Manage Scopes` → `Create Scope`。
2. 填寫 scope 名稱與描述，選擇是否為可選 scope。

## 5. 整合範例 (簡略)

- 在應用程式端 (client) 設定 OAuth2/OIDC，使用產生的 ClientId，Redirect URI 與需要的 scopes（例如 `openid profile clients.read`）。

## 6. 常見問題與排除

- 若看到 Access Denied：請確認你已登入的帳號擁有 `clients.read` 或 `scopes.read` 權限。
- 若看不到文件：請聯絡系統管理員或使用頁面右下的 Support 連結。

---

如需更詳細的操作說明，請參考系統文件或聯絡團隊。
