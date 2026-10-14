import { beforeEach, describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { Route } from "@/routes/index";
import type { AdminStats, AdminUser } from "@/lib/schemas";
import { FakeBff } from "../fakeBff";
import { renderWithApp } from "../render";

const DashboardPage = Route.options.component!;

const stats: AdminStats = { totalUsers: 1234, activeUsers: 56, newUsersToday: 7, admins: 2, totalProjects: 89, activeProjects: 40 };

const users: AdminUser[] = [
  { id: "u1", email: "a@example.com", displayName: "Alice", role: "Admin", projectCount: 1, createdAt: "2026-10-01T09:00:00Z", lastSeenAt: "2026-10-14T09:00:00Z" },
  { id: "u2", email: "b@example.com", displayName: "Bob", role: "User", projectCount: 2, createdAt: "2026-10-01T09:00:00Z", lastSeenAt: "2026-10-13T09:00:00Z" },
];

describe("Dashboard page", () => {
  let bff: FakeBff;

  beforeEach(() => {
    bff = new FakeBff();
  });

  it("shows the live numbers from the Core API", async () => {
    bff
      .on("GET", "/api/keystone-admin/admin/stats", { status: 200, body: stats })
      .on("GET", "/api/keystone-admin/admin/users", { status: 200, body: users });

    renderWithApp(DashboardPage);

    expect(await screen.findByText((1234).toLocaleString())).toBeInTheDocument();
    expect(screen.getByText("2 admins")).toBeInTheDocument();
    expect(screen.getByText("56")).toBeInTheDocument();
    expect(screen.getByText("40 active")).toBeInTheDocument();
  });

  it("lists recently active users with their project counts", async () => {
    bff
      .on("GET", "/api/keystone-admin/admin/stats", { status: 200, body: stats })
      .on("GET", "/api/keystone-admin/admin/users", { status: 200, body: users });

    renderWithApp(DashboardPage);

    expect(await screen.findByText("Admin · 1 project")).toBeInTheDocument();
    expect(screen.getByText("User · 2 projects")).toBeInTheDocument();
  });

  it("shows placeholders while numbers are missing", async () => {
    bff
      .on("GET", "/api/keystone-admin/admin/stats", { status: 500 })
      .on("GET", "/api/keystone-admin/admin/users", { status: 200, body: [] });

    renderWithApp(DashboardPage);

    expect(await screen.findByText("Dashboard Overview")).toBeInTheDocument();
    expect(screen.getAllByText("--").length).toBeGreaterThan(0);
  });

  it("shows Admins only when the Core API answers 403", async () => {
    bff
      .on("GET", "/api/keystone-admin/admin/stats", { status: 403 })
      .on("GET", "/api/keystone-admin/admin/users", { status: 403 });

    renderWithApp(DashboardPage);

    expect(await screen.findByText("Admins only")).toBeInTheDocument();
  });
});
