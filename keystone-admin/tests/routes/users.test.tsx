import { beforeEach, describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route } from "@/routes/users";
import type { AdminUser } from "@/lib/schemas";
import { FakeBff } from "../fakeBff";
import { renderWithApp } from "../render";

const UsersPage = Route.options.component!;

const alice: AdminUser = {
  id: "u1",
  email: "alice@example.com",
  displayName: "Alice Example",
  role: "Admin",
  projectCount: 3,
  createdAt: "2026-10-01T09:00:00Z",
  lastSeenAt: "2026-10-14T09:00:00Z",
};

const bob: AdminUser = { ...alice, id: "u2", email: "bob@example.com", displayName: "Bob Builder", role: "User", projectCount: 0 };

describe("Users page", () => {
  let bff: FakeBff;

  beforeEach(() => {
    bff = new FakeBff();
  });

  it("lists the users the Core API knows", async () => {
    bff.on("GET", "/api/keystone-admin/admin/users", { status: 200, body: [alice, bob] });

    renderWithApp(UsersPage, { path: "/users" });

    expect(await screen.findByText("Alice Example")).toBeInTheDocument();
    expect(screen.getByText("Bob Builder")).toBeInTheDocument();
    expect(screen.getByText("Users (2)")).toBeInTheDocument();
    expect(screen.getByText("Admin")).toBeInTheDocument();
  });

  it("filters by name or email", async () => {
    const user = userEvent.setup();
    bff.on("GET", "/api/keystone-admin/admin/users", { status: 200, body: [alice, bob] });
    renderWithApp(UsersPage, { path: "/users" });

    const search = await screen.findByPlaceholderText(/Search users/);
    await user.type(search, "BOB@");

    expect(screen.getByText("Users (1)")).toBeInTheDocument();
    expect(screen.queryByText("Alice Example")).not.toBeInTheDocument();
    expect(screen.getByText("Bob Builder")).toBeInTheDocument();
  });

  it("shows Admins only when the Core API answers 403", async () => {
    // The UI does not decide who is an admin; the API does, from the token's role claim.
    bff.on("GET", "/api/keystone-admin/admin/users", { status: 403 });

    renderWithApp(UsersPage, { path: "/users" });

    expect(await screen.findByText("Admins only")).toBeInTheDocument();
    expect(screen.queryByText(/Users \(/)).not.toBeInTheDocument();
  });

  it("reports other failures", async () => {
    bff.on("GET", "/api/keystone-admin/admin/users", { status: 500 });

    renderWithApp(UsersPage, { path: "/users" });

    expect(await screen.findByText("Could not load users (500).")).toBeInTheDocument();
  });
});
