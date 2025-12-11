# Phase 18: Personnel Lifecycle Management

## 1. 概述 (Overview)

**目標**: 完善人員 (Person) 的生命週期管理，包含到職、離職、復職等狀態變更，並確保系統存取權限與人員狀態同步。

**現狀分析**:
- 目前 `Person` 實體缺乏狀態 (Status) 與生效日期 (StartDate/EndDate)。
- 登入邏輯 (`LoginService`) 僅檢查 `ApplicationUser.IsLockedOut`，未檢查 `ApplicationUser.IsActive` 或 `Person` 狀態。
- 缺乏自動化排程來處理預約生效或失效的人員。

## 2. 資料模型變更 (Schema Changes)

### 2.1 Person Entity Update

在 `Core.Domain.Entities.Person` 新增以下欄位：

```csharp
public enum PersonStatus
{
    Pending = 0,    // 預約 / 尚未生效
    Active = 1,     // 在職 / 有效
    Suspended = 2,  // 停權 / 留職停薪
    Resigned = 3,   // 離職 / 退休
    Terminated = 4  //以此類推
}

public class Person
{
    // ... Existing
    
    public PersonStatus Status { get; set; } = PersonStatus.Active;
    
    // 生效日 (Inclusive)
    public DateTime? StartDate { get; set; } 
    
    // 失效日 (Inclusive: EndDate 當天結束後失效，或視業務定義，通常 EndDate 當天仍有效，隔天失效)
    // 建議：EndDate 為「最後工作日」。
    public DateTime? EndDate { get; set; }

    // Soft Delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}
```

### 2.2 ApplicationUser Entity (Review)

`ApplicationUser` 已有 `IsActive`，需確保與 `Person.Status` 連動。
建議策略：
- **Master Switch**: `Person.Status` 是主控開關。若 Person 失效，旗下所有 User 皆失效。
- **Fine-grained**: `ApplicationUser.IsActive` 可個別停用特定帳號 (e.g. 停用 AD 帳號但保留 Local Admin)。

## 3. 核心邏輯與安全性 (Core Logic & Security)

### 3.1 Login Validation (`LoginService`)

在 `AuthenticateLocalUserAsync` 與 `AuthenticateExternalUserAsync` 增加檢查：

1. **Check Person**:
   - 若 `user.PersonId` != null:
     - 檢查 `Person.IsDeleted` -> Block
     - 檢查 `Person.Status` != Active -> Block
     - 檢查 `Person.StartDate` (若有) > Now -> Block
     - 檢查 `Person.EndDate` (若有) < Now -> Block (視時區定義)

2. **Check User**:
   - 檢查 `ApplicationUser.IsActive` -> Block

### 3.2 狀態連動 (State Transition)

- **Onboard (報到)**:
    - 設定 `Status = Active`。
    - 設定 `StartDate = DateTime.UtcNow` (或指定日期)。
    - 系統自動啟用其下 `ApplicationUser.IsActive = true` (Optional, 視需求而定)。
    
- **Terminate (離職)**:
    - 設定 `Status = Resigned`。
    - 設定 `EndDate = DateTime.UtcNow` (立即生效) 或 指定日期 (預約)。
    - **強制登出**: 立即撤銷 (Revoke) 該人員名下所有 ApplicationUser 的 Refresh Token 與 Access Token (若 Reference Token)。
    - 使用 `IOpenIddictTokenManager` 找出並撤銷所有 Tokens。
    - 這確保即便 Access Token 尚未過期，Refresh Token 也無法再換取新 Token。

## 4. UI/UX 變更 (Admin Panel)

### 4.1 Person List
- 新增 `Status` 欄位顯示 (Badge: Green/Red/Gray)。
- 支援依 Status 篩選。

### 4.2 Person Editor
- 新增 Status Dropdown。
- 新增 StartDate / EndDate DatePicker。
- 驗證：EndDate 必須 >= StartDate。

## 5. 背景工作 (Background Jobs)

為了支援「預約離職」與「預約報到」，將利用專案現有的 **Quartz.NET** 架構 (目前 OpenIddict 已使用) 實作排程工作。

**PersonLifecycleJob (Quartz Job)**:
- 排程設定: 每天凌晨 (e.g., `0 0 0 * * ?`) 或每小時執行。
- **Auto-Activate**: 找出 `Status == Pending` 且 `StartDate <= Now` 的人員 -> 改為 `Active`。
- **Auto-Terminate**: 找出 `Status == Active` 且 `EndDate <= Now` 的人員 -> 改為 `Resigned`。
- **強制登出**: 對於被 Auto-Terminate 的人員，觸發 Token Revocation。

*註: 原有的 MonitoringBackgroundService 因屬於高頻率即時推播 (Real-time Broadcast)，維持原生的 `BackgroundService` 實作較為合適，不須遷移至 Quartz。*

## 6. 實作階段 (Sub-phases)

### Phase 18.1: Schema & Entity
- Modify `Person` entity.
- EF Core Migration.
- Update `ApplicationDbContext`.

### Phase 18.2: Logic & Security (Critical)
- Implement `IPersonService` lifecycle methods (Terminate, Activate).
- Update `LoginService` validation logic.
- Unit Tests for Login restrictions.

### Phase 18.3: Admin UI
- Update `PersonsController` API.
- Update `PersonList.vue` & `PersonEditor.vue`.

### Phase 18.4: Automation
- Implement `PersonLifecycleJob`.
- Register Hosted Service.`。
- 設定排程邏輯。
