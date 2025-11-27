import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  // Per-test timeout: increase to 60s so longer admin UI flows don't hit the global limit
  timeout: 60_000,
  retries: 0,
  // Lower worker count when running locally to reduce flakiness caused by contention
  // Use 3 workers for a balance of speed and isolation as requested
  workers: 3,
  fullyParallel: true, // Enable full parallelization across workers
  use: {
    baseURL: 'https://localhost:7001',
    ignoreHTTPSErrors: true,
    trace: 'retain-on-failure',
  },
  globalSetup: require.resolve('./global-setup'),
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
});
