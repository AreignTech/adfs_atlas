import { defineConfig, devices } from "@playwright/test"

const baseURL = "http://127.0.0.1:3100"

export default defineConfig({
  testDir: "./tests/ui",
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  reporter: "list",
  use: {
    baseURL,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
  webServer: {
    command: process.env.ATLAS_UI_TEST_PRODUCTION ? "pnpm exec next start -H 127.0.0.1 -p 3100" : "pnpm exec next dev -H 127.0.0.1 -p 3100",
    url: baseURL,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
    env: {
      ATLAS_PLAYWRIGHT: process.env.ATLAS_UI_TEST_PRODUCTION ? "" : "true",
    },
  },
})
