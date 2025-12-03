# SSO Entry Portal Architecture

## 🎯 Purpose

提供統一的應用程式入口，讓 users 可以從一個地方 SSO 到所有授權的應用程式。

---

## 🏗️ Architecture Overview

```
┌──────────────────────────────────────────────────────┐
│                  Browser (User)                      │
└──────────────────────────────────────────────────────┘
                        │
                        │ 1. Visit portal
                        ↓
┌──────────────────────────────────────────────────────┐
│         SSO Entry Portal (獨立的 Web App)             │
│  - Next.js / React / Vue                             │
│  - 顯示可用的 applications                            │
│  - 根據 user roles 過濾顯示                          │
└──────────────────────────────────────────────────────┘
                        │
                        │ 2. Login (OIDC)
                        ↓
┌──────────────────────────────────────────────────────┐
│              HybridAuth IdP (這個系統)                │
│  - 驗證使用者身份                                     │
│  - 發 access_token with roles                        │
└──────────────────────────────────────────────────────┘
                        │
                        │ 3. Get user info + roles
                        ↓
┌──────────────────────────────────────────────────────┐
│         SSO Entry Portal (已登入狀態)                 │
│  - 從 token 讀取 user roles                          │
│  - 顯示允許的 apps                                   │
│  - 點擊 app → 觸發 OIDC flow                        │
└──────────────────────────────────────────────────────┘
                        │
                        │ 4. Click "Email System"
                        ↓
┌──────────────────────────────────────────────────────┐
│              HybridAuth IdP                          │
│  - Silent authentication (已登入)                    │
│  - 發 token for Email System                        │
└──────────────────────────────────────────────────────┘
                        │
                        │ 5. Redirect with token
                        ↓
┌──────────────────────────────────────────────────────┐
│              Email System (目標 App)                  │
│  - 驗證 token                                        │
│  - 登入完成！                                        │
└──────────────────────────────────────────────────────┘
```

---

## 📋 Component Responsibilities

### 1. HybridAuth IdP (這個系統)
**責任**:
- ✅ 驗證使用者身份
- ✅ 管理 users/roles
- ✅ 註冊 OIDC clients
- ✅ 發行 tokens
- ✅ 提供 /connect/authorize, /connect/token endpoints
- ❌ **不負責**顯示 app 清單 (這是 Portal 的工作)

**現有功能**:
- Admin Portal: 管理 IdP 本身
- My Account: user 管理自己的授權記錄

---

### 2. SSO Entry Portal (新的獨立 App)
**責任**:
- ✅ 作為 OIDC client 註冊到 IdP
- ✅ 使用 OIDC 登入 IdP
- ✅ 顯示 user 可用的 applications
- ✅ 根據 roles 過濾顯示
- ✅ 提供 "Launch" 按鈕觸發 SSO

**技術棧建議**:
- Frontend: Next.js / React / Vue
- Authentication: OIDC Client Library
  - JavaScript: `oidc-client-ts`
  - .NET: `Microsoft.AspNetCore.Authentication.OpenIdConnect`
- Backend API: 管理 app catalog

---

### 3. Individual Applications (Email, HR, BI, etc.)
**責任**:
- ✅ 作為 OIDC client 註冊到 IdP
- ✅ 接受來自 IdP 的 tokens
- ✅ 驗證 roles 並控制內部權限
- ✅ 提供自己的功能

---

## 🗄️ Data Model for SSO Portal

### Applications Table (Portal 自己的 DB)
```sql
CREATE TABLE Applications (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL,
    Description NVARCHAR(500),
    IconUrl NVARCHAR(500),
    LaunchUrl NVARCHAR(500) NOT NULL,  -- URL to trigger OIDC flow
    
    -- 哪些角色可以看到這個 app
    RequiredRoles NVARCHAR(500),  -- JSON array: ["Admin", "Manager"]
    
    -- Display settings
    DisplayOrder INT,
    IsEnabled BIT DEFAULT 1,
    Category NVARCHAR(50),
    
    -- OIDC settings
    ClientId NVARCHAR(100),  -- Registered in IdP
    Scopes NVARCHAR(500)  -- openid profile email app_specific_scope
);

-- Example data
INSERT INTO Applications VALUES
(NEWID(), 'Email System', 'Corporate email', '/icons/email.png', 
 'https://email.company.com', '["User","Admin"]', 1, 1, 'Communication',
 'email-client', 'openid profile email'),
 
(NEWID(), 'Admin Panel', 'System administration', '/icons/admin.png',
 'https://admin.company.com', '["Admin"]', 10, 1, 'Administration',
 'admin-client', 'openid profile admin_api'),
 
(NEWID(), 'IdP Management', 'Manage identity provider', '/icons/idp.png',
 'https://idp.company.com', '["Admin"]', 11, 1, 'Administration',
 'idp-admin', 'openid profile idp_manage');
```

---

## 🔧 Implementation Steps

### Phase 1: Register SSO Portal as OIDC Client
```sql
-- In HybridAuth IdP database
INSERT INTO OpenIddictApplications (...)
VALUES (
    ClientId = 'sso-portal',
    DisplayName = 'SSO Entry Portal',
    RedirectUris = 'https://portal.company.com/signin-oidc',
    AllowedScopes = 'openid profile email roles'
);
```

### Phase 2: Create SSO Portal App
```bash
# Option 1: Next.js
npx create-next-app@latest sso-portal
cd sso-portal
npm install oidc-client-ts

# Option 2: ASP.NET Core MVC
dotnet new mvc -n SsoPortal
cd SsoPortal
dotnet add package Microsoft.AspNetCore.Authentication.OpenIdConnect
```

### Phase 3: Configure OIDC Authentication
```javascript
// Next.js example: lib/auth.ts
import { UserManager } from 'oidc-client-ts';

const oidcConfig = {
  authority: 'https://idp.company.com',
  client_id: 'sso-portal',
  redirect_uri: 'https://portal.company.com/signin-oidc',
  scope: 'openid profile email roles',
  response_type: 'code'
};

export const userManager = new UserManager(oidcConfig);
```

```csharp
// ASP.NET Core example: Program.cs
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "Cookies";
    options.DefaultChallengeScheme = "oidc";
})
.AddCookie("Cookies")
.AddOpenIdConnect("oidc", options =>
{
    options.Authority = "https://idp.company.com";
    options.ClientId = "sso-portal";
    options.ClientSecret = "secret";
    options.ResponseType = "code";
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");
    options.Scope.Add("roles");
    options.SaveTokens = true;
});
```

### Phase 4: Display Applications Based on Roles
```typescript
// pages/index.tsx (Next.js)
import { useEffect, useState } from 'react';
import { userManager } from '@/lib/auth';

interface Application {
  id: string;
  name: string;
  description: string;
  iconUrl: string;
  launchUrl: string;
  requiredRoles: string[];
}

export default function Dashboard() {
  const [apps, setApps] = useState<Application[]>([]);
  const [userRoles, setUserRoles] = useState<string[]>([]);

  useEffect(() => {
    userManager.getUser().then(user => {
      if (user) {
        // Get roles from token claims
        const roles = user.profile.role as string[];
        setUserRoles(roles);
        
        // Fetch available apps
        fetch('/api/applications')
          .then(res => res.json())
          .then(data => {
            // Filter apps based on user roles
            const filtered = data.filter((app: Application) =>
              app.requiredRoles.some(role => roles.includes(role))
            );
            setApps(filtered);
          });
      }
    });
  }, []);

  const launchApp = (app: Application) => {
    // Trigger OIDC flow for the target app
    window.location.href = `https://idp.company.com/connect/authorize?` +
      `client_id=${app.clientId}&` +
      `redirect_uri=${encodeURIComponent(app.launchUrl)}&` +
      `response_type=code&` +
      `scope=${encodeURIComponent(app.scopes)}&` +
      `state=${generateState()}`;
  };

  return (
    <div className="dashboard">
      <h1>Welcome, {user?.profile.name}</h1>
      <h2>Your Applications</h2>
      
      <div className="app-grid">
        {apps.map(app => (
          <div key={app.id} className="app-card" onClick={() => launchApp(app)}>
            <img src={app.iconUrl} alt={app.name} />
            <h3>{app.name}</h3>
            <p>{app.description}</p>
          </div>
        ))}
      </div>
    </div>
  );
}
```

```csharp
// ASP.NET Core example: Controllers/HomeController.cs
[Authorize]
public class HomeController : Controller
{
    private readonly IApplicationCatalogService _catalogService;

    public async Task<IActionResult> Index()
    {
        // Get user roles from claims
        var userRoles = User.Claims
            .Where(c => c.Type == "role")
            .Select(c => c.Value)
            .ToList();

        // Get available apps
        var allApps = await _catalogService.GetApplicationsAsync();
        
        // Filter by roles
        var availableApps = allApps.Where(app =>
            app.RequiredRoles.Any(role => userRoles.Contains(role))
        ).ToList();

        return View(availableApps);
    }
    
    [HttpPost]
    public IActionResult Launch(string appId)
    {
        var app = _catalogService.GetApplicationById(appId);
        
        // Redirect to IdP authorize endpoint
        var authorizeUrl = $"https://idp.company.com/connect/authorize?" +
            $"client_id={app.ClientId}&" +
            $"redirect_uri={Uri.EscapeDataString(app.LaunchUrl)}&" +
            $"response_type=code&" +
            $"scope={Uri.EscapeDataString(app.Scopes)}&" +
            $"state={GenerateState()}";
            
        return Redirect(authorizeUrl);
    }
}
```

---

## 🎨 UI Design Example

```
┌────────────────────────────────────────────────────────┐
│  🏢 Company Portal                    John Doe ▼ Logout│
├────────────────────────────────────────────────────────┤
│                                                        │
│  📱 Your Applications                                  │
│                                                        │
│  Communication                                         │
│  ┌──────────────┐  ┌──────────────┐                  │
│  │  📧         │  │  💬          │                  │
│  │  Email      │  │  Chat        │                  │
│  │  System     │  │  Teams       │                  │
│  └──────────────┘  └──────────────┘                  │
│                                                        │
│  Business Tools                                        │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐│
│  │  📊         │  │  💼          │  │  📈          ││
│  │  BI         │  │  HR          │  │  CRM         ││
│  │  Dashboard  │  │  Portal      │  │  System      ││
│  └──────────────┘  └──────────────┘  └──────────────┘│
│                                                        │
│  Administration (Admin Only)                           │
│  ┌──────────────┐  ┌──────────────┐                  │
│  │  🛠️         │  │  ⚙️           │                  │
│  │  Admin      │  │  IdP         │                  │
│  │  Panel      │  │  Manage      │                  │
│  └──────────────┘  └──────────────┘                  │
│                                                        │
│  📜 Recent Activity                                    │
│  • Logged into Email System - 2 mins ago              │
│  • Accessed HR Portal - 1 hour ago                    │
│                                                        │
└────────────────────────────────────────────────────────┘
```

---

## 🔐 Security Considerations

### 1. Role-Based Filtering
- ✅ Portal 根據 user roles 過濾顯示的 apps
- ✅ 但最終權限檢查在 target app 端
- ⚠️ Portal 的過濾只是 UX 優化，不能取代 app 的權限驗證

### 2. Token Refresh
```javascript
// Auto refresh tokens before expiration
setInterval(async () => {
  const user = await userManager.getUser();
  if (user && user.expires_in < 300) { // 5 minutes
    await userManager.signinSilent();
  }
}, 60000); // Check every minute
```

### 3. Logout Handling
```javascript
const logout = async () => {
  // Single logout from IdP
  await userManager.signoutRedirect({
    id_token_hint: user.id_token,
    post_logout_redirect_uri: 'https://portal.company.com'
  });
};
```

---

## 📊 Comparison: IdP vs SSO Portal

| Feature | HybridAuth IdP | SSO Entry Portal |
|---------|----------------|------------------|
| **Purpose** | 身份驗證 & 管理 | 統一應用入口 |
| **Users** | Admin (管理) + All users (My Account) | All authenticated users |
| **Main Function** | Issue tokens, manage users/clients | Display & launch apps |
| **Role Switching** | 切換 IdP 內部權限 (Admin/User) | 根據 role 顯示不同 apps |
| **Data** | Users, Roles, Clients, Tokens | Application catalog |
| **UI** | Admin management interface | User-friendly app launcher |
| **Authentication** | Self-hosted (ASP.NET Identity) | OIDC Client (依賴 IdP) |

---

## ✅ Recommended Approach

### Phase 11 (Current - IdP Features)
- ✅ 保留 My Account 功能 (user 管理自己的授權)
- ✅ 保留 Role switching (IdP Admin Portal 內部權限)
- ✅ 修正 cookie-based active role detection

### Phase 12 (New - SSO Portal)
- 🆕 創建獨立的 SSO Entry Portal application
- 🆕 註冊為 OIDC client
- 🆕 實作 app catalog & role-based filtering
- 🆕 提供統一的 SSO 入口

---

## 🎯 Summary

**你的理解完全正確**：

1. ✅ **IdP Admin Portal** 的 role switching 用途有限
   - 主要給 admin 切換管理權限用
   - 一般 user 不太需要（他們只用 My Account）

2. ✅ **SSO Entry Portal** 應該是獨立的 app
   - 提供統一入口顯示所有 applications
   - 根據 user roles 顯示不同的 apps
   - 這才是你想要的功能！

3. ✅ **My Account** 功能還是有用的
   - 讓 user 查看/撤銷授權記錄
   - 這是 GDPR/privacy 合規需要的功能

**建議**：
- Phase 11 完成基本的 role switching (cookie-based)
- 另外開 Phase 12 建立 SSO Entry Portal
