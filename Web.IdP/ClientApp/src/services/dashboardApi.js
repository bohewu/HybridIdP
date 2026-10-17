async function request(resource) {
  const response = await fetch(`/api/admin/dashboard/${resource}`, {
    credentials: 'include',
    cache: 'no-store'
  })
  if (!response.ok) {
    throw Object.assign(new Error('Dashboard request failed'), { status: response.status })
  }
  return response.json()
}

export const dashboardApi = {
  getStats: () => request('stats'),
  async getHealth() {
    const data = await request('health')
    const statuses = ['Healthy', 'Degraded', 'Unhealthy']
    const checks = ['sqlserver', 'postgresql', 'redis']
    if (!statuses.includes(data.status) || !data.checkedAtUtc ||
        !Number.isFinite(Date.parse(data.checkedAtUtc)) || !data.entries) {
      throw new Error('Invalid dashboard health response')
    }
    const entries = {}
    for (const name of checks) {
      const entry = data.entries[name]
      if (entry === undefined) continue
      if (!entry || !statuses.includes(entry.status)) {
        throw new Error('Invalid dashboard health status')
      }
      entries[name] = { status: entry.status }
    }
    return { status: data.status, checkedAtUtc: data.checkedAtUtc, entries }
  }
}
