import { test, expect } from '@playwright/test';
import { loginAsUser } from './helpers/auth-helper';

test.describe('Device Authorization Flow', () => {
    const clientId = `device-test-${Date.now()}`;

    test('should complete full device flow', async ({ page, request }) => {
        test.setTimeout(60000); // Increase timeout to 60s for polling
        // 1. Setup: Create Device Client via Admin UI/API
        await page.goto('https://localhost:7035/admin/clients');
        await loginAsUser(page, 'admin@hybridauth.local', 'Admin@123');

        await page.evaluate(async (cId) => {
            // Create Client
            const res = await fetch('/api/admin/clients', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    clientId: cId,
                    displayName: 'Device Flow Test Client',
                    applicationType: 'native',
                    type: 'public',
                    consentType: 'explicit',
                    redirectUris: [],
                    permissions: [
                        'ept:device', 'ept:token',
                        'gt:urn:ietf:params:oauth:grant-type:device_code',
                        'gt:refresh_token',
                        'scp:openid', 'scp:profile', 'scp:email'
                    ]
                })
            });
            if (!res.ok) throw new Error(await res.text());
            const data = await res.json();

            // Add Scopes
            await fetch(`/api/admin/clients/${data.id}/scopes`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ scopes: ['openid', 'profile', 'email'] })
            });
        }, clientId);

        // Logout Admin
        await page.goto('https://localhost:7035/logout');

        // 2. Initiate Device Flow
        const deviceResponse = await request.post('https://localhost:7035/connect/device', {
            form: {
                client_id: clientId,
                scope: 'openid profile email'
            },
            ignoreHTTPSErrors: true
        });

        expect(deviceResponse.ok()).toBeTruthy();
        const deviceData = await deviceResponse.json();
        const { device_code, user_code, verification_uri } = deviceData;

        expect(device_code).toBeTruthy();
        expect(user_code).toBeTruthy();

        // 3. User Authorization
        const verifyUrl = verification_uri.startsWith('http') ? verification_uri : `https://localhost:7035${verification_uri}`;
        await page.goto(verifyUrl);

        // Login as User
        await loginAsUser(page, 'testuser@hybridauth.local', 'Test@123');

        // Enter User Code
        const codeInput = page.locator('input[name="UserCode"]');
        if (await codeInput.isVisible() && await codeInput.inputValue() === '') {
            await codeInput.fill(user_code);
        }
        await page.click('button[type="submit"]');

        // Verify Success Message (wait for redirect to home)
        await expect(page).toHaveURL('https://localhost:7035/', { timeout: 15000 });

        // 4. Poll for Token
        let tokenResponse;
        for (let i = 0; i < 10; i++) {
            tokenResponse = await request.post('https://localhost:7035/connect/token', {
                form: {
                    grant_type: 'urn:ietf:params:oauth:grant-type:device_code',
                    client_id: clientId,
                    device_code: device_code
                },
                ignoreHTTPSErrors: true
            });

            if (tokenResponse.ok()) break;

            // If pending, wait and retry
            const error = await tokenResponse.json();
            if (error.error === 'authorization_pending') {
                await page.waitForTimeout(2000); // Wait 2s
                continue;
            } else {
                // If other error, fail
                throw new Error(`Token exchange failed: ${JSON.stringify(error)}`);
            }
        }

        expect(tokenResponse?.ok()).toBeTruthy();
        const tokenData = await tokenResponse?.json();
        expect(tokenData.access_token).toBeTruthy();
    });
});
