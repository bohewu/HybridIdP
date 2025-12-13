import { test as base } from '@playwright/test';
import { AdminApi } from './helpers/api-client';
import path from 'path';

const STORAGE_STATE = path.join(__dirname, '..', '.auth', 'admin.json');

/**
 * Extended test fixture that provides an authenticated API client.
 * 
 * Usage:
 * ```typescript
 * import { test, expect } from '../fixtures';
 * 
 * test('example', async ({ page, api }) => {
 *   const user = await api.users.create({ ... });
 *   await page.goto(`/Admin/Users/Edit/${user.id}`);
 *   // ...
 *   await api.users.deleteUser(user.id);
 * });
 * ```
 */
export const test = base.extend<{ api: AdminApi }>({
    api: async ({ playwright }, use) => {
        // Create an API request context with stored auth cookies
        const context = await playwright.request.newContext({
            baseURL: 'https://localhost:7035',
            ignoreHTTPSErrors: true,
            storageState: STORAGE_STATE,
        });

        const api = new AdminApi(context);
        await use(api);

        // Cleanup
        await context.dispose();
    },
});

export { expect } from '@playwright/test';
