# HybridIdP 實作指南

> 📋 本文件包含開發規範、最佳實踐、架構模式和實作範本

## 目錄

1. [架構概覽](#架構概覽)
2. [技術堆疊](#技術堆疊)
3. [Hybrid 架構模式](#hybrid-架構模式)
4. [API 實作範本](#api-實作範本)
5. [UI 實作範本](#ui-實作範本)
6. [Tailwind CSS 設定](#tailwind-css-設定)
7. [測試範本](#測試範本)
8. [常見陷阱](#常見陷阱)

---

## 架構概覽

### Hybrid Architecture Pattern

本專案採用 **Hybrid 架構**，結合伺服器端渲染和客戶端互動的優勢：

```text
┌─────────────────────────────────────────────────────────┐
│ Razor Page (.cshtml) - Server-side Authorization       │
│ ┌─────────────────────────────────────────────────────┐ │
│ │ Bootstrap 5 Layout (_AdminLayout.cshtml)            │ │
│ │ ┌────────────┐ ┌──────────────────────────────────┐ │ │
│ │ │  Sidebar   │ │  Main Content Area               │ │ │
│ │ │ (Bootstrap)│ │  ┌────────────────────────────┐  │ │ │
│ │ │            │ │  │ Vue.js SPA (Vite)          │  │ │
│ │ │ - Nav      │ │  │ - Tailwind CSS             │  │ │
│ │ │ - Profile  │ │  │ - Interactive CRUD         │  │ │
│ │ │            │ │  │ - API Integration          │  │ │
│ │ └────────────┘ │  └────────────────────────────┘  │ │ │
│ │                │                                    │ │ │
│ └─────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

### 職責分離

| 層級 | 技術 | 職責 |
|------|------|------|
| **路由 & 授權** | Razor Pages | `[Authorize(Roles = "Admin")]` 伺服器端安全 |
| **外層佈局** | Bootstrap 5 (CDN) | Sidebar, Header, Footer - 穩定、無建置需求 |
| **內容區域** | Vue.js 3 + Vite | 互動式 CRUD、表單驗證、狀態管理 |
| **樣式** | Tailwind CSS | Vue 組件樣式（透過 Vite 處理） |
| **API** | ASP.NET Core | RESTful API、驗證、業務邏輯 |

### 優勢

- ✅ **伺服器端安全**: Razor Pages 控制路由和授權（不能繞過）
- ✅ **穩定佈局**: Bootstrap 5 透過 CDN，不依賴 Vite
- ✅ **互動性**: Vue.js 提供現代化使用者體驗
- ✅ **SEO 友善**: 伺服器端渲染的外層結構
- ✅ **開發效率**: Vite HMR 提供快速開發循環

---

## 技術堆疊

### Backend

- **Framework**: ASP.NET Core .NET 9
- **Database**: PostgreSQL 17
- **ORM**: Entity Framework Core 9
- **Authentication**: OpenIddict 6.x
- **Authorization**: Role-based (`Admin`, `User`)
- **Testing**: xUnit, Moq

### Frontend

- **Build Tool**: Vite 5.4.21
- **Framework**: Vue.js 3.5.13 (Composition API)
- **Styling**: Tailwind CSS 3.4.17
- **Layout**: Bootstrap 5.3.2 (CDN)
- **Icons**: Bootstrap Icons 1.11.1
- **Testing**: Playwright (E2E)

### Development

- **IDE**: Visual Studio Code / Rider
- **Version Control**: Git (Conventional Commits)
- **Containerization**: Docker (PostgreSQL)
- **API Testing**: Swagger UI

---

## Hybrid 架構模式

### 檔案結構範例

以 **Users Management** 為例：

```text
Web.IdP/
├── Pages/
│   └── Admin/
│       └── Users.cshtml              # Razor Page (路由 + 授權)
│           └── Users.cshtml.cs       # PageModel (可選)
├── ClientApp/
│   └── src/
│       └── admin/
│           └── users/
│               ├── main.js           # Vue SPA 入口點
│               ├── style.css         # ⚠️ Tailwind CSS 指令
│               ├── UsersApp.vue      # 主組件
│               └── components/       # 子組件
│                   ├── UserList.vue
│                   ├── UserForm.vue
│                   └── ...
└── Api/
    └── Admin/
        └── UsersController.cs        # API Controller
```

### 1. Razor Page 範本

**`Pages/Admin/Users.cshtml`**

```cshtml
@page
@model Web.IdP.Pages.Admin.UsersModel
@{
    ViewData["Title"] = "User Management";
    ViewData["Breadcrumb"] = "Users";
    Layout = "_AdminLayout";
}

<div id="app"></div>

@section Scripts {
    <script type="module" src="~/src/admin/users/main.js" asp-append-version="true"></script>
}
```

**`Pages/Admin/Users.cshtml.cs`**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Core.Domain.Constants;

namespace Web.IdP.Pages.Admin;

[Authorize(Roles = AuthConstants.Roles.Admin)]
public class UsersModel : PageModel
{
    public void OnGet()
    {
        // Optional: Pre-render data or setup
    }
}
```

### 2. Vue SPA 入口點

**`ClientApp/src/admin/users/style.css`** ⚠️ **必須建立**

```css
@tailwind base;
@tailwind components;
@tailwind utilities;
```

**`ClientApp/src/admin/users/main.js`** ⚠️ **必須 import style.css**

```javascript
import { createApp } from 'vue';
import './style.css';  // ⚠️ 關鍵！沒有這行樣式會跑掉
import UsersApp from './UsersApp.vue';

const app = createApp(UsersApp);
app.mount('#app');
```

### 3. Vue 主組件範本

**`ClientApp/src/admin/users/UsersApp.vue`**

```vue
<template>
  <div class="container-fluid">
    <!-- Header with title and actions -->
    <div class="d-flex justify-content-between align-items-center mb-4">
      <h1 class="h3">User Management</h1>
      <button @click="showCreateModal = true" class="btn btn-primary">
        <i class="bi bi-plus-circle me-2"></i>Create User
      </button>
    </div>

    <!-- Search and filters -->
    <div class="card mb-4">
      <div class="card-body">
        <div class="row g-3">
          <div class="col-md-4">
            <input 
              v-model="searchQuery" 
              type="text" 
              class="form-control" 
              placeholder="Search users..."
            >
          </div>
          <div class="col-md-3">
            <select v-model="filterRole" class="form-select">
              <option value="">All Roles</option>
              <option value="Admin">Admin</option>
              <option value="User">User</option>
            </select>
          </div>
        </div>
      </div>
    </div>

    <!-- User list component -->
    <UserList 
      :users="filteredUsers" 
      :loading="loading"
      @edit="handleEdit"
      @delete="handleDelete"
    />

    <!-- Create/Edit modal -->
    <UserFormModal 
      v-if="showCreateModal"
      @close="showCreateModal = false"
      @save="handleSave"
    />
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue';
import UserList from './components/UserList.vue';
import UserFormModal from './components/UserFormModal.vue';

const users = ref([]);
const loading = ref(false);
const searchQuery = ref('');
const filterRole = ref('');
const showCreateModal = ref(false);

const filteredUsers = computed(() => {
  return users.value.filter(user => {
    const matchesSearch = user.email.toLowerCase().includes(searchQuery.value.toLowerCase());
    const matchesRole = !filterRole.value || user.roles.includes(filterRole.value);
    return matchesSearch && matchesRole;
  });
});

const fetchUsers = async () => {
  loading.value = true;
  try {
    const response = await fetch('/api/admin/users');
    users.value = await response.json();
  } catch (error) {
    console.error('Failed to fetch users:', error);
  } finally {
    loading.value = false;
  }
};

const handleEdit = (user) => {
  // Implementation
};

const handleDelete = async (userId) => {
  // Implementation
};

const handleSave = async (userData) => {
  // Implementation
  await fetchUsers();
  showCreateModal.value = false;
};

onMounted(() => {
  fetchUsers();
});
</script>
```

---

## API 實作範本

### 1. DTOs

**`Core.Application/DTOs/UserSummaryDto.cs`** (List 用)

```csharp
namespace Core.Application.DTOs;

public record UserSummaryDto
{
    public string Id { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? UserName { get; init; }
    public bool IsActive { get; init; }
    public List<string> Roles { get; init; } = new();
    public DateTime CreatedAt { get; init; }
}
```

**`Core.Application/DTOs/UserDetailDto.cs`** (詳細資料用)

```csharp
namespace Core.Application.DTOs;

public record UserDetailDto
{
    public string Id { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? UserName { get; init; }
    public string? Name { get; init; }
    public string? Department { get; init; }
    public bool IsActive { get; init; }
    public List<string> Roles { get; init; } = new();
    public Dictionary<string, string> Claims { get; init; } = new();
    public DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
}
```

**`Core.Application/DTOs/CreateUserDto.cs`** (建立用)

```csharp
using System.ComponentModel.DataAnnotations;

namespace Core.Application.DTOs;

public record CreateUserDto
{
    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    [MinLength(6)]
    public string Password { get; init; } = string.Empty;

    public string? UserName { get; init; }
    public string? Name { get; init; }
    public string? Department { get; init; }
    public List<string> Roles { get; init; } = new();
}
```

### 2. Service Interface

**`Core.Application/IUserManagementService.cs`**

```csharp
using Core.Application.DTOs;

namespace Core.Application;

public interface IUserManagementService
{
    Task<PagedUsersDto> GetUsersAsync(int page = 1, int pageSize = 10, string? search = null);
    Task<UserDetailDto?> GetUserByIdAsync(string userId);
    Task<UserDetailDto> CreateUserAsync(CreateUserDto dto);
    Task<UserDetailDto> UpdateUserAsync(string userId, UpdateUserDto dto);
    Task DeleteUserAsync(string userId);
    Task<bool> ActivateUserAsync(string userId);
    Task<bool> DeactivateUserAsync(string userId);
}
```

### 3. Service Implementation

**`Infrastructure/Services/UserManagementService.cs`**

```csharp
using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

public class UserManagementService : IUserManagementService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;

    public UserManagementService(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<PagedUsersDto> GetUsersAsync(int page = 1, int pageSize = 10, string? search = null)
    {
        var query = _userManager.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u => 
                u.Email!.Contains(search) || 
                (u.UserName != null && u.UserName.Contains(search)));
        }

        var totalCount = await query.CountAsync();
        var users = await query
            .OrderBy(u => u.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var userDtos = new List<UserSummaryDto>();
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            userDtos.Add(new UserSummaryDto
            {
                Id = user.Id,
                Email = user.Email!,
                UserName = user.UserName,
                IsActive = user.IsActive,
                Roles = roles.ToList(),
                CreatedAt = user.CreatedAt
            });
        }

        return new PagedUsersDto
        {
            Users = userDtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<UserDetailDto> CreateUserAsync(CreateUserDto dto)
    {
        var user = new ApplicationUser
        {
            Email = dto.Email,
            UserName = dto.UserName ?? dto.Email,
            Name = dto.Name,
            Department = dto.Department,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        if (dto.Roles.Any())
        {
            await _userManager.AddToRolesAsync(user, dto.Roles);
        }

        var roles = await _userManager.GetRolesAsync(user);
        return new UserDetailDto
        {
            Id = user.Id,
            Email = user.Email!,
            UserName = user.UserName,
            Name = user.Name,
            Department = user.Department,
            IsActive = user.IsActive,
            Roles = roles.ToList(),
            CreatedAt = user.CreatedAt
        };
    }

    // ... 其他方法實作
}
```

### 4. API Controller

**`Web.IdP/Api/Admin/UsersController.cs`**

```csharp
using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Web.IdP.Api.Admin;

[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = AuthConstants.Roles.Admin)]
public class UsersController : ControllerBase
{
    private readonly IUserManagementService _userService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        IUserManagementService userService,
        ILogger<UsersController> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<PagedUsersDto>> GetUsers(
        [FromQuery] int page = 1, 
        [FromQuery] int pageSize = 10, 
        [FromQuery] string? search = null)
    {
        try
        {
            var result = await _userService.GetUsersAsync(page, pageSize, search);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching users");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UserDetailDto>> GetUser(string id)
    {
        var user = await _userService.GetUserByIdAsync(id);
        if (user == null)
            return NotFound();

        return Ok(user);
    }

    [HttpPost]
    public async Task<ActionResult<UserDetailDto>> CreateUser([FromBody] CreateUserDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var user = await _userService.CreateUserAsync(dto);
            return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<UserDetailDto>> UpdateUser(
        string id, 
        [FromBody] UpdateUserDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var user = await _userService.UpdateUserAsync(id, dto);
            return Ok(user);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        try
        {
            await _userService.DeleteUserAsync(id);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("{id}/activate")]
    public async Task<IActionResult> ActivateUser(string id)
    {
        var success = await _userService.ActivateUserAsync(id);
        return success ? Ok() : NotFound();
    }

    [HttpPost("{id}/deactivate")]
    public async Task<IActionResult> DeactivateUser(string id)
    {
        var success = await _userService.DeactivateUserAsync(id);
        return success ? Ok() : NotFound();
    }
}
```

---

## UI 實作範本

### Vue 組件範例

#### 1. List Component

**`UserList.vue`**

```vue
<template>
  <div class="card">
    <div class="card-body">
      <div v-if="loading" class="text-center py-5">
        <div class="spinner-border" role="status">
          <span class="visually-hidden">Loading...</span>
        </div>
      </div>

      <div v-else-if="users.length === 0" class="text-center py-5 text-muted">
        <i class="bi bi-inbox display-4"></i>
        <p class="mt-3">No users found</p>
      </div>

      <div v-else class="table-responsive">
        <table class="table table-hover">
          <thead>
            <tr>
              <th>Email</th>
              <th>Name</th>
              <th>Roles</th>
              <th>Status</th>
              <th>Created</th>
              <th class="text-end">Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="user in users" :key="user.id">
              <td>{{ user.email }}</td>
              <td>{{ user.name || '-' }}</td>
              <td>
                <span 
                  v-for="role in user.roles" 
                  :key="role"
                  class="badge bg-primary me-1"
                >
                  {{ role }}
                </span>
              </td>
              <td>
                <span 
                  :class="['badge', user.isActive ? 'bg-success' : 'bg-secondary']"
                >
                  {{ user.isActive ? 'Active' : 'Inactive' }}
                </span>
              </td>
              <td>{{ formatDate(user.createdAt) }}</td>
              <td class="text-end">
                <button 
                  @click="$emit('edit', user)" 
                  class="btn btn-sm btn-outline-primary me-1"
                >
                  <i class="bi bi-pencil"></i>
                </button>
                <button 
                  @click="$emit('delete', user.id)" 
                  class="btn btn-sm btn-outline-danger"
                >
                  <i class="bi bi-trash"></i>
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</template>

<script setup>
defineProps({
  users: Array,
  loading: Boolean
});

defineEmits(['edit', 'delete']);

const formatDate = (dateString) => {
  return new Date(dateString).toLocaleDateString();
};
</script>
```

#### 2. Form Component

**`UserFormModal.vue`**

```vue
<template>
  <div class="modal d-block" tabindex="-1" style="background-color: rgba(0,0,0,0.5)">
    <div class="modal-dialog modal-lg">
      <div class="modal-content">
        <div class="modal-header">
          <h5 class="modal-title">{{ isEdit ? 'Edit User' : 'Create User' }}</h5>
          <button @click="$emit('close')" type="button" class="btn-close"></button>
        </div>
        <form @submit.prevent="handleSubmit">
          <div class="modal-body">
            <div class="mb-3">
              <label class="form-label">Email *</label>
              <input 
                v-model="formData.email" 
                type="email" 
                class="form-control" 
                required
              >
            </div>

            <div class="mb-3">
              <label class="form-label">Password *</label>
              <input 
                v-model="formData.password" 
                type="password" 
                class="form-control" 
                :required="!isEdit"
                minlength="6"
              >
              <small v-if="isEdit" class="text-muted">
                Leave blank to keep current password
              </small>
            </div>

            <div class="mb-3">
              <label class="form-label">Name</label>
              <input 
                v-model="formData.name" 
                type="text" 
                class="form-control"
              >
            </div>

            <div class="mb-3">
              <label class="form-label">Department</label>
              <input 
                v-model="formData.department" 
                type="text" 
                class="form-control"
              >
            </div>

            <div class="mb-3">
              <label class="form-label">Roles</label>
              <div class="form-check">
                <input 
                  v-model="formData.roles" 
                  value="Admin" 
                  type="checkbox" 
                  class="form-check-input"
                  id="roleAdmin"
                >
                <label class="form-check-label" for="roleAdmin">Admin</label>
              </div>
              <div class="form-check">
                <input 
                  v-model="formData.roles" 
                  value="User" 
                  type="checkbox" 
                  class="form-check-input"
                  id="roleUser"
                >
                <label class="form-check-label" for="roleUser">User</label>
              </div>
            </div>

            <div v-if="error" class="alert alert-danger">{{ error }}</div>
          </div>
          <div class="modal-footer">
            <button @click="$emit('close')" type="button" class="btn btn-secondary">
              Cancel
            </button>
            <button type="submit" class="btn btn-primary" :disabled="saving">
              <span v-if="saving" class="spinner-border spinner-border-sm me-1"></span>
              {{ isEdit ? 'Update' : 'Create' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive } from 'vue';

const props = defineProps({
  user: Object,
  isEdit: Boolean
});

const emit = defineEmits(['close', 'save']);

const formData = reactive({
  email: props.user?.email || '',
  password: '',
  name: props.user?.name || '',
  department: props.user?.department || '',
  roles: props.user?.roles || []
});

const saving = ref(false);
const error = ref('');

const handleSubmit = async () => {
  saving.value = true;
  error.value = '';

  try {
    const url = props.isEdit 
      ? `/api/admin/users/${props.user.id}` 
      : '/api/admin/users';
    
    const method = props.isEdit ? 'PUT' : 'POST';

    const response = await fetch(url, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(formData)
    });

    if (!response.ok) {
      const data = await response.json();
      throw new Error(data.error || 'Failed to save user');
    }

    emit('save');
  } catch (err) {
    error.value = err.message;
  } finally {
    saving.value = false;
  }
};
</script>
```

---

## Tailwind CSS 設定

### ⚠️ 每個 Vue SPA 必須執行的步驟

**1. 創建 `style.css`**

在 `ClientApp/src/admin/[feature]/` 目錄下創建：

```css
/* style.css */
@tailwind base;
@tailwind components;
@tailwind utilities;
```

**2. 在 `main.js` 中 import**

```javascript
// main.js
import { createApp } from 'vue';
import './style.css';  // ⚠️ 必須加這行！
import App from './App.vue';

createApp(App).mount('#app');
```

**3. 驗證**

- 瀏覽器 Console 應該看到 `[vite] connected`
- Tailwind 樣式應該正常運作（例如 `class="p-4 bg-blue-500"` 有效果）

### 如果忘記會怎樣？

❌ **沒有 import './style.css'** → 整個排版會跑掉，Tailwind 樣式完全失效

---

## 測試範本

### 1. Unit Test (Service)

**`Tests.Application.UnitTests/UserManagementTests.cs`**

```csharp
using Xunit;
using Moq;
using Core.Application.DTOs;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Core.Domain;

namespace Tests.Application.UnitTests;

public class UserManagementTests
{
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly UserManagementService _service;

    public UserManagementTests()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            store.Object, null, null, null, null, null, null, null, null);

        _service = new UserManagementService(_userManagerMock.Object, null!);
    }

    [Fact]
    public async Task CreateUserAsync_ValidDto_CreatesUser()
    {
        // Arrange
        var dto = new CreateUserDto
        {
            Email = "test@example.com",
            Password = "Test123!",
            Name = "Test User"
        };

        _userManagerMock
            .Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _service.CreateUserAsync(dto);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(dto.Email, result.Email);
        _userManagerMock.Verify(x => x.CreateAsync(
            It.Is<ApplicationUser>(u => u.Email == dto.Email), 
            dto.Password), Times.Once);
    }
}
```

### 2. Integration Test (API)

**`Tests.Infrastructure.IntegrationTests/UsersApiTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using Core.Application.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Tests.Infrastructure.IntegrationTests;

public class UsersApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public UsersApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
        // TODO: Setup authentication token
    }

    [Fact]
    public async Task GetUsers_ReturnsPagedResult()
    {
        // Act
        var response = await _client.GetAsync("/api/admin/users?page=1&pageSize=10");

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PagedUsersDto>();
        Assert.NotNull(result);
        Assert.NotNull(result.Users);
    }

    [Fact]
    public async Task CreateUser_ValidDto_ReturnsCreated()
    {
        // Arrange
        var dto = new CreateUserDto
        {
            Email = "newuser@example.com",
            Password = "Test123!",
            Name = "New User"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/admin/users", dto);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<UserDetailDto>();
        Assert.NotNull(result);
        Assert.Equal(dto.Email, result.Email);
    }
}
```

---

## 常見陷阱

### 1. ❌ 忘記 import Tailwind CSS

**症狀：** 整個排版跑掉，Vue 組件沒有樣式

**原因：** 沒有在 `main.js` 中 import `'./style.css'`

**解決：**

```javascript
// main.js
import './style.css';  // ⚠️ 加這行
```

### 2. ❌ 重複執行 `npm run dev`

**症狀：** Port 衝突錯誤

**原因：** Vite dev server 已經在運行

**解決：**

```bash
# 檢查 Vite 是否運行
# 瀏覽器訪問 http://localhost:5173

# 如果需要重啟
# Ctrl+C 停止 → npm run dev
```

### 3. ❌ 在開發時執行 `npm run build`

**症狀：** 開發流程中斷，HMR 失效

**原因：** Build 是用於生產環境

**解決：** 開發時只用 `npm run dev`，不要執行 build

### 4. ❌ API 路徑錯誤

**症狀：** 404 Not Found

**原因：** API endpoint 路徑不正確

**解決：** 確認 controller route: `[Route("api/admin/users")]`

### 5. ❌ 忘記 `[Authorize]` 屬性

**症狀：** 未授權用戶可以訪問 admin 功能

**原因：** Razor Page 或 API Controller 沒有加授權檢查

**解決：**

```csharp
[Authorize(Roles = AuthConstants.Roles.Admin)]
public class UsersController : ControllerBase { }
```

### 6. ❌ DTO Validation 不完整

**症狀：** 無效資料進入資料庫

**原因：** 缺少 `[Required]`, `[EmailAddress]` 等驗證屬性

**解決：**

```csharp
public record CreateUserDto
{
    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    [MinLength(6)]
    public string Password { get; init; } = string.Empty;
}
```

### 7. ❌ 未處理錯誤

**症狀：** 500 Internal Server Error，沒有錯誤訊息

**原因：** API Controller 沒有 try-catch

**解決：**

```csharp
[HttpPost]
public async Task<ActionResult> CreateUser([FromBody] CreateUserDto dto)
{
    try
    {
        var result = await _service.CreateUserAsync(dto);
        return CreatedAtAction(nameof(GetUser), new { id = result.Id }, result);
    }
    catch (InvalidOperationException ex)
    {
        return BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error creating user");
        return StatusCode(500, "Internal server error");
    }
}
```

---

## 參考資料

- **完整需求：** `idp_req_details.md`
- **工作流程：** `WORKFLOW.md`
- **測試指南：** `dev_testing_guide.md`
- **進度追蹤：** `progress_completed.md`, `progress_todo.md`

---

**記住：遵循這些範本和最佳實踐，可以確保程式碼品質和一致性！** 🚀
