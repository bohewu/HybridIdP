# Permission System - Developer Guide

## ⚠️ CRITICAL: Permission Constant Naming Convention

### The Golden Rule
**Always use PascalCase for permission keys, they will resolve to lowercase values.**

### ✅ Correct Usage
```javascript
import permissionService, { Permissions } from '@/utils/permissionService'

// Checking permissions
if (permissionService.hasPermission(Permissions.Clients.Read)) {
  // User can read clients
}

if (permissionService.hasPermission(Permissions.Users.Create)) {
  // User can create users
}
```

### ❌ Wrong Usage (WILL BREAK!)
```javascript
// ❌ NEVER use uppercase - this will be undefined!
Permissions.Clients.READ     // undefined - WRONG!
Permissions.Users.CREATE     // undefined - WRONG!

// ❌ NEVER use lowercase keys
Permissions.Clients.read     // undefined - WRONG!
```

## Why This Matters

The backend returns permissions as lowercase strings:
```json
{
  "permissions": ["clients.read", "clients.create", "users.read"],
  "isAdmin": false
}
```

Our frontend constants map PascalCase keys to lowercase values:
```javascript
export const Permissions = {
  Clients: {
    Read: 'clients.read',    // ✅ PascalCase key → lowercase value
    Create: 'clients.create',
    Update: 'clients.update',
    Delete: 'clients.delete'
  }
}
```

When you write `Permissions.Clients.Read`, it evaluates to `'clients.read'` which matches the backend.

When you write `Permissions.Clients.READ`, it evaluates to `undefined` which never matches.

## Available Permissions

### Clients
- `Permissions.Clients.Read` → `'clients.read'`
- `Permissions.Clients.Create` → `'clients.create'`
- `Permissions.Clients.Update` → `'clients.update'`
- `Permissions.Clients.Delete` → `'clients.delete'`

### Scopes
- `Permissions.Scopes.Read` → `'scopes.read'`
- `Permissions.Scopes.Create` → `'scopes.create'`
- `Permissions.Scopes.Update` → `'scopes.update'`
- `Permissions.Scopes.Delete` → `'scopes.delete'`

### Users
- `Permissions.Users.Read` → `'users.read'`
- `Permissions.Users.Create` → `'users.create'`
- `Permissions.Users.Update` → `'users.update'`
- `Permissions.Users.Delete` → `'users.delete'`

### Roles
- `Permissions.Roles.Read` → `'roles.read'`
- `Permissions.Roles.Create` → `'roles.create'`
- `Permissions.Roles.Update` → `'roles.update'`
- `Permissions.Roles.Delete` → `'roles.delete'`

### Persons
- `Permissions.Persons.Read` → `'persons.read'`
- `Permissions.Persons.Create` → `'persons.create'`
- `Permissions.Persons.Update` → `'persons.update'`
- `Permissions.Persons.Delete` → `'persons.delete'`

### Audit
- `Permissions.Audit.Read` → `'audit.read'`

### Settings
- `Permissions.Settings.Read` → `'settings.read'`
- `Permissions.Settings.Update` → `'settings.update'`

## Common Patterns

### In Vue Components
```vue
<script setup>
import { ref, onMounted } from 'vue'
import permissionService, { Permissions } from '@/utils/permissionService'

const canCreate = ref(false)
const canUpdate = ref(false)
const canDelete = ref(false)
const canRead = ref(false)

onMounted(async () => {
  // Force reload permissions on mount
  await permissionService.reloadPermissions()
  
  // Check permissions using PascalCase
  canRead.value = permissionService.hasPermission(Permissions.Clients.Read)
  canCreate.value = permissionService.hasPermission(Permissions.Clients.Create)
  canUpdate.value = permissionService.hasPermission(Permissions.Clients.Update)
  canDelete.value = permissionService.hasPermission(Permissions.Clients.Delete)
})
</script>
```

### Error Handling
```javascript
if (!canCreate.value) {
  deniedMessage.value = 'You do not have permission to create clients'
  deniedPermission.value = Permissions.Clients.Create  // PascalCase!
  showAccessDenied.value = true
  return
}
```

## Testing Your Code

Run the permission constant tests:
```bash
npm run test -- permissionService.test.js
```

This will verify:
- All permission values are lowercase
- Keys use PascalCase (not UPPERCASE)
- Format matches backend expectations

## Troubleshooting

### "Access Denied" appearing when it shouldn't?
1. Check the browser console - you'll see "Permissions loaded: [...]"
2. Verify the permission constant is using **PascalCase** not UPPERCASE
3. Check the import statement includes `{ Permissions }`
4. Try force reload: `await permissionService.reloadPermissions()`

### How to add a new permission?
1. Add to backend `Permissions.cs` constants
2. Add to role's permission string in `DataSeeder.cs`
3. Add to frontend `permissionService.js` Permissions object (PascalCase!)
4. Update this documentation
5. Add test case to `permissionService.test.js`

## Prevention Measures

We have the following safeguards in place:

1. **ESLint Rule**: Catches uppercase usage at lint time
2. **Unit Tests**: Verifies constant structure
3. **JSDoc Comments**: IDE hints about correct usage
4. **This Documentation**: Reference guide for developers

When in doubt, remember: **PascalCase keys, lowercase values!**
