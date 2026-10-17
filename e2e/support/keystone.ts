import { expect, type Browser, type BrowserContext, type Page } from "@playwright/test";
import { environment, type TestUser } from "./environment";

/** Starts the OIDC login at the BFF, the way the apps' "Sign in" links do. */
export function loginPath(returnPath: string, { admin = false, prompt }: { admin?: boolean; prompt?: "login" } = {}) {
  const prefix = admin ? "/api/keystone-admin" : "/api/keystone";
  const url = `${prefix}/auth/login?returnUrl=${encodeURIComponent(returnPath)}`;
  return prompt ? `${url}&prompt=${prompt}` : url;
}

/**
 * Completes Zitadel's login UI (login v2) for a user: login name, then password. Expects the page to be
 * on the login UI already, or on its way there.
 */
export async function completeZitadelLogin(page: Page, user: TestUser) {
  await page.waitForURL((url) => url.href.startsWith(`${environment.zitadelUrl}/ui/v2/login/`));

  const password = page.locator('input[type="password"]');
  const loginName = page.getByLabel("Loginname");
  await expect(loginName.or(password)).toBeVisible();
  if (await loginName.isVisible()) {
    await loginName.fill(user.loginName);
    await page.getByRole("button", { name: "Continue" }).click();
  }

  await password.fill(environment.password);
  await page.getByRole("button", { name: "Continue" }).click();
}

/**
 * Signs in through the BFF and Zitadel, and waits until the browser is back on Keystone. When this
 * browser still has a Zitadel session, Zitadel signs the user in silently (single sign-on) and its
 * login UI never appears.
 */
export async function signIn(page: Page, user: TestUser, returnPath = "/", options: { admin?: boolean } = {}) {
  const backOnKeystone = (url: URL) => url.origin === environment.baseUrl && url.pathname === returnPath;
  await page.goto(loginPath(returnPath, options));
  await page.waitForURL((url) => backOnKeystone(url) || url.href.startsWith(`${environment.zitadelUrl}/ui/v2/login/`));
  if (!backOnKeystone(new URL(page.url()))) {
    await completeZitadelLogin(page, user);
  }
  await page.waitForURL(backOnKeystone);
}

/** A fresh browser (own cookies, own Zitadel session) signed in as <user>. */
export async function newSignedInBrowser(browser: Browser, user: TestUser, returnPath = "/") {
  const context = await browser.newContext();
  const page = await context.newPage();
  await signIn(page, user, returnPath);
  return { context, page };
}

/**
 * Signs out at Zitadel itself, not through Keystone, as if the user signed out of another application
 * that shares the Zitadel session. Zitadel then calls the BFF's back-channel logout endpoint.
 */
export async function signOutAtZitadel(page: Page, user: TestUser) {
  await page.goto(`${environment.zitadelUrl}/oidc/v1/end_session?client_id=${environment.clientId}`);
  // Zitadel's login UI asks which account to sign out.
  await page.getByRole("button", { name: new RegExp(user.displayName) }).click();
  await expect(page.getByText("You have successfully logged out.")).toBeVisible();
}

/** Calls the BFF from the page, with the session cookie, like the apps' api.ts does. */
export async function callApi(
  page: Page,
  method: string,
  path: string,
  { body, csrf = true }: { body?: unknown; csrf?: boolean } = {}
): Promise<{ status: number; body: unknown }> {
  for (let attempt = 0; attempt < 3; attempt++) {
    try {
      return await page.evaluate(
        async ({ method, path, body, csrf }) => {
          const response = await fetch(path, {
            method,
            credentials: "include",
            headers: { "Content-Type": "application/json", ...(csrf ? { "X-CSRF": "1" } : {}) },
            body: body === undefined ? undefined : JSON.stringify(body),
          });
          const text = await response.text();
          let parsed: unknown = text;
          try {
            parsed = text ? JSON.parse(text) : null;
          } catch {
            // not JSON
          }
          return { status: response.status, body: parsed };
        },
        { method, path, body, csrf }
      );
    } catch (error) {
      if (
        attempt < 2 &&
        error instanceof Error &&
        (error.message.includes("Failed to fetch") || error.message.includes("Execution context was destroyed"))
      ) {
        await page.waitForTimeout(500);
        continue;
      }
      throw error;
    }
  }
  throw new Error(`callApi ${method} ${path} failed after retries`);
}

/** Whether this browser currently has a Keystone session (the BFF's /me). */
export async function hasSession(page: Page) {
  return (await callApi(page, "GET", "/api/keystone/me")).status === 200;
}

/** Ends every Keystone session of the user signed in on <page> (the "Sign out everywhere" call). */
export async function endAllSessions(page: Page) {
  const result = await callApi(page, "POST", "/api/keystone/sessions/revoke-all");
  expect(result.status).toBe(200);
}

export async function closeAll(...contexts: BrowserContext[]) {
  await Promise.all(contexts.map((context) => context.close()));
}
