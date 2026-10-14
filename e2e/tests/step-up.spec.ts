import { expect, test, type Page } from "@playwright/test";
import { environment, users } from "../support/environment";
import { callApi, completeZitadelLogin, loginPath, signIn } from "../support/keystone";

async function authTime(page: Page): Promise<number> {
  const me = await callApi(page, "GET", "/api/keystone/me");
  const claims = (me.body as { claims: { type: string; value: string }[] }).claims;
  return Number(claims.find((c) => c.type === "auth_time")!.value);
}

/*
 * The 5 minute limit itself (DELETE refused with an RFC 9470 challenge after an old sign-in) is
 * covered by the BFF's unit and proxy tests; waiting 5 minutes here would make the suite very slow.
 * These tests cover the part only a real identity provider can: prompt=login.
 */
test.describe("Step-up: entering the password again", () => {
  test("prompt=login asks for the password even though the Zitadel session is still valid", async ({ page }) => {
    await signIn(page, users.alice, "/projects");
    const before = await authTime(page);

    // Without prompt=login, Zitadel would send the browser straight back (single sign-on). With it,
    // Zitadel's login UI starts over, from the login name.
    await page.goto(loginPath("/projects", { prompt: "login" }));
    await expect(page.getByLabel("Loginname")).toBeVisible();
    await page.waitForTimeout(1100); // auth_time has a resolution of one second
    await completeZitadelLogin(page, users.alice);

    await page.waitForURL(`${environment.baseUrl}/projects`);
    expect(await authTime(page)).toBeGreaterThan(before);
  });

  test("without prompt=login, signing in again is silent", async ({ page }) => {
    await signIn(page, users.alice, "/projects");

    await page.goto(loginPath("/me"));

    await page.waitForURL(`${environment.baseUrl}/me`);
    await expect(page.getByRole("heading", { name: "Who am I?" })).toBeVisible();
  });
});
