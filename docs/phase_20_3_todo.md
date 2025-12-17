# Phase 20.3 TODO: Custom JsonStringLocalizer Implementation

## Problem Statement
The current localization solution using `My.Extensions.Localization.Json` has proven unreliable for cross-project resource loading, specifically:
- Difficulty resolving resources located in `Infrastructure` assembly from `Web.IdP`.
- Inconsistent behavior between `dotnet run`, `dotnet watch`, and published builds regarding resource path resolution.
- "ResourceNotFound" for `EmailTemplateResource` despite correct file presence.

## Proposed Solution
Implement a lightweight, custom `IStringLocalizer` that explicitly handles our project structure and resource loading requirements.

## Implementation Details

### 1. Components
Move away from the third-party library and implement:

1.  **`JsonStringLocalizer`**:
    -   Implements `Microsoft.Extensions.Localization.IStringLocalizer`.
    -   Responsible for loading JSON files from disk and caching them.
    -   **Key Feature**: robust file search logic that checks multiple fallback paths:
        -   `ResourcesPath` relative to `AppContext.BaseDirectory` (Production/Bin).
        -   `ResourcesPath` relative to `Directory.GetCurrentDirectory()` (Development/Run).
        -   `../Infrastructure/{ResourcesPath}` (Development/Cross-Project).

2.  **`JsonStringLocalizerFactory`**:
    -   Implements `Microsoft.Extensions.Localization.IStringLocalizerFactory`.
    -   Responsible for creating `JsonStringLocalizer` instances.
    -   Handles Type-to-Filename mapping (e.g., `Infrastructure.Resources.EmailTemplateResource` -> `EmailTemplateResource.json`).

### 2. Location
-   Place the implementation in `Web.IdP` (e.g., `Web.IdP.Services.Localization`) to avoid circular dependencies and type-identity issues between `Infrastructure` and `Web.IdP`.
-   Alternatively, if shared, ensure `Microsoft.AspNetCore.App` framework reference is used consistently.

### 3. Usage
Register the factory in `ServiceCollectionExtensions.cs`:

```csharp
services.AddLocalization(options => options.ResourcesPath = "Resources");
services.AddSingleton<IStringLocalizerFactory, Web.IdP.Services.Localization.JsonStringLocalizerFactory>();
```

### 4. File Structure
Maintain standard resource naming to minimize confusion:
-   `Infrastructure/Resources/EmailTemplateResource.json` (English/Default)
-   `Infrastructure/Resources/EmailTemplateResource.zh-TW.json` (Traditional Chinese)

## verification
1.  Verify `EmailTemplateService` correctly resolves strings for `zh-TW` admin users.
2.  Verify fallback to `en-US` (or default keys) if resource missing.
3.  Verify operation in both `Development` (Source) and `Production` (Published) modes.
