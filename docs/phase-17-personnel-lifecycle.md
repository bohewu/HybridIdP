# Phase 17: Personnel Lifecycle Management

## 1. 概述 (Overview)

本階段目標是完善 `Person` (人員) 的生命週期管理。目前 `Person` 僅作為靜態資料容器，缺乏「在職狀態」與「生效/失效日期」的概念。
透過本階段實作，系統將支援：
- **狀態管理**：在職、離職、留職停薪等。
- **排程生效**：預約入職 (Future Active) 與 預約離職 (Future Terminate)。
- **自動執行**：時間到字動停用帳號權限。
- **安全性強化**：登入時強制檢查人員狀態。

## 2. 資料庫與實體變更 (Schema Changes)

### 2.1 Person Entity 擴充

需在 `Core.Domain.Entities.Person` 增加以下欄位：

```csharp
public enum PersonStatus
{
    Active = 1,          // 在職/有效
    Pending = 2,         // 待報到 (尚未生效)
    Suspended = 3,       // 停權/留職停薪 (暫時失效)
    Resigned = 4,        // 已離職 (永久失效)
    Terminated = 5       // 免職/開除 (永久失效，黑名單)
}

public class Person
{
    // Existing fields...

    // New Lifecycle Fields
    public PersonStatus Status { get; set; } = PersonStatus.Active;

    /// <summary>
    /// 到職日/生效日。若為 Null 表示建立即生效。
    /// </summary>
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// 離職日/失效日。若為 Null 表示無限期。
    /// 若今天的日期 >= EndDate，則視為失效。
    /// </summary>
    public DateTime? EndDate { get; set; }
    
    // Soft Delete
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}
```

### 2.2 ApplicationUser 確認

`ApplicationUser` 必須保留 `IsActive` (帳號層級控制)。
`Person.Status` 控制的是「這個人」所有帳號的生殺大權。
`ApplicationUser.IsActive` 控制的是「這個帳號」的個別權限。

**權限判斷公式**：
`CanLogin = (Person.Status == Active) AND (Now >= StartDate) AND (Now < EndDate) AND (User.IsActive == true)`

## 3. 核心邏輯 (Core Logic)

### 3.1 登入驗證 (Login Validation)

修改 `Infrastructure.Services.LoginService`。
在驗證密碼成功後，**必須**進行以下檢查：

1.  **使用者檢查**：`ApplicationUser.IsActive` 必須為 `true`。
2.  **人員檢查**：
    -   查詢關聯的 `Person`。
    -   檢查 `Person.IsDeleted` 為 `false`。
    -   檢查 `Person.Status` 為 `Active`。
    -   檢查日期範圍：`DateTime.UtcNow` 必須介於 `StartDate` (若有) 與 `EndDate` (若有) 之間。

若檢查失敗，應回傳 `LoginResult.LockedOut()` 或新的 `LoginResult.Disabled()` (需擴充)。

### 3.2 狀態轉換邏輯

需實作 `IPersonService` 處理狀態變更：

- **Terminate (離職)**：
    - 設定 `Status = Resigned`。
    - 設定 `EndDate = DateTime.UtcNow` (立即生效) 或 指定日期 (預約)。
    - **強制登出**: 立即撤銷 (Revoke) 該人員名下所有 ApplicationUser 的 Refresh Token 與 Access Token (若 Reference Token)。
    - 使用 `IOpenIddictTokenManager` 找出並撤銷所有 Tokens。
    - 這確保即便 Access Token 尚未過期，Refresh Token 也無法再換取新 Token。

## 4. UI/UX 變更 (Admin Panel)

### 4.1 人員列表 (Person List)
- 增加「狀態」欄位顯示 (Badge: Green for Active, Red for Resigned)。
- 支援依狀態篩選。

### 4.2 編輯人員 (Edit Person)
- 新增 "Lifecycle Code" 區塊。
- 編輯 `Status` (Dropdown)。
- 編輯 `StartDate`, `EndDate` (Date Picker)。
- 新增 "Deactivate / Terminate" 按鈕 (快速設定離職)。

## 5. 背景工作 (Background Jobs)

為了支援「預約離職」與「預約報到」，將利用專案現有的 **Quartz.NET** 架構 (目前 OpenIddict 已使用) 實作排程工作。

**PersonLifecycleJob (Quartz Job)**:
- 排程設定: 每天凌晨 (e.g., `0 0 0 * * ?`) 或每小時執行。
- **Auto-Activate**: 找出 `Status == Pending` 且 `StartDate <= Now` 的人員 -> 改為 `Active`。
- **Auto-Terminate**: 找出 `Status == Active` 且 `EndDate <= Now` 的人員 -> 改為 `Resigned`。
- **強制登出**: 對於被 Auto-Terminate 的人員，觸發 Token Revocation。

*註: 原有的 MonitoringBackgroundService 因屬於高頻率即時推播 (Real-time Broadcast)，維持原生的 `BackgroundService` 實作較為合適，不須遷移至 Quartz。*

## 6. 實作階段 (Sub-phases)

### Phase 17.1: Schema & Entity
- [ ] 修改 `Person` Entity。
- [ ] 建立 EF Core Migration。
- [ ] 更新 Database。

### Phase 17.2: Logic & Security (Critical)
- [ ] 實作 `IPersonService` 基本 CRUD (含狀態)。
- [ ] **Hotfix**: 修改 `LoginService` 加上 `IsActive` 與 `Person.Status` 檢查。
- [ ] 撰寫單元測試驗證各種狀態組合的登入結果。

### Phase 17.3: Admin UI
- [ ] 更新 `PersonsController` API。
- [ ] 修改前端 `PersonList.vue` 與 `PersonEditor.vue`。
- [ ] 增加狀態篩選與日期設定 UI。

### Phase 17.4: Automation (Background Job)
- [ ] 實作 `PersonLifecycleBackgroundService`。
- [ ] 設定排程邏輯。
