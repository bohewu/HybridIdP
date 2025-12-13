import { describe, it, expect } from 'vitest'
import { Permissions } from './permissionService'

describe('Permission Constants', () => {
  it('should have lowercase permission values', () => {
    // Verify all permission values are lowercase with dots
    const allPermissions = [
      Permissions.Clients.Read,
      Permissions.Clients.Create,
      Permissions.Clients.Update,
      Permissions.Clients.Delete,
      Permissions.Scopes.Read,
      Permissions.Scopes.Create,
      Permissions.Scopes.Update,
      Permissions.Scopes.Delete,
      Permissions.Users.Read,
      Permissions.Users.Create,
      Permissions.Users.Update,
      Permissions.Users.Delete,
      Permissions.Roles.Read,
      Permissions.Roles.Create,
      Permissions.Roles.Update,
      Permissions.Roles.Delete,
      Permissions.Persons.Read,
      Permissions.Persons.Create,
      Permissions.Persons.Update,
      Permissions.Persons.Delete,
      Permissions.Audit.Read,
      Permissions.Settings.Read,
      Permissions.Settings.Update
    ]

    allPermissions.forEach(permission => {
      expect(permission).toMatch(/^[a-z]+\.[a-z]+$/)
      expect(permission).toBe(permission.toLowerCase())
    })
  })

  it('should use PascalCase keys', () => {
    // Verify the structure uses PascalCase keys
    expect(Permissions.Clients).toBeDefined()
    expect(Permissions.Clients.Read).toBe('clients.read')
    expect(Permissions.Clients.Create).toBe('clients.create')

    // These should NOT exist (would be undefined)
    expect(Permissions.Clients.READ).toBeUndefined()
    expect(Permissions.Clients.CREATE).toBeUndefined()
  })

  it('should match backend permission format', () => {
    // Verify format matches backend: <entity>.<action>
    expect(Permissions.Clients.Read).toBe('clients.read')
    expect(Permissions.Users.Create).toBe('users.create')
    expect(Permissions.Roles.Update).toBe('roles.update')
    expect(Permissions.Scopes.Delete).toBe('scopes.delete')
  })
})
