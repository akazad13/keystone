import { beforeEach, describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClientProvider } from "@tanstack/react-query";
import { createMemoryHistory, createRoute, createRouter, RouterProvider } from "@tanstack/react-router";
import { Route as RootRoute } from "@/routes/__root";
import { FakeBff } from "../fakeBff";
import { newQueryClient } from "../render";

/** The real root layout with one child page, as the generated route tree would mount it. */
function renderAdminApp() {
  const queryClient = newQueryClient();
  const page = createRoute({ getParentRoute: () => RootRoute, path: "/", component: () => <p>Dashboard page</p> });
  const router = createRouter({
    routeTree: RootRoute.addChildren([page]),
    context: { queryClient },
    history: createMemoryHistory({ initialEntries: ["/"] }),
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  );
}

describe("Admin layout", () => {
  let bff: FakeBff;

  beforeEach(() => {
    bff = new FakeBff();
  });

  it("asks for an administrator sign-in when there is no session", async () => {
    bff.signedOut();

    renderAdminApp();

    expect(await screen.findByText("Admin Access Required")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sign In" }).closest("a")).toHaveAttribute(
      "href",
      expect.stringMatching(/^\/api\/keystone-admin\/auth\/login\?returnUrl=/)
    );
    expect(screen.queryByText("Dashboard page")).not.toBeInTheDocument();
  });

  it("shows the page with the header once signed in", async () => {
    bff.signedIn({ name: "Alice Example", email: "alice@example.com" });

    renderAdminApp();

    expect(await screen.findByText("Dashboard page")).toBeInTheDocument();
    expect(screen.getByText("Alice Example")).toBeInTheDocument();
    expect(screen.getByText("alice@example.com")).toBeInTheDocument();
  });

  it("signs out with a form POST to the admin prefix", async () => {
    bff.signedIn();

    renderAdminApp();

    const form = (await screen.findByRole("button", { name: /Logout/ })).closest("form")!;
    expect(form).toHaveAttribute("method", "post");
    expect(form).toHaveAttribute("action", "/api/keystone-admin/auth/logout");
  });
});
