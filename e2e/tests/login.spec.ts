import { expect, test } from "@playwright/test";
import { environment, users } from "../support/environment";
import { callApi, completeZitadelLogin, loginPath, signIn } from "../support/keystone";

test.describe("Signing in", () => {
  test("a signed-out visitor signs in through Zitadel and comes back to the same page", async ({ page }) => {
    await page.goto("/me");
    await expect(page.getByText("Sign in to see who the platform thinks you are.")).toBeVisible();

    await page.getByRole("main").getByRole("button", { name: "Sign in" }).click();
    await completeZitadelLogin(page, users.alice);

    await expect(page).toHaveURL(`${environment.baseUrl}/me`);
    await expect(page.getByRole("heading", { name: "Who am I?" })).toBeVisible();
    await expect(page.getByRole("banner").getByText(users.alice.displayName)).toBeVisible();
  });

  test("the Core API sees the user through a Keystone token from the token exchange", async ({ page }) => {
    await signIn(page, users.alice, "/me");

    // Left: the BFF session (Zitadel's ID token). Right: the Core API's view (the Keystone access token).
    const session = page.getByRole("table").nth(0);
    const coreApi = page.getByRole("table").nth(1);
    // Rows read "<claim> [only here] <value>".
    await expect(coreApi.getByRole("row", { name: /^iss( only here)? https:\/\/identity\.keystone\.internal$/ })).toBeVisible();
    await expect(coreApi.getByRole("row", { name: /^aud( only here)? core-api$/ })).toBeVisible();
    await expect(coreApi.getByRole("row", { name: /^role( only here)? Admin$/ })).toBeVisible();
    // The Zitadel session's id (sid) stays in the BFF; the Keystone token does not carry it.
    await expect(session.getByRole("row", { name: /^sid only here / })).toBeVisible();
    // Both sides describe the same person.
    const sub = await session.getByRole("row", { name: /^sub / }).getByRole("cell").nth(1).innerText();
    await expect(coreApi.getByRole("row", { name: `sub ${sub}` })).toBeVisible();
  });

  test("the session cookie is HttpOnly, Secure, SameSite=Lax and host-only", async ({ page, context }) => {
    await signIn(page, users.alice);

    const cookie = (await context.cookies(environment.baseUrl)).find((c) => c.name === "__Host-keystone");

    expect(cookie).toBeDefined();
    expect(cookie!.httpOnly).toBe(true);
    expect(cookie!.secure).toBe(true);
    expect(cookie!.sameSite).toBe("Lax");
    expect(cookie!.path).toBe("/");
    // The tokens stay in Redis: the cookie only references the session.
    expect(cookie!.value.length).toBeLessThan(1000);
  });

  test("no token ever reaches the browser", async ({ page }) => {
    const jwt = /eyJ[\w-]+\.eyJ[\w-]+\.[\w-]+/;
    const responses: string[] = [];
    page.on("response", async (response) => {
      if (response.url().startsWith(`${environment.baseUrl}/api/`)) {
        responses.push(await response.text().catch(() => ""));
      }
    });

    await signIn(page, users.alice, "/me");
    await expect(page.getByRole("table")).toHaveCount(2);

    expect(responses.length).toBeGreaterThan(0);
    for (const body of responses) expect(body).not.toMatch(jwt);
    const storage = await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }));
    expect(storage).not.toMatch(jwt);
  });

  test("login refuses to redirect to another site afterwards", async ({ page }) => {
    await page.goto(loginPath("https://evil.example/phish"));
    await completeZitadelLogin(page, users.alice);

    await expect(page).toHaveURL(`${environment.baseUrl}/`);
  });

  test("state-changing API calls without the CSRF header are rejected", async ({ page }) => {
    await signIn(page, users.alice);

    const withoutHeader = await callApi(page, "POST", "/api/keystone/projects", { body: { name: "CSRF probe" }, csrf: false });

    expect(withoutHeader.status).toBe(400);
    const projects = await callApi(page, "GET", "/api/keystone/projects");
    expect(JSON.stringify(projects.body)).not.toContain("CSRF probe");
  });
});
