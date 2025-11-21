import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  timeout: 60_000,
  retries: 0,
  workers: 4, // Run 4 tests in parallel
  fullyParallel: true, // Enable full parallelization across workers
  use: {
    baseURL: 'https://localhost:7001',
    ignoreHTTPSErrors: true,
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
});
