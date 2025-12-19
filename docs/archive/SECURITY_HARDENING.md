# Security Hardening Implementation

## Overview
This document describes the comprehensive security hardening implementation for HybridAuthIdP to achieve CSP compliance and pass vulnerability scans.

## Security Headers Implementation

### SecurityHeadersMiddleware
Location: `Web.IdP/Middleware/SecurityHeadersMiddleware.cs`

**Implemented Headers:**
- **Content-Security-Policy**: Restricts resource loading to prevent XSS attacks
  - `default-src 'self'`
  - `script-src 'self' https://cdn.jsdelivr.net https://challenges.cloudflare.com` (Bootstrap + Turnstile)
  - `script-src-elem` - same as script-src (explicit element loading)
  - `style-src 'self' https://cdn.jsdelivr.net https://challenges.cloudflare.com` (Bootstrap CSS/Icons + Turnstile)
  - `style-src-elem 'self' https://cdn.jsdelivr.net https://challenges.cloudflare.com` - **Blocks inline `<style>` tags**
  - `style-src-attr 'none'` - **Blocks inline style attributes**
  - `font-src 'self' https://cdn.jsdelivr.net data:` (Bootstrap Icons)
  - `img-src 'self' data: https:`
  - `connect-src 'self' wss: https://cdn.jsdelivr.net https://challenges.cloudflare.com` (SignalR, source maps, Turnstile)
  - `frame-src https://challenges.cloudflare.com` (Turnstile iframe)
  - `frame-ancestors 'none'` (prevents embedding)
  - `object-src 'none'` (blocks plugins)
  - Development mode: Adds `'unsafe-eval'` for Vite and `ws:` for HMR

- **X-Content-Type-Options**: `nosniff` - Prevents MIME type sniffing

- **X-Frame-Options**: `DENY` - Prevents clickjacking attacks

- **X-XSS-Protection**: `1; mode=block` - Enables browser XSS protection

- **Referrer-Policy**: `strict-origin-when-cross-origin` - Controls referrer information

- **Permissions-Policy**: Disables dangerous browser features:
  - `camera=()` - No camera access
  - `microphone=()` - No microphone access
  - `geolocation=()` - No geolocation access
  - `payment=()` - No payment API
  - `usb=()` - No USB access

- **Strict-Transport-Security**: `max-age=31536000; includeSubDomains; preload` (Production only)

- **Server Header**: Removed for security through obscurity

- **X-Powered-By**: Removed to hide technology stack

## Secure Cookie Configuration

### Application Cookie (Authentication)
Location: `Web.IdP/Program.cs`

```csharp
services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;              // Prevents JavaScript access
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;  // HTTPS only
    options.Cookie.SameSite = SameSiteMode.Lax;  // CSRF protection
    options.Cookie.Name = ".HybridAuthIdP.Identity";
    // ... other settings
});
```

### Session Cookie
```csharp
services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Name = ".HybridAuthIdP.Session";
    // ... other settings
});
```

### Antiforgery Token Cookie
```csharp
services.AddAntiforgery(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;  // Strict for CSRF tokens
    options.Cookie.Name = ".HybridAuthIdP.Antiforgery";
});
```

## Inline Style and Script Removal

### Removed Inline Styles
All inline styles have been removed and converted to CSS classes to comply with CSP:

1. **Homepage (Index.cshtml)**
   - Authorization card gradient background → `.home-icon-container.authorization`
   - Linked Accounts card gradient background → `.home-icon-container.linked-accounts`

2. **Linked Accounts (LinkedAccounts.cshtml)**
   - Avatar sizing → `.account-avatar`

3. **Authorizations (Authorizations.cshtml)**
   - Dynamic app icon gradients → `.app-icon.gradient-0` through `.gradient-7`
   - Converted `GetAppIconStyle()` to `GetAppIconClass()` for class-based approach

4. **Admin Layout (_AdminLayout.cshtml)**
   - Language selector width → `.admin-nav-width`

5. **OAuth Authorize (Authorize.cshtml)**
   - Scope icon sizing → `.scope-icon`

### CSS Classes Added
Location: `Web.IdP/wwwroot/css/site.css`

```css
/* Homepage icon containers */
.home-icon-container { width: 80px; height: 80px; border-radius: 20px; display: flex; align-items: center; justify-content: center; }
.home-icon-container.authorization { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); }
.home-icon-container.linked-accounts { background: linear-gradient(135deg, #43e97b 0%, #38f9d7 100%); }

/* Avatar and icon sizing */
.account-avatar { width: 48px; height: 48px; font-size: 1.25rem; }
.scope-icon { width: 20px; height: 20px; }
.admin-nav-width { width: 140px; }

/* App icon gradients (8 variants) */
.app-icon.gradient-0 { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); }
.app-icon.gradient-1 { background: linear-gradient(135deg, #f093fb 0%, #f5576c 100%); }
.app-icon.gradient-2 { background: linear-gradient(135deg, #4facfe 0%, #00f2fe 100%); }
.app-icon.gradient-3 { background: linear-gradient(135deg, #43e97b 0%, #38f9d7 100%); }
.app-icon.gradient-4 { background: linear-gradient(135deg, #fa709a 0%, #fee140 100%); }
.app-icon.gradient-5 { background: linear-gradient(135deg, #30cfd0 0%, #330867 100%); }
.app-icon.gradient-6 { background: linear-gradient(135deg, #a8edea 0%, #fed6e3 100%); }
.app-icon.gradient-7 { background: linear-gradient(135deg, #ff9a56 0%, #ff6a88 100%); }
```

### Removed Inline Scripts
All inline JavaScript has been moved to external files:

1. **Menu Active State Logic**
   - Location: `Web.IdP/wwwroot/js/menu.js`
   - Function: Highlights active menu items based on current URL
   - Loaded with: `<script src="~/js/menu.js" asp-append-version="true"></script>`

## Verification

### No Inline Styles
```powershell
grep -r 'style="' Web.IdP/**/*.cshtml
# Result: No matches found
```

### No Inline Scripts
```powershell
grep -r '<script>' Web.IdP/**/*.cshtml
# Result: No matches found (only external script references remain)
```

### Build Status
```
✅ Build successful
✅ 328 tests passed (6 failing tests are pre-existing RED tests for unimplemented session refresh features)
```

## Browser Compatibility

### CSP Policy Testing
The CSP policy allows:
- ✅ Bootstrap 5.3.2 from CDN (css/js)
- ✅ Bootstrap Icons 1.11.1 from CDN
- ✅ Cloudflare Turnstile (script, style, iframe, connect)
- ✅ Source map files (.map) from CDN
- ✅ Vite HMR in development (WebSocket, unsafe-eval)
- ✅ SignalR in production (WebSocket over TLS)
- ❌ Inline styles (blocked by `style-src-elem` and `style-src-attr 'none'`)
- ❌ Inline `<style>` tags (blocked by `style-src-elem`)
- ❌ Inline style attributes (blocked by `style-src-attr 'none'`)
- ❌ Inline scripts (blocked)
- ❌ Eval-based code execution in production (blocked)

**Note**: No duplicate CSP directives - each directive appears only once in the policy.

## Middleware Registration

Location: `Web.IdP/Program.cs`

```csharp
app.UseHttpsRedirection();
app.UseSecurityHeaders();  // ← Security headers middleware
app.UseStaticFiles();
// ... rest of middleware pipeline
```

## Security Checklist

- ✅ Content Security Policy (CSP) implemented
- ✅ X-Content-Type-Options: nosniff
- ✅ X-Frame-Options: DENY
- ✅ X-XSS-Protection enabled
- ✅ Strict-Transport-Security (HSTS) in production
- ✅ Referrer-Policy configured
- ✅ Permissions-Policy restricts dangerous features
- ✅ Server header removed
- ✅ X-Powered-By header removed
- ✅ All cookies HttpOnly
- ✅ All cookies Secure (HTTPS only)
- ✅ All cookies SameSite configured
- ✅ No inline styles
- ✅ No inline scripts
- ✅ External scripts from trusted CDN only
- ✅ Custom cookie names (no default ASP.NET names)

## Testing Recommendations

1. **Vulnerability Scanning**: Run OWASP ZAP or similar tools against the application
2. **CSP Testing**: Check browser console for CSP violations
3. **Header Verification**: Use browser DevTools Network tab to verify all security headers
4. **Cookie Inspection**: Verify HttpOnly, Secure, and SameSite attributes in browser DevTools
5. **HTTPS Enforcement**: Test that HTTP requests redirect to HTTPS
6. **Content Injection**: Attempt XSS attacks to verify CSP blocks malicious scripts

## Notes

- Development mode includes `unsafe-eval` for Vite build tool compatibility
- Production mode uses stricter CSP without unsafe-eval
- WebSocket (wss:) is allowed in production for SignalR real-time monitoring
- All gradients and styles are now CSS-based for maintainability and CSP compliance
