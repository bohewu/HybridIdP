import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  timeout: 60_000,
  retries: 0,
  use: {
    baseURL: 'https://localhost:7001',
    ignoreHTTPSErrors: true,
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
});
