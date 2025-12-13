import { chromium } from 'playwright';
import fs from 'fs';
import path from 'path';

// Path to store authenticated session state for reuse by tests
const STORAGE_STATE = path.join(__dirname, '.auth', 'admin.json');

// Playwright global setup: authenticate as admin and save storage state for reuse by tests.
// Test clients and scopes are handled by DataSeeder.cs on backend startup.
export default async function globalSetup() {
  const baseUrl = 'https://localhost:7035';
  const adminEmail = 'admin@hybridauth.local';
  const adminPassword = 'Admin@123';

  // Ensure .auth directory exists
  const authDir = path.dirname(STORAGE_STATE);
  if (!fs.existsSync(authDir)) {
    fs.mkdirSync(authDir, { recursive: true });
  }

  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ ignoreHTTPSErrors: true });
  const page = await context.newPage();

  // helper: wait for the Admin API health endpoint to be reachable before proceeding
  async function waitForAdminHealth(timeout = 120_000, interval = 2_000) {
    const deadline = Date.now() + timeout;
    while (Date.now() < deadline) {
      try {
        const resp = await page.goto(`${baseUrl}/api/admin/health`, { waitUntil: 'networkidle', timeout: 5000 }).catch(() => null);
        // Accept 200 = healthy, or 401/403 = service reachable but auth-protected
        if (resp && (resp.status() === 200 || resp.status() === 401 || resp.status() === 403)) {
          return true;
        }
      } catch (err) {
        // ignore and retry
      }
      await new Promise((r) => setTimeout(r, interval));
    }
    return false;
  }

  try {
    // Wait for the admin API to be up and healthy
    const ok = await waitForAdminHealth().catch(() => false);
    if (!ok) throw new Error(`globalSetup: admin API ${baseUrl}/api/admin/health did not become healthy in time`);

    // Login as admin
    await page.goto(`${baseUrl}/Account/Login`);
    await page.fill('#Input_Login', adminEmail);
    await page.fill('#Input_Password', adminPassword);
    await page.click('button.auth-btn-primary');
    await page.waitForSelector('.user-name, .user-info-name', { timeout: 20000 });

    // Save storage state for reuse by tests (cookies, localStorage)
    await context.storageState({ path: STORAGE_STATE });
    console.log(`[global-setup] Auth state saved to ${STORAGE_STATE}`);

  } catch (e) {
    console.error('globalSetup failed:', e);
    // Swallow - global setup should not crash the run abruptly in dev environments
  } finally {
    await context.close();
    await browser.close();
  }
}

