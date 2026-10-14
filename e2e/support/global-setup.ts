import { chromium } from "@playwright/test";
import { environment, users } from "./environment";
import { callApi, signIn } from "./keystone";

/** Fails fast, with instructions, when the stack is not running. */
export default async function globalSetup() {
  process.env.NODE_TLS_REJECT_UNAUTHORIZED ??= "0"; // the dev certificate, for this check only

  const problems: string[] = [];

  try {
    const discovery = await fetch(`${environment.zitadelUrl}/.well-known/openid-configuration`);
    if (!discovery.ok) problems.push(`Zitadel discovery answered ${discovery.status}.`);
  } catch {
    problems.push(`Zitadel is not reachable at ${environment.zitadelUrl}. Start it: cd zitadel; docker compose up -d --wait`);
  }

  try {
    const me = await fetch(`${environment.baseUrl}/api/keystone/me`);
    if (me.status !== 401) problems.push(`The BFF answered ${me.status} for an anonymous /me (expected 401).`);
  } catch {
    problems.push(`The BFF is not reachable at ${environment.baseUrl}. Start Keystone: dotnet run --project Keystone.AppHost`);
  }

  // Reading these early turns a missing setup.ps1 run into one clear message.
  try {
    void environment.password;
    void environment.clientId;
  } catch (error) {
    problems.push((error as Error).message);
  }

  if (problems.length > 0) {
    throw new Error(`The end-to-end tests need the running stack (see e2e/README.md):\n- ${problems.join("\n- ")}`);
  }

  await warmUp();
}

/**
 * Right after the AppHost starts, the first API call is slow: the token exchange, the Core API's
 * first database connection and JWKS fetch, and Vite compiling each app on its first page load.
 * Pay that once here, with generous timeouts, so the tests themselves see a warm stack.
 */
async function warmUp() {
  const browser = await chromium.launch({ channel: environment.browserChannel || undefined });
  try {
    const context = await browser.newContext({ baseURL: environment.baseUrl, ignoreHTTPSErrors: true });
    const page = await context.newPage();
    page.setDefaultTimeout(120_000);

    await signIn(page, users.alice, "/projects");
    const projects = await callApi(page, "GET", "/api/keystone/projects");
    if (projects.status !== 200) throw new Error(`Warm-up: GET /api/keystone/projects answered ${projects.status}.`);

    await page.goto("/admin/");
    await page.getByRole("heading", { name: "Dashboard Overview" }).waitFor();
    await page.getByText("Total users").waitFor();
  } finally {
    await browser.close();
  }
}
