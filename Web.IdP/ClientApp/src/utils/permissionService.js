/**
 * Permission Service
 * Manages user permissions and checks authorization for UI elements
 */

class PermissionService {
  constructor() {
    this.permissions = [];
    this.isAdmin = false;
    this.loaded = false;
  }

  /**
   * Load current user's permissions from the server
   */
  async loadPermissions() {
    try {
      const response = await fetch('/api/admin/permissions/current', {
        credentials: 'include'
      });
      
      if (response.ok) {
        const data = await response.json();
        this.permissions = data.permissions || [];
        this.isAdmin = data.isAdmin || false;
        this.loaded = true;
        console.log('Permissions loaded:', this.permissions);
        return true;
      } else {
        console.warn('Failed to load permissions:', response.status);
        this.permissions = [];
        this.isAdmin = false;
        this.loaded = true;
        return false;
      }
    } catch (error) {
      console.error('Error loading permissions:', error);
      this.permissions = [];
      this.isAdmin = false;
      this.loaded = true;
      return false;
    }
  }

  /**
   * Check if user has a specific permission
   * @param {string} permission - Permission to check (e.g., 'users.read')
   * @returns {boolean}
   */
  hasPermission(permission) {
    // Admin has all permissions
    if (this.isAdmin) return true;
    
    // Check if user has the specific permission
    return this.permissions.includes(permission);
  }

  /**
   * Check if user has ANY of the specified permissions
   * @param {string[]} permissions - Array of permissions to check
   * @returns {boolean}
   */
  hasAnyPermission(permissions) {
    if (this.isAdmin) return true;
    return permissions.some(p => this.permissions.includes(p));
  }

  /**
   * Check if user has ALL of the specified permissions
   * @param {string[]} permissions - Array of permissions to check
   * @returns {boolean}
   */
  hasAllPermissions(permissions) {
    if (this.isAdmin) return true;
    return permissions.every(p => this.permissions.includes(p));
  }

  /**
   * Get all user permissions
   * @returns {string[]}
   */
  getAllPermissions() {
    return [...this.permissions];
  }

  /**
   * Check if permissions are loaded
   * @returns {boolean}
   */
  isLoaded() {
    return this.loaded;
  }

  /**
   * Clear cached permissions
   */
  clear() {
    this.permissions = [];
    this.isAdmin = false;
    this.loaded = false;
  }
}

// Create singleton instance
const permissionService = new PermissionService();

// Permission constants (should match backend)
export const Permissions = {
  Clients: {
    Read: 'clients.read',
    Create: 'clients.create',
    Update: 'clients.update',
    Delete: 'clients.delete'
  },
  Scopes: {
    Read: 'scopes.read',
    Create: 'scopes.create',
    Update: 'scopes.update',
    Delete: 'scopes.delete'
  },
  Users: {
    Read: 'users.read',
    Create: 'users.create',
    Update: 'users.update',
    Delete: 'users.delete'
  },
  Roles: {
    Read: 'roles.read',
    Create: 'roles.create',
    Update: 'roles.update',
    Delete: 'roles.delete'
  },
  Persons: {
    Read: 'persons.read',
    Create: 'persons.create',
    Update: 'persons.update',
    Delete: 'persons.delete'
  },
  Audit: {
    Read: 'audit.read'
  },
  Settings: {
    Read: 'settings.read',
    Update: 'settings.update'
  }
};

// Export as default
export default permissionService;
