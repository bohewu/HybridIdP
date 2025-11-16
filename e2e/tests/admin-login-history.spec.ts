import { test, expect, Page } from '@playwright/test';

const IDP_BASE = 'https://localhost:7035';
const ADMIN_EMAIL = 'admin@hybridauth.local';
const ADMIN_PASSWORD = 'Admin@123';

async function adminLogin(page: Page) {
  await page.goto(`${IDP_BASE}/Identity/Account/Login`).catch(() => {});
  if (!page.url().includes('/Login')) {
    await page.goto(`${IDP_BASE}/Account/Login`);
  }
  await page.fill('input[name="Input.Email"]', ADMIN_EMAIL);
  await page.fill('input[name="Input.Password"]', ADMIN_PASSWORD);
  await page.click('button[type="submit"]');
  await page.waitForLoadState('networkidle');
}

test.describe('Admin Login History API', () => {
  test('Retrieve login history for a user after login', async ({ page, request }) => {
    // Login to establish session and record login
    await adminLogin(page);

    // Fetch users to get admin user ID
    const usersResponse = await page.request.get(`${IDP_BASE}/api/admin/users?take=1`);
    expect(usersResponse.status()).toBe(200);
    const usersJson = await usersResponse.json();
    const adminUser = usersJson.items?.[0] || usersJson[0] || usersJson;
    const userId = adminUser.id || adminUser.userId || adminUser.Id;
    expect(userId).toBeTruthy();

    // Retrieve login history
    const historyResponse = await page.request.get(`${IDP_BASE}/api/admin/users/${userId}/login-history?count=5`);
    expect(historyResponse.status()).toBe(200);
    const history = await historyResponse.json();
    expect(Array.isArray(history)).toBe(true);
    expect(history.length).toBeGreaterThan(0);

    // Check structure of login history entry
    const latestLogin = history[0];
    expect(latestLogin).toHaveProperty('id');
    expect(latestLogin).toHaveProperty('userId');
    expect(latestLogin).toHaveProperty('loginTime');
    expect(latestLogin).toHaveProperty('ipAddress');
    expect(latestLogin).toHaveProperty('userAgent');
    expect(latestLogin).toHaveProperty('isSuccessful');
    expect(latestLogin).toHaveProperty('riskScore');
    expect(latestLogin).toHaveProperty('isFlaggedAbnormal');
    expect(latestLogin.isSuccessful).toBe(true);
  });

  test('Approve abnormal login returns 404 for non-abnormal login', async ({ page, request }) => {
    // Login to establish session
    await adminLogin(page);

    // Fetch users to get admin user ID
    const usersResponse = await page.request.get(`${IDP_BASE}/api/admin/users?take=1`);
    expect(usersResponse.status()).toBe(200);
    const usersJson = await usersResponse.json();
    const adminUser = usersJson.items?.[0] || usersJson[0] || usersJson;
    const userId = adminUser.id || adminUser.userId || adminUser.Id;
    expect(userId).toBeTruthy();

    // Retrieve login history
    const historyResponse = await page.request.get(`${IDP_BASE}/api/admin/users/${userId}/login-history?count=1`);
    expect(historyResponse.status()).toBe(200);
    const history = await historyResponse.json();
    expect(Array.isArray(history)).toBe(true);
    expect(history.length).toBeGreaterThan(0);

    const latestLogin = history[0];
    const loginHistoryId = latestLogin.id;

    // Try to approve a non-abnormal login (should return 404)
    const approveResponse = await page.request.post(`${IDP_BASE}/api/admin/users/${userId}/login-history/${loginHistoryId}/approve`);
    expect(approveResponse.status()).toBe(404);
  });
});