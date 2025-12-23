/**
 * Global Fetch CSRF Interceptor
 * 
 * Automatically adds X-XSRF-TOKEN header to all mutating fetch requests (POST/PUT/DELETE/PATCH).
 * Import this file early in your application entry point to enable automatic CSRF protection.
 * 
 * The CSRF token is read from <meta name="csrf-token"> which is set by the server-side layout.
 */

const originalFetch = window.fetch

/**
 * Get CSRF token from meta tag
 */
function getCsrfToken() {
  const meta = document.querySelector('meta[name="csrf-token"]')
  return meta?.content || ''
}

/**
 * Enhanced fetch that automatically includes CSRF token for mutating requests
 */
window.fetch = function(url, options = {}) {
  const method = (options.method || 'GET').toUpperCase()
  
  // Only add CSRF token for mutating methods
  if (['POST', 'PUT', 'DELETE', 'PATCH'].includes(method)) {
    const token = getCsrfToken()
    if (token) {
      // Merge CSRF header with existing headers
      options.headers = {
        ...options.headers,
        'X-XSRF-TOKEN': token
      }
    }
  }
  
  // Always include credentials for cookie auth
  if (!options.credentials) {
    options.credentials = 'include'
  }
  
  return originalFetch.call(window, url, options)
}

// Export for explicit use if needed
export { getCsrfToken }
export default getCsrfToken
