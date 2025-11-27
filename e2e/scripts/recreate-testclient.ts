import { chromium } from 'playwright';

// Simple script to recreate testclient-public using Playwright to authenticate and call Admin API
// Usage: npx ts-node e2e/scripts/recreate-testclient.ts

async function main() {
  const baseUrl = 'https://localhost:7035';
  const adminEmail = 'admin@hybridauth.local';
  const adminPassword = 'Admin@123';

  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ ignoreHTTPSErrors: true });
  const page = await context.newPage();

  try {
    await page.goto(`${baseUrl}/Account/Login`);
    await page.fill('#Input_Login', adminEmail);
    await page.fill('#Input_Password', adminPassword);
    await page.click('button.auth-btn-primary');
    await page.waitForSelector('.user-name', { timeout: 20000 });

    console.log('Logged in as admin. Searching for existing testclient-public...');
    const client = await page.evaluate(async () => {
      const r = await fetch('/api/admin/clients?search=testclient-public&take=100');
      if (!r.ok) return null;
      const json = await r.json();
      const items = Array.isArray(json) ? json : (json.items || []);
      return items.find((c: any) => c.clientId === 'testclient-public') || null;
    });

    if (client && client.id) {
      console.log(`Found existing testclient-public (id=${client.id}), deleting...`);
      const details = await page.evaluate(async (id) => {
        const r = await fetch(`/api/admin/clients/${id}`);
        return (r.ok ? r.json() : null);
      }, client.id);
      console.log('Client backup (partial):', JSON.stringify({ id: details?.id, clientId: details?.clientId }).slice(0, 400));
      await page.evaluate(async (id) => {
        await fetch(`/api/admin/clients/${id}`, { method: 'DELETE' });
      }, client.id);
      console.log('Deleted existing client');
    }

    console.log('Creating fresh testclient-public with canonical permissions...');
    const createResp = await page.evaluate(async () => {
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
      const r = await fetch('/api/admin/clients', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
      if (!r.ok) throw new Error('Create failed: ' + r.status);
      return r.json();
    });

    console.log('Created client:', createResp?.id || '(no id returned)');
  } catch (err) {
    console.error('Failed during recreate testclient:', err);
    process.exitCode = 1;
  } finally {
    await context.close();
    await browser.close();
  }
}

main();
