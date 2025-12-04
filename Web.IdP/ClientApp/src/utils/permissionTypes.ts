/**
 * Permission constants type definitions
 * This ensures IDE autocomplete and type checking for permission constants
 */

export interface PermissionCategory {
  Read: string;
  Create?: string;
  Update?: string;
  Delete?: string;
}

export interface PermissionsType {
  Clients: PermissionCategory;
  Scopes: PermissionCategory;
  Users: PermissionCategory;
  Roles: PermissionCategory;
  Persons: PermissionCategory;
  Audit: {
    Read: string;
  };
  Settings: {
    Read: string;
    Update: string;
  };
}

// Re-export the actual Permissions object with type
export { Permissions } from './permissionService.js';
export type { PermissionsType as Permissions };
