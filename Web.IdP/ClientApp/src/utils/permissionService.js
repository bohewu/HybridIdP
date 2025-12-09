/**
 * Permission Service
 * Manages user permissions and checks authorization for UI elements
 */

class PermissionService {
  constructor() {
    this.permissions = [];
    this.isAdmin = false;
    this.loaded = false;
    this.lastLoadTime = null;
    this.cacheTimeoutMs = 5 * 60 * 1000; // 5 minutes cache
  }

  /**
   * Load current user's permissions from the server
   * @param {boolean} forceReload - Force reload even if cached
   */
  async loadPermissions(forceReload = false) {
    // Check if we can use cached permissions
    if (!forceReload && this.loaded && this.lastLoadTime) {
      const now = Date.now();
      const cacheAge = now - this.lastLoadTime;
      if (cacheAge < this.cacheTimeoutMs) {
        console.log('Using cached permissions (age: ' + Math.round(cacheAge / 1000) + 's)');
        return true;
      }
    }

    try {
      const response = await fetch('/api/admin/permissions/current', {
        credentials: 'include',
        cache: 'no-store' // Don't cache the response
      });
      
      if (response.ok) {
        const data = await response.json();
        this.permissions = data.permissions || [];
        this.isAdmin = data.isAdmin || false;
        this.userId = data.userId;
        this.loaded = true;
        this.lastLoadTime = Date.now();
        console.log('Permissions loaded:', this.permissions, 'User:', this.userId);
        return true;
      } else {
        console.warn('Failed to load permissions:', response.status);
        this.permissions = [];
        this.isAdmin = false;
        this.userId = null;
        this.loaded = true;
        this.lastLoadTime = Date.now();
        return false;
      }
    } catch (error) {
      console.error('Error loading permissions:', error);
      this.permissions = [];
      this.isAdmin = false;
      this.loaded = true;
      this.lastLoadTime = Date.now();
      return false;
    }
  }

  /**
   * Reload permissions from server (force refresh cache)
   * Use this after login or role changes
   */
  async reloadPermissions() {
    console.log('Reloading permissions (forced)...');
    return await this.loadPermissions(true);
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
   * Use this on logout or when user session ends
   */
  clear() {
    console.log('Clearing permissions cache...');
    this.permissions = [];
    this.isAdmin = false;
    this.userId = null;
    this.loaded = false;
    this.lastLoadTime = null;
  }

  /**
   * Check if cache is stale and should be refreshed
   * @returns {boolean}
   */
  isCacheStale() {
    if (!this.loaded || !this.lastLoadTime) return true;
    const now = Date.now();
    const cacheAge = now - this.lastLoadTime;
    return cacheAge >= this.cacheTimeoutMs;
  }
}

// Create singleton instance
const permissionService = new PermissionService();

/**
 * Permission constants (MUST match backend)
 * 
 * ⚠️ IMPORTANT: Use PascalCase keys, NOT UPPERCASE!
 * ✅ Correct: Permissions.Clients.Read
 * ❌ Wrong:   Permissions.Clients.READ
 * 
 * The values are lowercase to match backend format: "clients.read"
 * 
 * @type {Object.<string, Object.<string, string>>}
 */
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
