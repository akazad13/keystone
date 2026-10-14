import { expect, test } from "@playwright/test";
import { users } from "../support/environment";
import { callApi, closeAll, newSignedInBrowser, signIn } from "../support/keystone";

/** Unique per run, so leftovers from earlier runs never match. */
const unique = (name: string) => `${name} ${Date.now().toString(36)}`;

test.describe("My projects (browser > BFF > token exchange > Core API > SQL)", () => {
  test("create, complete and delete a project", async ({ page }) => {
    const name = unique("E2E project");
    page.on("dialog", (dialog) => dialog.accept()); // "Delete ...?"
    await signIn(page, users.alice, "/projects");

    await page.getByLabel("Name").fill(name);
    await page.getByLabel("Description").fill("Created by the end-to-end tests");
    await page.getByRole("button", { name: "Create project" }).click();

    const card = page.locator("div.rounded-xl", { hasText: name });
    await expect(card.getByText("Active")).toBeVisible();

    await card.getByRole("button", { name: "Complete" }).click();
    await expect(card.getByText("Completed")).toBeVisible();

    // Still within 5 minutes of signing in, so no step-up is needed.
    await card.getByRole("button", { name: "Delete" }).click();
    await expect(page.getByText(name)).toHaveCount(0);
  });

  test("validation runs in the browser and in the Core API", async ({ page }) => {
    await signIn(page, users.alice, "/projects");

    await page.getByRole("button", { name: "Create project" }).click();
    await expect(page.getByText("Project name is required")).toBeVisible();

    // Bypassing the form, the Core API's validator answers with a validation problem.
    const result = await callApi(page, "POST", "/api/keystone/projects", { body: { name: "x".repeat(201) } });
    expect(result.status).toBe(400);
    expect(JSON.stringify(result.body)).toContain("must not exceed 200 characters");
  });

  test("users only ever see their own projects", async ({ page, browser }) => {
    const name = unique("Alice's secret");
    await signIn(page, users.alice, "/projects");
    const created = await callApi(page, "POST", "/api/keystone/projects", { body: { name } });
    expect(created.status).toBe(201);
    const id = (created.body as { id: string }).id;

    const bob = await newSignedInBrowser(browser, users.bob, "/projects");
    try {
      await expect(bob.page.getByRole("heading", { name: "My projects" })).toBeVisible();
      await expect(bob.page.getByText(name)).toHaveCount(0);

      // Knowing the id does not help: someone else's project looks like one that does not exist.
      expect((await callApi(bob.page, "GET", `/api/keystone/projects/${id}`)).status).toBe(404);
      expect((await callApi(bob.page, "PUT", `/api/keystone/projects/${id}`, { body: { name: "Mine now", status: "Active" } })).status).toBe(404);
    } finally {
      await closeAll(bob.context);
      expect((await callApi(page, "DELETE", `/api/keystone/projects/${id}`)).status).toBe(204);
    }
  });
});
