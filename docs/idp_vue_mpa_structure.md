# Vue.js 3 Multi-Page Application (MPA) Structure

This document outlines the Multi-Page Application (MPA) architecture for the `ClientApp` (Vue.js) portion of the HybridAuthIdP project, following the official `Vite.AspNetCore` library documentation.

---

## 1. Directory Structure

The directory structure remains the same. The `ClientApp` folder is the root of the Vite project, and each functional area (e.g., `admin`, `account-manage`) has its own entry point.

```
Web.IdP/
├── ClientApp/                # The value for 'PackageDirectory'
│   ├── src/
│   │   ├── admin/                # Admin Portal Application
│   │   │   └── main.js           # Entry point for Admin app
│   │   │
│   │   ├── account-manage/       # User Self-Service Application
│   │   │   └── main.js           # Entry point for Account app
│   │   └── ...
│   │
│   └── package.json
│
├── appsettings.Development.json  # Vite server configuration
├── vite.config.js                # Vite build configuration
└── ...
```

---

## 2. Configuration

Configuration is split between `vite.config.js` (for build settings) and `appsettings.json` (for server and library settings), as recommended by the `Vite.AspNetCore` documentation.

### 2.1. Vite Configuration (`vite.config.js`)

This file is primarily for Vite's build process. We define the `root` and the MPA entry points in `build.rollupOptions.input`.

```javascript
// vite.config.js
import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';

export default defineConfig({
  plugins: [vue()],
  // The root of our frontend application, relative to the .NET project root.
  root: 'ClientApp',
  build: {
    // The output directory, relative to the .NET project root.
    outDir: '../wwwroot/dist',
    emptyOutDir: true,
    // Generate a manifest.json file for the Vite.AspNetCore tag helpers.
    manifest: true,
    rollupOptions: {
      // Define the entry points for our MPA.
      // The path is relative to the `root` directory.
      input: {
        admin: './src/admin/main.js',
        accountManage: './src/account-manage/main.js',
      },
    },
  },
});
```

### 2.2. ASP.NET Core Configuration (`appsettings.Development.json`)

Runtime behavior, like starting the Vite Dev Server, is controlled here. This avoids hardcoding paths in `Program.cs`.

```json
// appsettings.Development.json
{
  "Vite": {
    "Server": {
      // Enable the automatic start of the Vite Development Server.
      "AutoRun": true,
      // The directory where the package.json file is located.
      "PackageDirectory": "ClientApp"
    }
  }
}
```

### 2.3. Service Registration (`Program.cs`)

The service registration is now much simpler, as the configuration is loaded from `appsettings.json`.

```csharp
// In Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

// 1. Add Vite services.
builder.Services.AddViteServices();

var app = builder.Build();

// ...

// 2. Use the Vite Development Server in development.
if (app.Environment.IsDevelopment())
{
    app.UseViteDevelopmentServer();
}

// ...

app.Run();
```

---

## 3. Usage in Razor Pages

With the configuration in place, using the MPA entry points in Razor is done with the `vite-src` tag helper. This is the recommended approach from the documentation.

### 3.1. Enable Tag Helpers (`_ViewImports.cshtml`)

First, make the tag helpers available in all Razor views.

```csharp
// In _ViewImports.cshtml
@addTagHelper *, Vite.AspNetCore
```

### 3.2. Use the `vite-src` Tag Helper

In your Razor Page, use a `<script>` tag with the `vite-src` attribute. The path should be relative to the `PackageDirectory` (`ClientApp`). The tag helper automatically handles generating the correct URL for both development and production.

**Example: Admin Page (`/Pages/Admin/Clients/Index.cshtml`)**

```html
@page

@{ 
    ViewData["Title"] = "Client Management";
}

@* This div is the mount point for our Admin Vue app. *@
<div id="app"></div>

@section Scripts {
    @*  
      The vite-src attribute points to the script entry point.
      The path is relative to the 'PackageDirectory' defined in appsettings.json (i.e., 'ClientApp').
      The tag helper will resolve this to the Vite dev server URL in development,
      or the compiled asset path from the manifest in production.
    *@
    <script type="module" vite-src="~/src/admin/main.js"></script>
}
```
