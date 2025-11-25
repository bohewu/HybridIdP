import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  // Per-test timeout: increase to 60s so longer admin UI flows don't hit the global limit
  timeout: 60_000,
  retries: 0,
  // Lower worker count when running locally to reduce flakiness caused by contention
  workers: 2,
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
