import { ref, onMounted } from 'vue'

/**
 * Composable for handling CSRF tokens in Vue apps.
 * Reads the token from a data attribute on the mount element.
 * 
 * Usage:
 * const { csrfToken, csrfHeaders } = useCsrfToken('profile-app')
 * 
 * // Then include in fetch requests:
 * fetch('/api/profile', { 
 *   method: 'PUT',
 *   headers: { ...csrfHeaders.value, 'Content-Type': 'application/json' },
 *   body: JSON.stringify(data)
 * })
 */
export function useCsrfToken(mountElementId) {
  const csrfToken = ref('')

  onMounted(() => {
    const mountEl = document.getElementById(mountElementId)
    if (mountEl?.dataset?.csrfToken) {
      csrfToken.value = mountEl.dataset.csrfToken
    }
  })

  /**
   * Returns headers object with CSRF token included
   */
  const csrfHeaders = {
    get value() {
      if (!csrfToken.value) return {}
      return { 'X-XSRF-TOKEN': csrfToken.value }
    }
  }

  /**
   * Wrapper function for fetch that includes CSRF token in headers
   */
  async function fetchWithCsrf(url, options = {}) {
    const headers = {
      ...options.headers,
      ...(csrfToken.value ? { 'X-XSRF-TOKEN': csrfToken.value } : {})
    }
    
    return fetch(url, {
      ...options,
      headers,
      credentials: 'include'
    })
  }

  return {
    csrfToken,
    csrfHeaders,
    fetchWithCsrf
  }
}

export default useCsrfToken
