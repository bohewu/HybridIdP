import { defineConfig, devices } from '@playwright/test';
import path from 'path';

// Path to store authenticated session state
const STORAGE_STATE = path.join(__dirname, '.auth', 'admin.json');

export default defineConfig({
  // Per-test timeout: increase to 90s for E2E flows with OAuth redirects
  timeout: 90_000,
  retries: 0,
  maxFailures: 1,
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
    // Main test project - uses stored auth state from global-setup
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        storageState: STORAGE_STATE,
      },
    },
  ],
});
