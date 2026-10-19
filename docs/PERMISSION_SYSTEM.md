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

These permissions authorize the human/admin caller managing clients. They are
separate from an OAuth application's endpoint, grant, response-type and scope
permissions. See [client settings and protocol permissions](OAUTH_FLOWS.md#client-settings-and-protocol-permissions)
for public/confidential, PKCE, Basic authentication and authorization-code setup.

In the client editor, allowed scopes and Required are separate settings.
Required means that users cannot deselect a scope **when the client actually
requests it**; it does not add scopes to requests, grants or existing tokens.
After saving, reopen the client to verify both settings. The required-scopes
API uses the internal application ID (`/api/admin/clients/{id}/required-scopes`),
which is distinct from the public OAuth `client_id`; existing update and
ownership checks still apply.

If a client needs display names, allow `profile` and update the client to request
it, then obtain consent under its configured policy and fresh tokens. Marking
`profile` Required alone does not supply names. Scope permission and user consent
also do not replace API resource usage approval. See
[OAuth consent and upgrade guidance](OAUTH_FLOWS.md#requested-scopes-required-and-consent)
for grant reuse, refresh, older registrations and revocation behavior.

### Claims preview

`Permissions.Claims.Preview` resolves to `claims.preview`. This dedicated
permission protects both the application-cookie Razor page `/Admin/ClaimsPreview`
and `/api/admin/claims-preview`, including its bounded client/account/scope
lookups. The navigation entry uses the same permission. No unrelated client,
account or claim-management permission is required for these preview lookups.
The Admin permission registry includes it; ApplicationManager is not granted it
automatically. Ordinary OAuth bearer tokens cannot authorize the preview.

POST requires antiforgery and returns no-store typed JSON, never a token.
The selected scopes are hypothetical and still subject to current client and
resource usage checks. Preview does not create consent or authorization and
does not authenticate the selected account. `ClaimsPreviewed` records the actor,
target account, client ID, scope/source counts and hypothetical flag without
claim values. Profile data comes only from eligible confirmed cached snapshots;
no refresh or snapshot write is performed.

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

Person account linking/unlinking also requires `Permissions.Users.Update`, even
for an empty Person. If account roles are involved, `Permissions.Roles.Update`
and the configured protected-role MFA checks also apply. This does not change
ordinary Person profile CRUD permissions.

Person links/unlinks and asset transfers additionally require the update
permission for every asset domain present (`Clients`, `Scopes`, `ApiResources`)
and exact source Person ownership, with the full IdP Admin exception. No
permission is required for an absent asset domain. All checks finish before
any association, role or ownership write; a denied transfer changes no owner.

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
