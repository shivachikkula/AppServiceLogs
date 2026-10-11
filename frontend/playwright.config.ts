import { defineConfig, devices } from '@playwright/test';

const port = 4300;

/** Smoke test of the production build against a mocked API (run `npm run build` first). */
export default defineConfig({
  testDir: './e2e',
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 1 : 0,
  reporter: process.env['CI'] ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: `http://localhost:${port}`,
    trace: 'retain-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        // Lets a machine with a preinstalled Chromium skip `playwright install`.
        launchOptions: process.env['PLAYWRIGHT_CHROMIUM_PATH']
          ? { executablePath: process.env['PLAYWRIGHT_CHROMIUM_PATH'] }
          : {},
      },
    },
  ],
  webServer: {
    command: 'node e2e/serve.mjs',
    url: `http://localhost:${port}`,
    env: { PORT: String(port) },
    reuseExistingServer: !process.env['CI'],
  },
});
