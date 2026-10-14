import { defineConfig } from "@playwright/test";
import { environment } from "./support/environment";

/**
 * End-to-end tests: a real browser against the running stack (Zitadel in Docker, Keystone under the
 * AppHost). Nothing is faked. See e2e/README.md for how to start the stack first.
 */
export default defineConfig({
  testDir: "./tests",
  globalSetup: "./support/global-setup.ts",
  // The tests share one Zitadel, one Redis and one database, and some of them end every session of a
  // test user. Running them one at a time keeps them independent of each other.
  workers: 1,
  fullyParallel: false,
  retries: 0,
  forbidOnly: !!process.env.CI,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  // On GitHub Actions, failures are also annotated on the pull request.
  reporter: process.env.CI ? [["github"], ["list"], ["html", { open: "never" }]] : [["list"], ["html", { open: "never" }]],
  use: {
    baseURL: environment.baseUrl,
    // Microsoft Edge is installed on Windows, so no browser download is needed. Set E2E_BROWSER_CHANNEL
    // to "chrome", or to "" for Playwright's own Chromium (after `npx playwright install chromium`).
    channel: environment.browserChannel || undefined,
    // The ASP.NET Core development certificate; trusted on a developer machine, but not necessarily in CI.
    ignoreHTTPSErrors: true,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
});
