import { test, expect } from '@playwright/test';
import { loginAsUser } from '../helpers/auth-helper';

test.describe('Client Credentials Flow', () => {
    const clientId = `m2m-test-${Date.now()}`;

    test('should allow custom scopes but deny OIDC scopes', async ({ page, request }) => {
        // 1. Setup: Create M2M Client via Admin UI
        await page.goto('https://localhost:7035/admin/clients');
        await loginAsUser(page, 'admin@hybridauth.local', 'Admin@123');

        // Setup Client
        const setupData = await page.evaluate(async (cId) => {
            const res = await fetch('/api/admin/clients', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    clientId: cId,
                    displayName: 'M2M Test Client',
                    applicationType: 'web',
                    type: 'confidential',
                    permissions: ['ept:token', 'gt:client_credentials']
                })
            });
            const data = await res.json();

            // Add Scope
            await fetch(`/api/admin/clients/${data.id}/scopes`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ scopes: ['api:company:read'] })
            });

            return data;
        }, clientId);

        const clientSecret = setupData.clientSecret;
        expect(clientSecret).toBeTruthy();

        // 2. Test Success: Custom Scope
        const tokenResponse = await request.post('https://localhost:7035/connect/token', {
            form: {
                grant_type: 'client_credentials',
                client_id: clientId,
                client_secret: clientSecret,
                scope: 'api:company:read'
            },
            ignoreHTTPSErrors: true
        });

        expect(tokenResponse.status()).toBe(200);
        const tokenData = await tokenResponse.json();
        expect(tokenData.access_token).toBeTruthy();
        expect(tokenData.scope).toContain('api:company:read');

        // 3. Test Failure: OIDC Scope (openid)
        const failResponse = await request.post('https://localhost:7035/connect/token', {
            form: {
                grant_type: 'client_credentials',
                client_id: clientId,
                client_secret: clientSecret,
                scope: 'openid'
            },
            ignoreHTTPSErrors: true
        });

        // Should fail because M2M cannot request openid/profile
        const failData = await failResponse.json();
        if (failResponse.status() === 200) {
            console.error('Unexpected success for openid scope:', failData);
        }
        expect(failResponse.status()).toBe(400);
        expect(failData.error).toBe('invalid_scope');
    });
});
