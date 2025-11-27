import { chromium } from 'playwright';

// Playwright global setup to ensure the canonical testclient exists before any tests run.
export default async function globalSetup() {
  const baseUrl = 'https://localhost:7035';
  const adminEmail = 'admin@hybridauth.local';
  const adminPassword = 'Admin@123';

  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ ignoreHTTPSErrors: true });
  const page = await context.newPage();

  // helper: wait for the Admin API health endpoint to be reachable before proceeding
  async function waitForAdminHealth(timeout = 120_000, interval = 2_000) {
    const deadline = Date.now() + timeout;
    while (Date.now() < deadline) {
      try {
        // try the admin health endpoint — use a short navigation timeout so we can retry quickly
        const resp = await page.goto(`${baseUrl}/api/admin/health`, { waitUntil: 'networkidle', timeout: 5000 }).catch(() => null);
        // Accept 200 = healthy, but also treat 401/403 as the service being reachable (auth-protected API)
        if (resp && (resp.status() === 200 || resp.status() === 401 || resp.status() === 403)) {
          try {
            // attempt parse - some environments might return simple text
            const json = await resp.json().catch(() => null);
            if (!json || json.status === 'healthy') return true;
            // If json exists but doesn't indicate 'healthy', it's still OK to consider the instance available
            // if the status code indicates the service is reachable and protected.
            if (resp.status() === 401 || resp.status() === 403) return true;
          } catch {
            return true; // 200 OK is sufficient if we can't parse JSON
          }
        }
      } catch (err) {
        // ignore and retry
      }
      await new Promise((r) => setTimeout(r, interval));
    }
    return false;
  }

  try {
    // Wait for the admin API to be up and healthy; fail fast with informative log otherwise
    const ok = await waitForAdminHealth().catch(() => false);
    if (!ok) throw new Error(`globalSetup: admin API ${baseUrl}/api/admin/health did not become healthy in time`);

    await page.goto(`${baseUrl}/Account/Login`);
    await page.fill('#Input_Login', adminEmail);
    await page.fill('#Input_Password', adminPassword);
    await page.click('button.auth-btn-primary');
    await page.waitForSelector('.user-name', { timeout: 20000 });

    // Find existing testclient-public
    const client = await page.evaluate(async () => {
      const r = await fetch('/api/admin/clients?search=testclient-public&take=100');
      if (!r.ok) return null;
      const json = await r.json();
      const items = Array.isArray(json) ? json : (json.items || []);
      return items.find((c: any) => c.clientId === 'testclient-public') || null;
    });

    if (client && client.id) {
      // Delete existing
      await page.evaluate(async (id) => {
        await fetch(`/api/admin/clients/${id}`, { method: 'DELETE' });
      }, client.id);
    }

    // Create canonical testclient-public
    await page.evaluate(async () => {
      const payload = {
        clientId: 'testclient-public',
        clientSecret: null,
        displayName: 'Test Client (Public)',
        applicationType: 'web',
        type: 'public',
        consentType: 'explicit',
        redirectUris: ['https://localhost:7001/signin-oidc'],
        postLogoutRedirectUris: ['https://localhost:7001/signout-callback-oidc'],
        permissions: ['ept:authorization','ept:token','ept:logout','gt:authorization_code','gt:refresh_token','response_type:code','scp:openid','scp:profile','scp:email','scp:roles','scp:api:company:read','scp:api:inventory:read']
      };
      await fetch('/api/admin/clients', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
    });

    // Optionally create necessary API scopes if they don't exist
    const requiredScopes = ['api:company:read', 'api:inventory:read'];
    for (const s of requiredScopes) {
      await page.evaluate(async (name) => {
        const res = await fetch(`/api/admin/scopes?search=${encodeURIComponent(name)}`);
        if (res.ok) {
          const json = await res.json();
          const items = Array.isArray(json) ? json : (json.items || []);
          if (items.some((i: any) => i.name === name)) return;
        }
        // create minimal scope
        await fetch('/api/admin/scopes', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name, displayName: name, description: 'Auto-created by global-setup', resources: [] }) });
      }, s).catch(() => {});
    }

  } catch (e) {
    console.error('globalSetup failed to recreate testclient:', e);
    // Swallow - global setup should not crash the run abruptly in dev environments
  } finally {
    await context.close();
    await browser.close();
  }
}
