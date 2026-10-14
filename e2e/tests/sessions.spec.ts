import { expect, test } from "@playwright/test";
import { users } from "../support/environment";
import { closeAll, endAllSessions, hasSession, newSignedInBrowser, signIn } from "../support/keystone";

test.describe("Your sessions", () => {
  test("see every browser, end one, then sign out everywhere", async ({ page, browser }) => {
    // Start from a clean slate: earlier tests leave alice signed in elsewhere.
    await signIn(page, users.alice);
    await endAllSessions(page);

    await signIn(page, users.alice, "/sessions");
    const laptop = page;
    const phone = await newSignedInBrowser(browser, users.alice);

    try {
      await laptop.reload();
      const sessionCards = laptop.locator("div.rounded-xl", { hasText: /^.*session [0-9a-f]+/ });
      await expect(sessionCards).toHaveCount(2);
      await expect(laptop.getByText("This browser")).toHaveCount(1);

      // End the other browser's session.
      await laptop.getByRole("button", { name: "End session" }).click();
      await expect(sessionCards).toHaveCount(1);
      await phone.page.goto("/sessions");
      await expect(phone.page.getByText("Sign in to see where you are signed in.")).toBeVisible();
      expect(await hasSession(laptop)).toBe(true);

      // Sign out everywhere, including here.
      laptop.once("dialog", (dialog) => dialog.accept());
      await laptop.getByRole("button", { name: "Sign out everywhere" }).click();
      await expect(laptop.getByText("Sign in to see where you are signed in.")).toBeVisible();
      expect(await hasSession(laptop)).toBe(false);
    } finally {
      await closeAll(phone.context);
    }
  });

  test("ending Keystone sessions keeps the Zitadel session: signing in again needs no password", async ({ page }) => {
    await signIn(page, users.alice, "/sessions");
    page.once("dialog", (dialog) => dialog.accept());
    await page.getByRole("button", { name: "Sign out everywhere" }).click();
    await expect(page.getByText("Sign in to see where you are signed in.")).toBeVisible();

    await page.getByRole("main").getByRole("button", { name: "Sign in" }).click();

    // Straight back, without Zitadel's login UI: the Zitadel session in this browser is still alive.
    await expect(page.getByRole("heading", { name: "Your sessions" })).toBeVisible();
    expect(await hasSession(page)).toBe(true);
  });
});
