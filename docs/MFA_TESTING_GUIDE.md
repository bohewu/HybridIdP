# MFA 開發端測試指南 (MFA Development Testing Guide)

本文件說明在開發環境中如何測試各種 MFA (多重因素身份驗證) 方式，包含 TOTP、Email OTP 以及 Passkey。

## 1. TOTP (驗證碼產生器)

### 手動測試步驟
1. 登入系統，進入「個人設定 (Profile)」 -> 「MFA 設定 (MFA Settings)」。
2. 在 TOTP 區塊點擊「顯示二維碼 (Setup Authenticator)」。
3. 使用手機上的 App (例如 **Microsoft Authenticator** 或 **Google Authenticator**) 掃描二維碼。
4. 輸入 App 產生的 6 位數代碼進行驗證並啟用。

### 開發小技巧 (無需手機)
如果你在開發時不想頻繁使用手機，可以：
- 複製 QR Code 下方顯示的 **Shared Key (密鑰)**。
- 使用網頁版工具 (如 [totp.app](https://totp.app/))，點擊「Add Token」並貼上密鑰，即可在瀏覽器直接取得代碼。

---

## 2. Email OTP (信箱驗證碼)

### 手動測試步驟
1. 在使用者設定頁面點擊「啟用信箱驗證」。
2. 系統會提示發送代碼，點擊「發送驗證碼」。
3. 檢查你的開發用電子信箱（需已設定 SMTP 伺服器）。

### 開發小技巧 (使用 Mailpit)
推薦在本地開發環境安裝 [Mailpit](https://github.com/axllent/mailpit)。它是系統預設推薦的輕量級 SMTP 測試伺服器。
- **優點**：無需真實收信，所有系統發出的郵件都會攔截在本地網頁介面。
- **查看代碼**：開啟 Mailpit 介面 (通常是 `http://localhost:8025`)，即可看到包含 6 位數代碼的驗證郵件。

---

## 3. Passkey (WebAuthn / 實體金鑰)

### 手動測試步驟
1. 點擊「註冊新 Passkey」。
2. 根據系統提示，可使用電腦內建的生物辨識 (Windows Hello, Touch ID) 或是實體金鑰 (如 Yubikey)。

### 開發小技巧 (使用 Chrome 虛擬器)
如果你手邊沒有指紋辨識或實體金鑰，可以使用瀏覽器內建的虛擬測試環境：
1. 開啟 **Chrome/Edge 開發者工具 (F12)**。
2. 點擊右上角「三個點 (...)」 -> 「More tools」 -> **「WebAuthn」**。
3. 在出現的面板中，勾選 **「Enable virtual authenticator environment」**。
4. 點擊 「Add」 建立一個虛擬金鑰。
5. 現在返回網頁點擊註冊，瀏覽器會自動彈出視窗並使用此虛擬金鑰完成流程。

---

## 4. 管理者安全性策略測試 (Security Policy)

測試身為管理者如何控制全域 MFA 開關：
1. 以 `admin` 帳號登入系統。
2. 進入 **安全性設定 (Security Settings)**。
3. 嘗試關閉 「Enable TOTP MFA」 或 「Enable Passkey」。
4. 使用一般人帳號登入，確認：
    - **UI 反應**：對應的區塊是否已從 Profile 頁面消失。
    - **API 保護**：若嘗試透過 Postman 等工具直接呼叫 `/api/account/mfa/setup`，後端應回傳 `403 Forbidden`。
    - **全部停用**：若全部關閉，Profile 頁面應顯示「管理者已停用所有 MFA 方式」的提示。

---

## 5. 自動化測試指令

在提交代碼前，請務必運行以下指令確保邏輯正確：

### 後端 API 測試 (System Tests)
```bash
# 測試 MFA 與 Email OTP 流程
dotnet test --filter "FullyQualifiedName~MfaApiTests"

# 測試 Passkey 流程
dotnet test --filter "FullyQualifiedName~PasskeyApiTests"
```

### 前端 UI 測試 (Vitest)
進入 `Web.IdP/ClientApp` 目錄：
```bash
npm run test -- MfaSettings.test.js
```
