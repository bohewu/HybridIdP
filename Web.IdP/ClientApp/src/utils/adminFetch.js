/**
 * Admin fetch utility that automatically includes CSRF token for mutating requests.
 * Reads the token from the meta tag in _AdminLayout.cshtml.
 */

let csrfToken = null

/**
 * Get CSRF token from meta tag (cached after first read)
 */
export function getCsrfToken() {
  if (csrfToken === null) {
    const meta = document.querySelector('meta[name="csrf-token"]')
    csrfToken = meta?.content || ''
  }
  return csrfToken
}

/**
 * Wrapper around fetch that automatically includes CSRF token for POST/PUT/DELETE/PATCH requests.
 * 
 * @param {string} url - The URL to fetch
 * @param {RequestInit} options - Fetch options
 * @returns {Promise<Response>}
 */
export async function adminFetch(url, options = {}) {
  const method = (options.method || 'GET').toUpperCase()
  
  // Only add CSRF token for mutating methods
  if (['POST', 'PUT', 'DELETE', 'PATCH'].includes(method)) {
    const token = getCsrfToken()
    if (token) {
      options.headers = {
        ...options.headers,
        'X-XSRF-TOKEN': token
      }
    }
  }
  
  // Always include credentials for cookie auth
  options.credentials = options.credentials || 'include'
  
  return fetch(url, options)
}

/**
 * Convenience methods for common HTTP verbs
 */
export const adminApi = {
  get: (url, options = {}) => adminFetch(url, { ...options, method: 'GET' }),
  
  post: (url, data, options = {}) => adminFetch(url, {
    ...options,
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...options.headers },
    body: JSON.stringify(data)
  }),
  
  put: (url, data, options = {}) => adminFetch(url, {
    ...options,
    method: 'PUT',
    headers: { 'Content-Type': 'application/json', ...options.headers },
    body: JSON.stringify(data)
  }),
  
  delete: (url, options = {}) => adminFetch(url, { ...options, method: 'DELETE' }),
  
  patch: (url, data, options = {}) => adminFetch(url, {
    ...options,
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json', ...options.headers },
    body: JSON.stringify(data)
  })
}

export default adminFetch
