import { expect, test } from "@playwright/test";
import { environment, users } from "../support/environment";
import { closeAll, hasSession, loginPath, newSignedInBrowser, signIn, signOutAtZitadel } from "../support/keystone";

test.describe("Signing out", () => {
  test("sign out ends the Keystone session and the Zitadel session (RP-initiated logout)", async ({ page }) => {
    await signIn(page, users.alice, "/me");

    await page.getByRole("banner").getByRole("button", { name: "Sign out" }).click();

    // Zitadel ends its session and sends the browser back to the portal.
    await page.waitForURL(`${environment.baseUrl}/`);
    await expect(page.getByRole("banner").getByRole("button", { name: "Sign in" })).toBeVisible();
    expect(await hasSession(page)).toBe(false);

    // The Zitadel (SSO) session is gone too: signing in again asks for the login name, not just a click.
    await page.goto(loginPath("/"));
    await expect(page.getByLabel("Loginname")).toBeVisible();
  });

  test("the admin app signs out to the admin app", async ({ page }) => {
    await signIn(page, users.alice, "/admin/", { admin: true });

    await page.getByRole("button", { name: "Logout" }).click();

    await page.waitForURL(`${environment.baseUrl}/admin/`);
    await expect(page.getByText("Admin Access Required")).toBeVisible();
  });
});

test.describe("Back-channel logout", () => {
  test("signing out at Zitadel ends the Keystone session, without the browser visiting Keystone", async ({ page }) => {
    await signIn(page, users.alice, "/me");
    expect(await hasSession(page)).toBe(true);

    // As if alice signed out of another application that shares her Zitadel session. Zitadel calls the
    // BFF's /bff/backchannel-logout server-to-server; the BFF deletes the session from Redis.
    await signOutAtZitadel(page, users.alice);

    await page.goto("/me");
    await expect(page.getByText("Sign in to see who the platform thinks you are.")).toBeVisible();
    expect(await hasSession(page)).toBe(false);
  });

  test("only the Zitadel session that ended is signed out; other browsers keep theirs", async ({ page, browser }) => {
    await signIn(page, users.alice);
    const other = await newSignedInBrowser(browser, users.alice);

    await signOutAtZitadel(page, users.alice);

    await page.goto("/");
    expect(await hasSession(page)).toBe(false);
    expect(await hasSession(other.page)).toBe(true);
    await closeAll(other.context);
  });

  test("an open tab notices the logout when the user comes back to it", async ({ page }) => {
    await signIn(page, users.alice, "/projects");
    await expect(page.getByRole("heading", { name: "My projects" })).toBeVisible();

    // Sign out at Zitadel in another tab of the same browser (same cookies, same Zitadel session).
    const zitadelTab = await page.context().newPage();
    await signOutAtZitadel(zitadelTab, users.alice);
    await zitadelTab.close();

    // Switching back to a tab fires visibilitychange (it bubbles from document to window, where React
    // Query listens); useAuth then re-checks /me. Headless tabs never become hidden, so fire it here.
    await page.bringToFront();
    await page.evaluate(() => document.dispatchEvent(new Event("visibilitychange", { bubbles: true })));
    await expect(page.getByText("Sign in to see your projects.")).toBeVisible();
  });
});
