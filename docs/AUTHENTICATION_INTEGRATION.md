# Authentication Integration Guide

## Overview

HybridAuthIdP supports multiple authentication methods through two primary integration patterns:

1. **Password-based Authentication** - Direct credential validation (Local, Active Directory, Legacy Systems)
2. **External Authentication** - OAuth/OIDC federation (Google, Azure AD, Facebook, etc.)

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     Authentication Flow                      │
└─────────────────────────────────────────────────────────────┘
                            │
                            ├─ Password-based (LoginService)
                            │   ├─ Local Password
                            │   ├─ Active Directory (LDAP)
                            │   └─ Legacy Systems
                            │
                            └─ External Login (OAuth/OIDC Middleware)
                                ├─ Google
                                ├─ Azure AD
                                ├─ Facebook
                                └─ Other OIDC Providers
```

## 1. Password-Based Authentication

### Current Implementation

Located in: `Infrastructure/Services/LoginService.cs`

```csharp
public async Task<LoginResult> AuthenticateAsync(string login, string password)
{
    // 1. Try local user
    var user = await _userManager.FindByEmailAsync(login) 
               ?? await _userManager.FindByNameAsync(login);
    
    if (user != null)
        return await AuthenticateLocalUserAsync(user, password);
    
    // 2. Try external password-based auth (AD, Legacy)
    return await AuthenticateLegacyUserAsync(login, password);
}
```

### Adding Active Directory Integration

**Step 1: Create IActiveDirectoryService**

```csharp
// Core.Application/IActiveDirectoryService.cs
public interface IActiveDirectoryService
{
    Task<AdAuthResult> ValidateAsync(string login, string password);
}

public class AdAuthResult
{
    public bool IsAuthenticated { get; set; }
    public string ObjectGuid { get; set; }  // AD unique identifier
    public string UserPrincipalName { get; set; }
    public string SamAccountName { get; set; }
    public string Email { get; set; }
    public string GivenName { get; set; }
    public string Surname { get; set; }
    public string DisplayName { get; set; }
    public string Department { get; set; }
    public string JobTitle { get; set; }
}
```

**Step 2: Implement AD Service**

```csharp
// Infrastructure/Services/ActiveDirectoryService.cs
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;

public class ActiveDirectoryService : IActiveDirectoryService
{
    private readonly string _ldapPath;
    private readonly string _domain;

    public ActiveDirectoryService(IConfiguration configuration)
    {
        _ldapPath = configuration["ActiveDirectory:LdapPath"];
        _domain = configuration["ActiveDirectory:Domain"];
    }

    public async Task<AdAuthResult> ValidateAsync(string login, string password)
    {
        try
        {
            using var context = new PrincipalContext(
                ContextType.Domain, 
                _domain, 
                login, 
                password
            );

            if (!context.ValidateCredentials(login, password))
            {
                return new AdAuthResult { IsAuthenticated = false };
            }

            // Get user details from AD
            using var searcher = new PrincipalSearcher(new UserPrincipal(context));
            var user = UserPrincipal.FindByIdentity(context, login);

            if (user == null)
            {
                return new AdAuthResult { IsAuthenticated = false };
            }

            return new AdAuthResult
            {
                IsAuthenticated = true,
                ObjectGuid = user.Guid.ToString(),
                UserPrincipalName = user.UserPrincipalName,
                SamAccountName = user.SamAccountName,
                Email = user.EmailAddress,
                GivenName = user.GivenName,
                Surname = user.Surname,
                DisplayName = user.DisplayName,
                // Can add more AD attributes as needed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Active Directory authentication failed for {Login}", login);
            return new AdAuthResult { IsAuthenticated = false };
        }
    }
}
```

**Step 3: Update LoginService**

```csharp
// Infrastructure/Services/LoginService.cs
public class LoginService : ILoginService
{
    private readonly IActiveDirectoryService _activeDirectoryService;
    
    public LoginService(
        UserManager<ApplicationUser> userManager,
        ISecurityPolicyService securityPolicyService,
        ILegacyAuthService legacyAuthService,
        IActiveDirectoryService activeDirectoryService,
        IJitProvisioningService jitProvisioningService,
        ILogger<LoginService> logger)
    {
        _userManager = userManager;
        _securityPolicyService = securityPolicyService;
        _legacyAuthService = legacyAuthService;
        _activeDirectoryService = activeDirectoryService;
        _jitProvisioningService = jitProvisioningService;
        _logger = logger;
    }

    public async Task<LoginResult> AuthenticateAsync(string login, string password)
    {
        // 1. Try local user first
        var user = await _userManager.FindByEmailAsync(login) 
                   ?? await _userManager.FindByNameAsync(login);

        if (user != null)
        {
            return await AuthenticateLocalUserAsync(user, password);
        }

        // 2. Try Active Directory (if login format matches)
        if (IsActiveDirectoryLogin(login))
        {
            return await AuthenticateAdUserAsync(login, password);
        }

        // 3. Fallback to legacy authentication
        return await AuthenticateLegacyUserAsync(login, password);
    }

    private async Task<LoginResult> AuthenticateAdUserAsync(string login, string password)
    {
        // Validate credentials against AD
        var adResult = await _activeDirectoryService.ValidateAsync(login, password);
        if (!adResult.IsAuthenticated)
        {
            return LoginResult.InvalidCredentials();
        }

        // Check if user already exists with AD login
        var user = await _userManager.FindByLoginAsync("ActiveDirectory", adResult.ObjectGuid);
        
        if (user != null)
        {
            _logger.LogInformation("User '{Login}' authenticated via Active Directory.", login);
            return LoginResult.Success(user);
        }

        // JIT Provisioning for new AD user
        var provisionedUser = await _jitProvisioningService.ProvisionUserAsync(new LegacyAuthResult
        {
            IsAuthenticated = true,
            Login = adResult.UserPrincipalName ?? login,
            Email = adResult.Email ?? $"{adResult.SamAccountName}@yourdomain.com",
            FirstName = adResult.GivenName,
            LastName = adResult.Surname,
        });

        // Link AD login to the provisioned user
        await _userManager.AddLoginAsync(provisionedUser, new UserLoginInfo(
            loginProvider: "ActiveDirectory",
            providerKey: adResult.ObjectGuid,
            displayName: "Active Directory"
        ));

        _logger.LogInformation("User '{Login}' authenticated via AD and JIT provisioned.", login);
        return LoginResult.Success(provisionedUser);
    }

    private bool IsActiveDirectoryLogin(string login)
    {
        // Detect AD login format:
        // - domain\username
        // - username@domain.com (if configured AD domain)
        return login.Contains("\\") || 
               login.EndsWith("@youraddomain.com", StringComparison.OrdinalIgnoreCase);
    }
}
```

**Step 4: Configuration**

```json
// appsettings.json
{
  "ActiveDirectory": {
    "Enabled": true,
    "Domain": "YOURDOMAIN",
    "LdapPath": "LDAP://dc.yourdomain.com",
    "DefaultDomain": "youraddomain.com"
  }
}
```

**Step 5: Register Services**

```csharp
// Program.cs or DI Configuration
services.AddScoped<IActiveDirectoryService, ActiveDirectoryService>();
```

### Benefits of Password-Based AD Integration

✅ **Direct authentication** - No redirect to external login page  
✅ **Seamless UX** - Same login form as local users  
✅ **JIT Provisioning** - Auto-create user on first login  
✅ **Multiple accounts** - Same person can have both AD and local accounts  
✅ **AspNetUserLogins tracking** - All external authentications recorded

---

## 2. External Authentication (OAuth/OIDC)

### Overview

For OAuth/OIDC providers (Google, Azure AD, Facebook), use ASP.NET Core's **External Authentication** middleware. These providers require browser redirects and token exchange.

### Implementation Flow

```
User clicks "Sign in with Google"
    ↓
Redirect to Google OAuth
    ↓
User authenticates at Google
    ↓
Google redirects back with authorization code
    ↓
Exchange code for tokens
    ↓
ExternalLoginCallback handler
    ↓
Check AspNetUserLogins for existing link
    ↓
If exists: Sign in
If not: Show account linking UI or auto-provision
```

### Example: Google OAuth Integration

**Step 1: Install Package**

```bash
dotnet add package Microsoft.AspNetCore.Authentication.Google
```

**Step 2: Configure in Program.cs**

```csharp
builder.Services.AddAuthentication()
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Google:ClientId"];
        options.ClientSecret = builder.Configuration["Google:ClientSecret"];
        options.CallbackPath = "/signin-google";
        
        // Request additional scopes
        options.Scope.Add("profile");
        options.Scope.Add("email");
        
        // Save tokens for API calls
        options.SaveTokens = true;
    });
```

**Step 3: Add External Login UI**

```cshtml
<!-- Pages/Account/Login.cshtml -->
<form method="post" asp-page-handler="ExternalLogin">
    <input type="hidden" name="provider" value="Google" />
    <button type="submit" class="btn btn-google">
        <i class="fab fa-google"></i> Sign in with Google
    </button>
</form>
```

**Step 4: Handle External Login Callback**

```csharp
// Pages/Account/Login.cshtml.cs
public async Task<IActionResult> OnPostExternalLoginAsync(string provider, string returnUrl = null)
{
    var redirectUrl = Url.Page("./ExternalLogin", 
        pageHandler: "Callback", 
        values: new { returnUrl });
    
    var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
    return new ChallengeResult(provider, properties);
}

public async Task<IActionResult> OnGetCallbackAsync(string returnUrl = null, string remoteError = null)
{
    if (remoteError != null)
    {
        return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
    }

    var info = await _signInManager.GetExternalLoginInfoAsync();
    if (info == null)
    {
        return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
    }

    // Sign in with external login provider
    var result = await _signInManager.ExternalLoginSignInAsync(
        info.LoginProvider, 
        info.ProviderKey, 
        isPersistent: false, 
        bypassTwoFactor: true
    );

    if (result.Succeeded)
    {
        _logger.LogInformation("{Name} logged in with {LoginProvider}.", 
            info.Principal.Identity.Name, info.LoginProvider);
        return LocalRedirect(returnUrl ?? "/");
    }

    // User doesn't exist, show account linking or auto-provision
    if (result.IsNotAllowed || result.IsLockedOut)
    {
        return RedirectToPage("./Lockout");
    }
    else
    {
        // Store info for account linking/creation
        ViewData["ReturnUrl"] = returnUrl;
        ViewData["LoginProvider"] = info.LoginProvider;
        
        // Get email from external provider
        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        
        // Option 1: Show account linking UI
        return Page(); // Show form to link or create account
        
        // Option 2: Auto-provision (JIT)
        // return await AutoProvisionExternalUserAsync(info, returnUrl);
    }
}

private async Task<IActionResult> AutoProvisionExternalUserAsync(
    ExternalLoginInfo info, 
    string returnUrl)
{
    var email = info.Principal.FindFirstValue(ClaimTypes.Email);
    var user = new ApplicationUser
    {
        UserName = email,
        Email = email,
        EmailConfirmed = true, // Trust external provider
        FirstName = info.Principal.FindFirstValue(ClaimTypes.GivenName),
        LastName = info.Principal.FindFirstValue(ClaimTypes.Surname),
    };

    var result = await _userManager.CreateAsync(user);
    if (result.Succeeded)
    {
        result = await _userManager.AddLoginAsync(user, info);
        if (result.Succeeded)
        {
            await _signInManager.SignInAsync(user, isPersistent: false);
            return LocalRedirect(returnUrl ?? "/");
        }
    }

    // Handle errors
    foreach (var error in result.Errors)
    {
        ModelState.AddModelError(string.Empty, error.Description);
    }
    return Page();
}
```

### Supported External Providers

| Provider | Package | Documentation |
|----------|---------|---------------|
| Google | `Microsoft.AspNetCore.Authentication.Google` | [Docs](https://docs.microsoft.com/en-us/aspnet/core/security/authentication/social/google-logins) |
| Microsoft | `Microsoft.AspNetCore.Authentication.MicrosoftAccount` | [Docs](https://docs.microsoft.com/en-us/aspnet/core/security/authentication/social/microsoft-logins) |
| Azure AD | `Microsoft.Identity.Web` | [Docs](https://docs.microsoft.com/en-us/azure/active-directory/develop/quickstart-v2-aspnet-core-webapp) |
| Facebook | `Microsoft.AspNetCore.Authentication.Facebook` | [Docs](https://docs.microsoft.com/en-us/aspnet/core/security/authentication/social/facebook-logins) |
| Twitter | `Microsoft.AspNetCore.Authentication.Twitter` | [Docs](https://docs.microsoft.com/en-us/aspnet/core/security/authentication/social/twitter-logins) |
| GitHub | `AspNet.Security.OAuth.GitHub` | [GitHub](https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers) |

---

## Key Differences

| Aspect | Password-Based (AD/LDAP) | External Authentication (OAuth/OIDC) |
|--------|-------------------------|-------------------------------------|
| **Flow** | Direct credential validation | Browser redirect + token exchange |
| **UI** | Same login form | External provider's login page |
| **Implementation** | LoginService method | ASP.NET middleware + callback |
| **Use Case** | Internal systems, AD, LDAP | Public OAuth providers (Google, etc.) |
| **User Experience** | Seamless, same page | Redirect to external site |

---

## Database Schema

All external authentications (both password-based and OAuth) are tracked in `AspNetUserLogins`:

```sql
CREATE TABLE AspNetUserLogins (
    LoginProvider VARCHAR(128) NOT NULL,    -- "ActiveDirectory", "Google", "AzureAD"
    ProviderKey VARCHAR(128) NOT NULL,      -- Unique ID from provider
    ProviderDisplayName VARCHAR(255),       -- Display name for UI
    UserId UNIQUEIDENTIFIER NOT NULL,       -- FK to AspNetUsers
    PRIMARY KEY (LoginProvider, ProviderKey)
);
```

**Examples:**

| LoginProvider | ProviderKey | ProviderDisplayName | UserId |
|--------------|-------------|---------------------|---------|
| ActiveDirectory | `{AD-ObjectGUID}` | Active Directory | user-guid-1 |
| Google | `{Google-sub-claim}` | Google | user-guid-1 |
| AzureAD | `{Azure-oid}` | Azure Active Directory | user-guid-2 |

---

## Person-User Relationship

```
Person (Physical Identity)
  └─ ApplicationUser #1 (john.doe@company.com)
      ├─ AspNetUserLogins: LoginProvider="Local" (password)
      └─ AspNetUserLogins: LoginProvider="ActiveDirectory", ProviderKey="{AD-GUID}"
  └─ ApplicationUser #2 (john.personal@gmail.com)
      └─ AspNetUserLogins: LoginProvider="Google", ProviderKey="{Google-sub}"
```

**Key Points:**
- One Person can have multiple ApplicationUsers
- Each ApplicationUser can have multiple external logins
- Person represents real-world identity (verified by ID documents)
- ApplicationUser represents authentication accounts

---

## Testing

### Test AD Authentication

```bash
# Local user
POST /Account/Login
{
  "login": "admin@hybridauth.local",
  "password": "Admin@123"
}

# AD user (domain\username)
POST /Account/Login
{
  "login": "DOMAIN\\johndoe",
  "password": "ADPassword123"
}

# AD user (UPN)
POST /Account/Login
{
  "login": "johndoe@addomain.com",
  "password": "ADPassword123"
}
```

### Test External Login

```bash
# Navigate to login page
GET /Account/Login

# Click "Sign in with Google" button
# This will redirect to Google OAuth
# After authentication, callback to /signin-google
```

---

## Best Practices

1. **Security**
   - Always use HTTPS for external authentication
   - Validate all claims from external providers
   - Implement proper CSRF protection

2. **User Experience**
   - Show clear login options (Local, AD, Google, etc.)
   - Handle account linking gracefully
   - Provide fallback for failed external auth

3. **Data Management**
   - Store minimal data from external providers
   - Keep Person data separate from auth data
   - Use `AspNetUserLogins` for all external auth tracking

4. **Monitoring**
   - Log all authentication attempts
   - Track JIT provisioning events
   - Monitor external provider failures

---

## Future Enhancements

- [ ] Add Azure AD B2C support
- [ ] Implement LINE Login (popular in Taiwan/Asia)
- [ ] Add SAML 2.0 support for enterprise SSO
- [ ] Multi-factor authentication with external providers
- [ ] Admin UI for managing external provider configurations
