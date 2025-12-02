# Agent Task: Refactor JitProvisioningService for Person Multi-Account Architecture

## 背景說明

這是一個基於 ASP.NET Core Identity + OpenIddict 的 IdP 專案，目前需要重構 `JitProvisioningService` 以支援 **Person Multi-Account Architecture**。

### 核心架構概念

```
Person (1) ─────┬───→ ApplicationUser (AD Account)
                │         └── AspNetUserLogins (ActiveDirectory)
                │
                ├───→ ApplicationUser (Google Account)
                │         └── AspNetUserLogins (Google)
                │
                └───→ ApplicationUser (Local Password)
```

**關鍵設計原則：**
1. **Person** = 真實身分（物理實體），一個人只有一個 Person
2. **ApplicationUser** = 登入帳號（認證實體），一個人可以有多個 ApplicationUser
3. **預設行為**: 1 ApplicationUser = 1 External Provider（除非手動綁定）
4. **AspNetUserLogins** = 外部認證連結表（Identity 框架內建）

### 當前問題

現有的 `JitProvisioningService` 位於 `Infrastructure/Identity/JitProvisioningService.cs`，存在以下問題：

❌ **只建立 ApplicationUser**，沒有建立 Person  
❌ **沒有建立 AspNetUserLogins**（外部認證連結）  
❌ **只支援 Legacy 系統**，無法處理 AD/Google/其他外部認證  
❌ **重複登入時沒有檢查是否為同一個 Person**（透過 Email 判斷）

## 任務目標

重構 `JitProvisioningService` 及相關測試，實作完整的 JIT Provisioning 流程。

## 實作需求

### 1. 建立新的 DTO: `ExternalAuthResult`

路徑：`Core.Application/DTOs/ExternalAuthResult.cs`

```csharp
namespace Core.Application.DTOs;

/// <summary>
/// 外部認證結果（AD, Google, Facebook 等）
/// </summary>
public class ExternalAuthResult
{
    /// <summary>
    /// 認證提供者名稱（"ActiveDirectory", "Google", "Facebook"）
    /// </summary>
    public string Provider { get; set; } = string.Empty;
    
    /// <summary>
    /// 外部系統的使用者唯一識別碼
    /// 例如：AD username, Google user ID, Facebook user ID
    /// </summary>
    public string ProviderKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Email（主要用於判斷是否為同一個 Person）
    /// </summary>
    public string? Email { get; set; }
    
    /// <summary>
    /// 名
    /// </summary>
    public string? FirstName { get; set; }
    
    /// <summary>
    /// 姓
    /// </summary>
    public string? LastName { get; set; }
    
    /// <summary>
    /// 中間名
    /// </summary>
    public string? MiddleName { get; set; }
    
    /// <summary>
    /// 員工編號（AD 可能提供）
    /// </summary>
    public string? EmployeeId { get; set; }
    
    /// <summary>
    /// 部門
    /// </summary>
    public string? Department { get; set; }
    
    /// <summary>
    /// 職稱
    /// </summary>
    public string? JobTitle { get; set; }
    
    /// <summary>
    /// 電話
    /// </summary>
    public string? PhoneNumber { get; set; }
    
    /// <summary>
    /// 顯示名稱（給 AspNetUserLogins.ProviderDisplayKey 使用）
    /// </summary>
    public string? DisplayName { get; set; }
    
    /// <summary>
    /// 額外的 Claims（選用）
    /// </summary>
    public Dictionary<string, string>? AdditionalClaims { get; set; }
}
```

### 2. 更新 Interface: `IJitProvisioningService`

路徑：`Core.Application/IJitProvisioningService.cs`

```csharp
using Core.Application.DTOs;
using Core.Domain;

namespace Core.Application;

public interface IJitProvisioningService
{
    /// <summary>
    /// 為外部認證使用者建立或更新 Person 和 ApplicationUser
    /// </summary>
    /// <param name="externalAuth">外部認證結果</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>已建立或更新的 ApplicationUser</returns>
    Task<ApplicationUser> ProvisionExternalUserAsync(
        ExternalAuthResult externalAuth,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Legacy 系統的 JIT Provisioning（保留向下相容）
    /// </summary>
    [Obsolete("Use ProvisionExternalUserAsync instead")]
    Task<ApplicationUser> ProvisionUserAsync(
        LegacyUserDto dto,
        CancellationToken cancellationToken = default);
}
```

### 3. 重構實作: `JitProvisioningService`

路徑：`Infrastructure/Identity/JitProvisioningService.cs`

**核心邏輯流程：**

```
Step 1: 檢查是否已有此外部登入（AspNetUserLogins）
├─ 已存在 → 更新 ApplicationUser 資訊，返回
└─ 不存在 → 繼續 Step 2

Step 2: 優先透過身分文件欄位（NationalId / PassportNumber / ResidentCertificateNumber）檢查是否有現存的 Person；若沒有身分文件或比對失敗，再以 Email 作為備援檢查
├─ 身分文件命中 → 使用該 Person，繼續 Step 3
├─ 身分文件未命中但 Email 有值且 Email 命中 → 使用該 Person，繼續 Step 3
└─ 都未命中 → 建立新 Person，繼續 Step 3

Step 3: 建立新的 ApplicationUser（連結到 Person）

Step 4: 建立 AspNetUserLogins（連結外部認證）

Step 5: 儲存所有變更
```

**重要實作細節：**

1. **需要注入 `ApplicationDbContext`** 來存取 `Persons` 表
2. **使用 `UserManager.FindByLoginAsync()`** 檢查外部登入
3. **使用 `UserManager.AddLoginAsync()`** 建立外部登入連結
4. **Person 唯一性判斷邏輯**: 在匹配 Person 時，應遵循以下優先順序：

    1. 使用身分文件（NationalId / PassportNumber / ResidentCertificateNumber）作為首要匹配條件 — 只要其中任一欄位命中，即視為同一個 Person。
    2. 若無身分文件或匹配失敗，再以 Email 作為備援匹配條件（Email 相同則視為同一個 Person）。

    註：若 Email 缺失或兩者皆未命中，將建立新的 Person。

    另外，username 的預設行為仍然可以採用 Email；若 Email 不存在，則使用 `{Provider}_{ProviderKey}` 作為 username。
5. **Transaction 處理**: Person + ApplicationUser + AspNetUserLogins 應在同一個 transaction

**程式碼骨架：**

```csharp
public class JitProvisioningService : IJitProvisioningService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;

    public JitProvisioningService(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context)
    {
        _userManager = userManager;
        _context = context;
    }

    public async Task<ApplicationUser> ProvisionExternalUserAsync(
        ExternalAuthResult externalAuth,
        CancellationToken cancellationToken = default)
    {
        // Validation
        if (externalAuth == null)
            throw new ArgumentNullException(nameof(externalAuth));
        if (string.IsNullOrWhiteSpace(externalAuth.Provider))
            throw new ArgumentException("Provider is required", nameof(externalAuth));
        if (string.IsNullOrWhiteSpace(externalAuth.ProviderKey))
            throw new ArgumentException("ProviderKey is required", nameof(externalAuth));

        // Step 1: 檢查是否已有此外部登入
        var existingUser = await _userManager.FindByLoginAsync(
            externalAuth.Provider,
            externalAuth.ProviderKey
        );

        if (existingUser != null)
        {
            // 已存在，更新資訊
            existingUser.Email = externalAuth.Email ?? existingUser.Email;
            existingUser.FirstName = externalAuth.FirstName ?? existingUser.FirstName;
            existingUser.LastName = externalAuth.LastName ?? existingUser.LastName;
            existingUser.PhoneNumber = externalAuth.PhoneNumber ?? existingUser.PhoneNumber;
            existingUser.Department = externalAuth.Department ?? existingUser.Department;
            existingUser.JobTitle = externalAuth.JobTitle ?? existingUser.JobTitle;
            
            await _userManager.UpdateAsync(existingUser);
            return existingUser;
        }

        // Step 2: 透過 Email 檢查是否有現存的 Person
        Person? person = null;
        if (!string.IsNullOrWhiteSpace(externalAuth.Email))
        {
            person = await _context.Persons
                .FirstOrDefaultAsync(p => p.Email == externalAuth.Email, cancellationToken);
        }

        // Step 3: 如果沒有 Person，建立新的
        if (person == null)
        {
            person = new Person
            {
                Id = Guid.NewGuid(),
                Email = externalAuth.Email,
                FirstName = externalAuth.FirstName,
                LastName = externalAuth.LastName,
                MiddleName = externalAuth.MiddleName,
                EmployeeId = externalAuth.EmployeeId,
                Department = externalAuth.Department,
                JobTitle = externalAuth.JobTitle,
                PhoneNumber = externalAuth.PhoneNumber,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = null // System provisioned
            };
            await _context.Persons.AddAsync(person, cancellationToken);
        }

        // Step 4: 建立新的 ApplicationUser（連結到 Person）
        var username = externalAuth.Email ?? 
                      $"{externalAuth.Provider}_{externalAuth.ProviderKey}";
        
        var newUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = username,
            Email = externalAuth.Email,
            EmailConfirmed = !string.IsNullOrWhiteSpace(externalAuth.Email), // 外部認證視為已驗證
            PersonId = person.Id,
            FirstName = externalAuth.FirstName,
            LastName = externalAuth.LastName,
            MiddleName = externalAuth.MiddleName,
            Department = externalAuth.Department,
            JobTitle = externalAuth.JobTitle,
            EmployeeId = externalAuth.EmployeeId,
            PhoneNumber = externalAuth.PhoneNumber,
            IsActive = true
        };

        var createResult = await _userManager.CreateAsync(newUser);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to create user: {string.Join(", ", createResult.Errors.Select(e => e.Description))}"
            );
        }

        // Step 5: 建立外部登入連結
        var displayName = externalAuth.DisplayName ?? 
                         externalAuth.Email ?? 
                         externalAuth.ProviderKey;
        
        var addLoginResult = await _userManager.AddLoginAsync(
            newUser,
            new UserLoginInfo(
                externalAuth.Provider,
                externalAuth.ProviderKey,
                displayName
            )
        );

        if (!addLoginResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to add external login: {string.Join(", ", addLoginResult.Errors.Select(e => e.Description))}"
            );
        }

        // Step 6: 儲存 Person 的變更
        await _context.SaveChangesAsync(cancellationToken);

        return newUser;
    }

    [Obsolete("Use ProvisionExternalUserAsync instead")]
    public async Task<ApplicationUser> ProvisionUserAsync(
        LegacyUserDto dto,
        CancellationToken cancellationToken = default)
    {
        // 轉換成 ExternalAuthResult 格式
        var externalAuth = new ExternalAuthResult
        {
            Provider = "Legacy",
            ProviderKey = dto.ExternalId ?? dto.Email ?? dto.IdCardNumber ?? Guid.NewGuid().ToString(),
            Email = dto.Email,
            FirstName = dto.FullName, // Legacy 只有 FullName
            PhoneNumber = dto.Phone,
            Department = dto.Department
        };

        return await ProvisionExternalUserAsync(externalAuth, cancellationToken);
    }
}
```

### 4. 更新 Unit Tests

路徑：`Tests.Application.UnitTests/JitProvisioningServiceTests.cs`

**需要測試的場景：**

1. ✅ **首次外部登入** - 建立 Person + ApplicationUser + AspNetUserLogins
2. ✅ **重複登入** - 更新 ApplicationUser 資訊
3. ✅ **同 Email 不同 Provider** - 使用相同 Person，建立新 ApplicationUser
4. ✅ **Email 為 null** - 仍能正常建立（使用 Provider_ProviderKey 作為 username）
5. ✅ **Legacy 向下相容** - 舊的 ProvisionUserAsync 仍能運作

**測試程式碼骨架：**

```csharp
public class JitProvisioningServiceTests
{
    private Mock<UserManager<ApplicationUser>> _userManagerMock;
    private Mock<ApplicationDbContext> _contextMock;
    private Mock<DbSet<Person>> _personsDbSetMock;
    private JitProvisioningService _service;

    public JitProvisioningServiceTests()
    {
        _userManagerMock = CreateUserManagerMock();
        _contextMock = CreateDbContextMock();
        _personsDbSetMock = CreatePersonsDbSetMock();
        
        _contextMock.Setup(c => c.Persons).Returns(_personsDbSetMock.Object);
        
        _service = new JitProvisioningService(
            _userManagerMock.Object,
            _contextMock.Object
        );
    }

    [Fact]
    public async Task ProvisionExternalUser_FirstTimeLogin_ShouldCreatePersonAndUser()
    {
        // Arrange
        var externalAuth = new ExternalAuthResult
        {
            Provider = "ActiveDirectory",
            ProviderKey = "john.doe@ad",
            Email = "john.doe@company.com",
            FirstName = "John",
            LastName = "Doe",
            EmployeeId = "EMP001"
        };

        _userManagerMock.Setup(um => um.FindByLoginAsync(
            It.IsAny<string>(), 
            It.IsAny<string>()
        )).ReturnsAsync((ApplicationUser?)null);

        // Person 不存在
        _personsDbSetMock.Setup(/* FirstOrDefaultAsync returns null */);

        _userManagerMock.Setup(um => um.CreateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(um => um.AddLoginAsync(
            It.IsAny<ApplicationUser>(),
            It.IsAny<UserLoginInfo>()
        )).ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _service.ProvisionExternalUserAsync(externalAuth);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("john.doe@company.com", result.Email);
        _contextMock.Verify(c => c.Persons.AddAsync(
            It.IsAny<Person>(),
            It.IsAny<CancellationToken>()
        ), Times.Once);
        _userManagerMock.Verify(um => um.CreateAsync(It.IsAny<ApplicationUser>()), Times.Once);
        _userManagerMock.Verify(um => um.AddLoginAsync(
            It.IsAny<ApplicationUser>(),
            It.IsAny<UserLoginInfo>()
        ), Times.Once);
    }

    [Fact]
    public async Task ProvisionExternalUser_ExistingLogin_ShouldUpdateUser()
    {
        // Arrange
        var existingUser = new ApplicationUser 
        { 
            Id = Guid.NewGuid(),
            Email = "john.doe@company.com" 
        };

        var externalAuth = new ExternalAuthResult
        {
            Provider = "ActiveDirectory",
            ProviderKey = "john.doe@ad",
            Email = "john.doe@newdomain.com",
            FirstName = "John",
            LastName = "Doe"
        };

        _userManagerMock.Setup(um => um.FindByLoginAsync(
            "ActiveDirectory",
            "john.doe@ad"
        )).ReturnsAsync(existingUser);

        _userManagerMock.Setup(um => um.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _service.ProvisionExternalUserAsync(externalAuth);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(existingUser.Id, result.Id);
        Assert.Equal("john.doe@newdomain.com", result.Email);
        _userManagerMock.Verify(um => um.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Once);
        _userManagerMock.Verify(um => um.CreateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task ProvisionExternalUser_SameEmailDifferentProvider_ShouldUseSamePerson()
    {
        // Arrange
        var existingPerson = new Person
        {
            Id = Guid.NewGuid(),
            Email = "john.doe@company.com",
            FirstName = "John",
            LastName = "Doe"
        };

        var externalAuth = new ExternalAuthResult
        {
            Provider = "Google",
            ProviderKey = "google-id-123",
            Email = "john.doe@company.com",
            FirstName = "John",
            LastName = "Doe"
        };

        _userManagerMock.Setup(um => um.FindByLoginAsync(
            It.IsAny<string>(),
            It.IsAny<string>()
        )).ReturnsAsync((ApplicationUser?)null);

        // Person 已存在（透過 Email）
        _personsDbSetMock.Setup(/* FirstOrDefaultAsync returns existingPerson */);

        _userManagerMock.Setup(um => um.CreateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(um => um.AddLoginAsync(
            It.IsAny<ApplicationUser>(),
            It.IsAny<UserLoginInfo>()
        )).ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _service.ProvisionExternalUserAsync(externalAuth);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(existingPerson.Id, result.PersonId);
        _contextMock.Verify(c => c.Persons.AddAsync(
            It.IsAny<Person>(),
            It.IsAny<CancellationToken>()
        ), Times.Never); // 不應建立新 Person
    }

    [Fact]
    public async Task ProvisionExternalUser_NoEmail_ShouldUseProviderKeyAsUsername()
    {
        // Arrange
        var externalAuth = new ExternalAuthResult
        {
            Provider = "CustomProvider",
            ProviderKey = "custom-user-123",
            Email = null, // 沒有 Email
            FirstName = "Anonymous",
            LastName = "User"
        };

        _userManagerMock.Setup(um => um.FindByLoginAsync(
            It.IsAny<string>(),
            It.IsAny<string>()
        )).ReturnsAsync((ApplicationUser?)null);

        _userManagerMock.Setup(um => um.CreateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(um => um.AddLoginAsync(
            It.IsAny<ApplicationUser>(),
            It.IsAny<UserLoginInfo>()
        )).ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _service.ProvisionExternalUserAsync(externalAuth);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("CustomProvider_custom-user-123", result.UserName);
    }

    [Fact]
    public async Task ProvisionUserAsync_LegacyMethod_ShouldStillWork()
    {
        // Arrange
        var legacyDto = new LegacyUserDto
        {
            IsAuthenticated = true,
            Email = "legacy@example.com",
            FullName = "Legacy User",
            ExternalId = "legacy-123"
        };

        _userManagerMock.Setup(um => um.FindByLoginAsync(
            It.IsAny<string>(),
            It.IsAny<string>()
        )).ReturnsAsync((ApplicationUser?)null);

        _userManagerMock.Setup(um => um.CreateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(um => um.AddLoginAsync(
            It.IsAny<ApplicationUser>(),
            It.IsAny<UserLoginInfo>()
        )).ReturnsAsync(IdentityResult.Success);

        // Act
#pragma warning disable CS0618 // Type or member is obsolete
        var result = await _service.ProvisionUserAsync(legacyDto);
#pragma warning restore CS0618

        // Assert
        Assert.NotNull(result);
        Assert.Equal("legacy@example.com", result.Email);
    }

    // Helper methods
    private static Mock<UserManager<ApplicationUser>> CreateUserManagerMock()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var options = new Mock<IOptions<IdentityOptions>>();
        var hasher = new Mock<IPasswordHasher<ApplicationUser>>();
        var userValidators = new List<IUserValidator<ApplicationUser>>();
        var passwordValidators = new List<IPasswordValidator<ApplicationUser>>();
        var normalizer = new Mock<ILookupNormalizer>();
        var errors = new IdentityErrorDescriber();
        var services = new Mock<IServiceProvider>();
        var logger = new Mock<ILogger<UserManager<ApplicationUser>>>();
        
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, options.Object, hasher.Object, 
            userValidators, passwordValidators, normalizer.Object, 
            errors, services.Object, logger.Object
        );
    }

    private static Mock<ApplicationDbContext> CreateDbContextMock()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        
        return new Mock<ApplicationDbContext>(options);
    }

    private static Mock<DbSet<Person>> CreatePersonsDbSetMock()
    {
        var persons = new List<Person>().AsQueryable();
        var mockSet = new Mock<DbSet<Person>>();
        
        mockSet.As<IQueryable<Person>>().Setup(m => m.Provider).Returns(persons.Provider);
        mockSet.As<IQueryable<Person>>().Setup(m => m.Expression).Returns(persons.Expression);
        mockSet.As<IQueryable<Person>>().Setup(m => m.ElementType).Returns(persons.ElementType);
        mockSet.As<IQueryable<Person>>().Setup(m => m.GetEnumerator()).Returns(persons.GetEnumerator());
        
        return mockSet;
    }
}
```

## 驗證清單

完成重構後，請確認：

- [ ] `ExternalAuthResult` DTO 已建立
- [ ] `IJitProvisioningService` 介面已更新
- [ ] `JitProvisioningService` 實作完成並通過所有測試
- [ ] 至少 5 個 Unit Test 全部通過
- [ ] 舊的 `ProvisionUserAsync` 保留向下相容（標記 Obsolete）
- [ ] `Program.cs` 的 DI 註冊不需要修改（因為介面名稱相同）

## 相關檔案路徑

```
Core.Application/
├── DTOs/
│   ├── ExternalAuthResult.cs         ← 新增
│   └── LegacyUserDto.cs              ← 保持不變
└── IJitProvisioningService.cs        ← 更新

Infrastructure/
└── Identity/
    └── JitProvisioningService.cs     ← 重構

Tests.Application.UnitTests/
└── JitProvisioningServiceTests.cs    ← 更新測試

Core.Domain/
├── ApplicationUser.cs                ← 已有 PersonId 欄位，不需修改
└── Entities/
    └── Person.cs                     ← 已有 Accounts 導航屬性，不需修改
```

## 參考文件

- `docs/PERSON_MULTI_ACCOUNT_ARCHITECTURE.md` - 完整架構說明
- `docs/AUTHENTICATION_INTEGRATION.md` - 外部認證整合範例

## 注意事項

1. **Transaction 處理**: Person 和 ApplicationUser 的建立應該在同一個 transaction 中
2. **Email 判斷邏輯**: 如果 Email 為空，無法判斷是否為同一個 Person，會建立新的 Person
3. **UserName 生成**: 優先使用 Email，如果沒有 Email 則使用 `{Provider}_{ProviderKey}`
4. **EmailConfirmed**: 外部認證來的帳號，EmailConfirmed 應設為 true
5. **CreatedBy**: System provisioned 的 Person，CreatedBy 可以是 null

## 預期結果

重構完成後，可以支援以下場景：

```csharp
// Scenario 1: AD 首次登入
var adAuth = new ExternalAuthResult
{
    Provider = "ActiveDirectory",
    ProviderKey = "john.doe@ad",
    Email = "john.doe@company.com",
    FirstName = "John",
    LastName = "Doe",
    EmployeeId = "EMP001"
};
var user1 = await jitService.ProvisionExternalUserAsync(adAuth);
// → 建立 Person + ApplicationUser + AspNetUserLogins

// Scenario 2: 同一個人用 Google 登入
var googleAuth = new ExternalAuthResult
{
    Provider = "Google",
    ProviderKey = "google-id-xyz",
    Email = "john.doe@company.com", // 同樣的 Email
    FirstName = "John",
    LastName = "Doe"
};
var user2 = await jitService.ProvisionExternalUserAsync(googleAuth);
// → 使用相同的 Person，建立新的 ApplicationUser + AspNetUserLogins
// → user1.PersonId == user2.PersonId (同一個人)
```

---

**Good luck! 如果有任何問題，請參考 `PERSON_MULTI_ACCOUNT_ARCHITECTURE.md` 文件。**
