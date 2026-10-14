import { expect, test } from "@playwright/test";
import { users } from "../support/environment";
import { closeAll, signIn } from "../support/keystone";

test.describe("Admin dashboard (roles from Zitadel)", () => {
  test("a user without the Admin role is signed in but sees Admins only", async ({ page }) => {
    await signIn(page, users.bob, "/admin/", { admin: true });

    await expect(page.getByText("Admins only")).toBeVisible();

    await page.goto("/admin/users");
    await expect(page.getByText("Admins only")).toBeVisible();
  });

  test("an admin sees live numbers and every user who has used Keystone", async ({ page, browser }) => {
    // Make sure bob has called the API at least once, so the Core API knows him.
    const bobContext = await browser.newContext();
    const bobPage = await bobContext.newPage();
    await signIn(bobPage, users.bob, "/projects");
    await expect(bobPage.getByRole("heading", { name: "My projects" })).toBeVisible();
    await closeAll(bobContext);

    await signIn(page, users.alice, "/admin/", { admin: true });
    await expect(page.getByRole("heading", { name: "Dashboard Overview" })).toBeVisible();
    await expect(page.getByText("Total users")).toBeVisible();
    await expect(page.getByText(/^\d+ admins$/)).toBeVisible();

    await page.getByRole("link", { name: "All users" }).click();
    await expect(page).toHaveURL(/\/admin\/users$/);
    const bobRow = page.getByRole("row", { name: new RegExp(users.bob.displayName) });
    const aliceRow = page.getByRole("row", { name: new RegExp(users.alice.displayName) });
    await expect(bobRow.getByText("User", { exact: true })).toBeVisible();
    await expect(aliceRow.getByText("Admin", { exact: true })).toBeVisible();
  });

  test("deep links into the admin app work after sign-in", async ({ page }) => {
    await signIn(page, users.alice, "/admin/users", { admin: true });

    await expect(page.getByRole("heading", { name: "Users" })).toBeVisible();
  });
});
