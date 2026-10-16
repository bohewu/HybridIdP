async function request(method, csrfToken = '') {
  const response = await fetch('/api/account/recovery-codes', {
    method,
    credentials: 'include',
    cache: 'no-store',
    headers: method === 'POST' ? { 'X-XSRF-TOKEN': csrfToken } : {}
  });
  const result = await response.json().catch(() => ({}));
  return { ...result, httpOk: response.ok, httpStatus: response.status };
}

export const accountRecoveryCodesApi = {
  getStatus: () => request('GET'),
  generate: (csrfToken) => request('POST', csrfToken)
};
