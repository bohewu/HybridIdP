// Helper to get CSRF token
function getCsrfToken() {
  return document.querySelector('input[name="__RequestVerificationToken"]')?.getAttribute('value');
}

// Helper to create fetch options with CSRF token
function createFetchOptions(method = 'GET', body = null) {
  const options = {
    method,
    headers: {
      'Content-Type': 'application/json'
    }
  };

  const token = getCsrfToken();
  if (token) {
    options.headers['RequestVerificationToken'] = token;
  }

  if (body) {
    options.body = JSON.stringify(body);
  }

  return options;
}

async function recoveryRequest(path = '', method = 'GET', body = null, csrfToken = '') {
  const headers = {};
  if (body) headers['Content-Type'] = 'application/json';
  if (csrfToken) headers['X-XSRF-TOKEN'] = csrfToken;

  const response = await fetch(`/api/account/recovery-email${path}`, {
    method,
    headers,
    credentials: 'include',
    ...(body ? { body: JSON.stringify(body) } : {})
  });
  const data = await response.json().catch(() => ({}));
  return { ...data, httpOk: response.ok, httpStatus: response.status };
}

export const accountApi = {
  async getMyRoles() {
    const response = await fetch('/api/my/roles', createFetchOptions());
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`);
    }
    return await response.json();
  },

  async getMyAccounts() {
    const response = await fetch('/api/my/accounts', createFetchOptions());
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`);
    }
    return await response.json();
  },

  async switchRole(roleId, password) {
    const request = {
      roleId,
      password
    };
    const response = await fetch('/api/my/switch-role', createFetchOptions('POST', request));
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`);
    }
    return await response.json();
  },

  async switchAccount(targetAccountId, reason) {
    const request = {
      targetAccountId,
      reason
    };
    const response = await fetch('/api/my/switch-account', createFetchOptions('POST', request));
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`);
    }
    return await response.json();
  },

  async getRecoveryEmailStatus(csrfToken = '') {
    return recoveryRequest('', 'GET', null, csrfToken);
  },

  async changeRecoveryEmail(candidateAddress, csrfToken = '') {
    return recoveryRequest('/change', 'POST', { candidateAddress }, csrfToken);
  },

  async verifyRecoveryEmail(code, csrfToken = '') {
    return recoveryRequest('/verify', 'POST', { code }, csrfToken);
  },

  async revokeRecoveryEmail(csrfToken = '') {
    return recoveryRequest('', 'DELETE', null, csrfToken);
  }
};
