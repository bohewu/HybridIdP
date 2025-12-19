# Person Multi-Account Architecture

## 核心概念

### Person vs ApplicationUser

```
Person (1) ─────┬───→ ApplicationUser (AD Account)
                │         └── AspNetUserLogins (ActiveDirectory)
                │
                ├───→ ApplicationUser (Google Account)
                │         └── AspNetUserLogins (Google)
                │
                └───→ ApplicationUser (Local Password)
                          └── PasswordHash (in AspNetUsers table)
```

### 設計原則

1. **Person = 真實身分（Physical Identity）**
   - 代表一個真實的人
   - 儲存身分證件資訊：NationalId, PassportNumber, EmployeeId
   - 儲存基本 Profile：FirstName, LastName, Department, JobTitle
   - **一個人只有一個 Person 記錄**

2. **ApplicationUser = 登入帳號（Authentication Account）**
   - 代表一個登入方式
   - 一個 Person 可以有多個 ApplicationUser
   - 每個 ApplicationUser 有獨立的 Email/UserName
   - 透過 PersonId 連結到 Person

3. **AspNetUserLogins = 外部認證連結**
   - 連結 ApplicationUser 到外部認證提供者
   - LoginProvider: "ActiveDirectory", "Google", "Facebook"
   - 一個 ApplicationUser 可以有多個外部連結

## 實作場景

### Scenario 1: AD 使用者首次登入

```csharp
// Step 1: AD 驗證成功
var adAuthResult = await AuthenticateWithActiveDirectory(username, password);

// Step 2: 檢查是否已有此 AD 帳號
var externalLoginInfo = new ExternalLoginInfo(
    loginProvider: "ActiveDirectory",
    providerKey: adAuthResult.AdUsername,
    displayName: adAuthResult.DisplayName
);

var existingUser = await _signInManager.GetExternalLoginAsync(externalLoginInfo);
if (existingUser == null)
{
    // Step 3: 首次登入 - 建立 Person + ApplicationUser + AspNetUserLogins
    var person = new Person
    {
        Id = Guid.NewGuid(),
        FirstName = adAuthResult.FirstName,
        LastName = adAuthResult.LastName,
        EmployeeId = adAuthResult.EmployeeId,
        Department = adAuthResult.Department,
        Email = adAuthResult.Email,
        CreatedAt = DateTime.UtcNow
    };
    await _context.Persons.AddAsync(person);

    var applicationUser = new ApplicationUser
    {
        Id = Guid.NewGuid(),
        UserName = adAuthResult.Email, // 使用 AD email 作為 username
        Email = adAuthResult.Email,
        EmailConfirmed = true, // AD 帳號視為已驗證
        PersonId = person.Id, // 連結到 Person
        IsActive = true
    };
    
    var createResult = await _userManager.CreateAsync(applicationUser);
    if (createResult.Succeeded)
    {
        // 建立外部登入連結
        var addLoginResult = await _userManager.AddLoginAsync(
            applicationUser,
            new UserLoginInfo(
                loginProvider: "ActiveDirectory",
                providerKey: adAuthResult.AdUsername,
                displayName: adAuthResult.DisplayName
            )
        );
    }
}
```

### Scenario 2: 同一個 Person 用 Google 登入

```csharp
// Step 1: Google 驗證成功（透過 ASP.NET Core Identity 的 OAuth middleware）
var googleInfo = await _signInManager.GetExternalLoginInfoAsync();

// Step 2: 檢查是否已有此 Google 帳號
var existingUser = await _userManager.FindByLoginAsync(
    googleInfo.LoginProvider,
    googleInfo.ProviderKey
);

if (existingUser == null)
{
    // Step 3: 判斷是否為已存在的 Person
    var email = googleInfo.Principal.FindFirstValue(ClaimTypes.Email);
    var existingPerson = await _context.Persons
        .FirstOrDefaultAsync(p => p.Email == email);

    if (existingPerson != null)
    {
        // 同一個 Person，建立新的 ApplicationUser
        var newApplicationUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            PersonId = existingPerson.Id, // 連結到同一個 Person
            IsActive = true
        };
        
        var createResult = await _userManager.CreateAsync(newApplicationUser);
        if (createResult.Succeeded)
        {
            await _userManager.AddLoginAsync(newApplicationUser, googleInfo);
        }
    }
    else
    {
        // 新的 Person
        var newPerson = new Person
        {
            Id = Guid.NewGuid(),
            Email = email,
            FirstName = googleInfo.Principal.FindFirstValue(ClaimTypes.GivenName),
            LastName = googleInfo.Principal.FindFirstValue(ClaimTypes.Surname),
            CreatedAt = DateTime.UtcNow
        };
        await _context.Persons.AddAsync(newPerson);

        var newApplicationUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            PersonId = newPerson.Id,
            IsActive = true
        };
        
        await _userManager.CreateAsync(newApplicationUser);
        await _userManager.AddLoginAsync(newApplicationUser, googleInfo);
    }
}
```

### Scenario 3: 手動連結多個帳號（Account Linking）

```csharp
// 使用者已用 AD 帳號登入，現在想連結 Google 帳號
public async Task<bool> LinkGoogleAccountAsync(Guid userId)
{
    var user = await _userManager.FindByIdAsync(userId.ToString());
    if (user == null) return false;

    // 觸發 Google OAuth flow
    var properties = _signInManager.ConfigureExternalAuthenticationProperties(
        "Google",
        redirectUrl: "/Account/LinkExternalLogin"
    );
    
    return true;
}

// Callback 處理
public async Task<bool> LinkExternalLoginCallback()
{
    var info = await _signInManager.GetExternalLoginInfoAsync();
    if (info == null) return false;

    var user = await _userManager.GetUserAsync(User);
    var result = await _userManager.AddLoginAsync(user, info);
    
    return result.Succeeded;
}
```

## JIT Provisioning 流程

### 完整的 JIT Provisioning 應該做什麼

```csharp
public interface IJitProvisioningService
{
    /// <summary>
    /// 為外部認證使用者建立或更新 Person 和 ApplicationUser
    /// </summary>
    Task<ApplicationUser> ProvisionExternalUserAsync(
        ExternalAuthResult externalAuth,
        CancellationToken cancellationToken = default);
}

public class ExternalAuthResult
{
    public string Provider { get; set; } // "ActiveDirectory", "Google"
    public string ProviderKey { get; set; } // AD username or Google user ID
    public string Email { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string EmployeeId { get; set; }
    public string Department { get; set; }
    public Dictionary<string, string> AdditionalClaims { get; set; }
}

public class JitProvisioningService : IJitProvisioningService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;

    public async Task<ApplicationUser> ProvisionExternalUserAsync(
        ExternalAuthResult externalAuth,
        CancellationToken cancellationToken = default)
    {
        // Step 1: 檢查是否已有此外部登入
        var existingUser = await _userManager.FindByLoginAsync(
            externalAuth.Provider,
            externalAuth.ProviderKey
        );

        if (existingUser != null)
        {
            // 已存在，更新資訊
            existingUser.Email = externalAuth.Email ?? existingUser.Email;
            await _userManager.UpdateAsync(existingUser);
            return existingUser;
        }

        // Step 2: 檢查是否有相同 Email 的 Person（判斷是否為同一人）
        Person person = null;
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
                EmployeeId = externalAuth.EmployeeId,
                Department = externalAuth.Department,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "System"
            };
            await _context.Persons.AddAsync(person, cancellationToken);
        }

        // Step 4: 建立新的 ApplicationUser（連結到 Person）
        var newUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = externalAuth.Email ?? $"{externalAuth.Provider}_{externalAuth.ProviderKey}",
            Email = externalAuth.Email,
            EmailConfirmed = true,
            PersonId = person.Id,
            FirstName = externalAuth.FirstName,
            LastName = externalAuth.LastName,
            Department = externalAuth.Department,
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
        var addLoginResult = await _userManager.AddLoginAsync(
            newUser,
            new UserLoginInfo(
                externalAuth.Provider,
                externalAuth.ProviderKey,
                externalAuth.Provider
            )
        );

        if (!addLoginResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to add external login: {string.Join(", ", addLoginResult.Errors.Select(e => e.Description))}"
            );
        }

        // Step 6: 儲存變更
        await _context.SaveChangesAsync(cancellationToken);

        return newUser;
    }
}
```

## 資料查詢範例

### 取得 Person 的所有登入帳號

```csharp
var person = await _context.Persons
    .Include(p => p.ApplicationUsers)
        .ThenInclude(u => u.UserLogins) // AspNetUserLogins
    .FirstOrDefaultAsync(p => p.Id == personId);

foreach (var user in person.ApplicationUsers)
{
    Console.WriteLine($"Account: {user.Email}");
    foreach (var login in user.UserLogins)
    {
        Console.WriteLine($"  - {login.LoginProvider}: {login.ProviderKey}");
    }
}
```

### 取得目前登入使用者的 Person 資料

```csharp
var user = await _userManager.GetUserAsync(User);
var person = await _context.Persons
    .Include(p => p.ApplicationUsers)
    .FirstOrDefaultAsync(p => p.Id == user.PersonId);
```

## 常見問題

### Q1: 同一個 Person 可以同時用多個帳號登入嗎？
**A:** 可以！例如：
- 在電腦上用 AD 帳號登入
- 在手機上用 Google 帳號登入
- 兩個 session 都指向同一個 Person

### Q2: 如何防止帳號重複建立？
**A:** 透過以下機制：
1. 檢查 `AspNetUserLogins` 是否已有此 Provider + ProviderKey
2. 檢查 `Person.Email` 是否已存在（決定是建立新 Person 還是連結到已存在的 Person）

### Q3: 如何實作「帳號合併」功能？
**A:** 
```csharp
public async Task MergePersonsAsync(Guid sourcePersionId, Guid targetPersonId)
{
    // 將所有 ApplicationUser 指向 target Person
    var sourceUsers = await _context.Users
        .Where(u => u.PersonId == sourcePersionId)
        .ToListAsync();

    foreach (var user in sourceUsers)
    {
        user.PersonId = targetPersonId;
    }

    // 刪除 source Person
    var sourcePerson = await _context.Persons.FindAsync(sourcePersionId);
    _context.Persons.Remove(sourcePerson);

    await _context.SaveChangesAsync();
}
```

### Q4: JitProvisioningService 需要重構嗎？
**A:** 是的！目前的 JitProvisioningService：
- ❌ 沒有建立 Person
- ❌ 沒有建立 AspNetUserLogins
- ❌ 只建立了 ApplicationUser

應該改成：
- ✅ 建立或查詢 Person
- ✅ 建立 ApplicationUser（連結到 Person）
- ✅ 建立 AspNetUserLogins（連結外部認證）

## 實作檢查清單

- [ ] 重構 `JitProvisioningService` 支援 Person + ApplicationUser + AspNetUserLogins
- [ ] 實作 `ExternalAuthResult` DTO
- [ ] 實作 AD 認證的 JIT Provisioning
- [ ] 實作 Google OAuth 的 JIT Provisioning
- [ ] 實作帳號連結功能（Account Linking UI）
- [ ] 實作 Person 查詢 API（顯示所有登入帳號）
- [ ] 更新 E2E 測試驗證 multi-account 場景
