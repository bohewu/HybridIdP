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
  }
};
