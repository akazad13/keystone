import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route } from "@/routes/projects";
import { browser } from "@/lib/navigation";
import type { Project } from "@/lib/schemas";
import { FakeBff } from "../fakeBff";
import { renderWithApp } from "../render";

const ProjectsPage = Route.options.component!;

const website: Project = {
  id: "p1",
  name: "Website relaunch",
  description: "New look",
  status: "Active",
  dueDate: "2026-12-01",
  createdAt: "2026-10-01T09:00:00Z",
  updatedAt: null,
};

describe("Projects page", () => {
  let bff: FakeBff;

  beforeEach(() => {
    bff = new FakeBff();
  });

  it("asks a signed-out visitor to sign in and loads nothing", async () => {
    bff.signedOut();

    renderWithApp(ProjectsPage, { path: "/projects" });

    expect(await screen.findByText("Sign in to see your projects.")).toBeInTheDocument();
    expect(bff.requestsTo("GET", "/api/keystone/projects")).toHaveLength(0);
  });

  it("lists the user's projects", async () => {
    bff.signedIn().on("GET", "/api/keystone/projects", { status: 200, body: [website] });

    renderWithApp(ProjectsPage, { path: "/projects" });

    expect(await screen.findByText("Website relaunch")).toBeInTheDocument();
    expect(screen.getByText("New look")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("shows an empty state", async () => {
    bff.signedIn().on("GET", "/api/keystone/projects", { status: 200, body: [] });

    renderWithApp(ProjectsPage, { path: "/projects" });

    expect(await screen.findByText(/No projects yet/)).toBeInTheDocument();
  });

  it("creates a project without sending an owner, then reloads the list", async () => {
    const user = userEvent.setup();
    let projects: Project[] = [];
    bff
      .signedIn()
      .on("GET", "/api/keystone/projects", () => ({ status: 200, body: projects }))
      .on("POST", "/api/keystone/projects", (request) => {
        projects = [{ ...website, id: "p2", name: (request.body as { name: string }).name, description: null }];
        return { status: 201, body: projects[0] };
      });
    renderWithApp(ProjectsPage, { path: "/projects" });

    await user.type(await screen.findByLabelText("Name"), "Mobile app");
    await user.click(screen.getByRole("button", { name: /Create project/ }));

    expect(await screen.findByText("Mobile app")).toBeInTheDocument();
    const [create] = bff.requestsTo("POST", "/api/keystone/projects");
    expect(create.body).toEqual({ name: "Mobile app", description: null, dueDate: null });
    expect(create.headers.get("X-CSRF")).toBe("1");
  });

  it("validates in the browser before calling the API", async () => {
    const user = userEvent.setup();
    bff.signedIn().on("GET", "/api/keystone/projects", { status: 200, body: [] });
    renderWithApp(ProjectsPage, { path: "/projects" });

    await user.click(await screen.findByRole("button", { name: /Create project/ }));

    expect(await screen.findByText("Project name is required")).toBeInTheDocument();
    expect(bff.requestsTo("POST", "/api/keystone/projects")).toHaveLength(0);
  });

  it("shows the API's validation messages", async () => {
    const user = userEvent.setup();
    bff
      .signedIn()
      .on("GET", "/api/keystone/projects", { status: 200, body: [] })
      .on("POST", "/api/keystone/projects", {
        status: 400,
        body: { title: "One or more validation errors occurred.", errors: { Name: ["Project name is taken."] } },
      });
    renderWithApp(ProjectsPage, { path: "/projects" });

    await user.type(await screen.findByLabelText("Name"), "Duplicate");
    await user.click(screen.getByRole("button", { name: /Create project/ }));

    expect(await screen.findByText("Project name is taken.")).toBeInTheDocument();
  });

  it("completes a project", async () => {
    const user = userEvent.setup();
    bff
      .signedIn()
      .on("GET", "/api/keystone/projects", { status: 200, body: [website] })
      .on("PUT", "/api/keystone/projects/p1", { status: 200, body: { ...website, status: "Completed" } });
    renderWithApp(ProjectsPage, { path: "/projects" });

    await user.click(await screen.findByRole("button", { name: /Complete/ }));

    await waitFor(() => expect(bff.requestsTo("PUT", "/api/keystone/projects/p1")).toHaveLength(1));
    expect(bff.requestsTo("PUT", "/api/keystone/projects/p1")[0].body).toEqual({
      name: "Website relaunch",
      description: "New look",
      status: "Completed",
      dueDate: "2026-12-01",
    });
  });

  it("deletes a project after confirmation", async () => {
    const user = userEvent.setup();
    vi.spyOn(window, "confirm").mockReturnValue(true);
    bff
      .signedIn()
      .on("GET", "/api/keystone/projects", { status: 200, body: [website] })
      .on("DELETE", "/api/keystone/projects/p1", { status: 204 });
    renderWithApp(ProjectsPage, { path: "/projects" });

    await user.click(await screen.findByRole("button", { name: /Delete/ }));

    await waitFor(() => expect(bff.requestsTo("DELETE", "/api/keystone/projects/p1")).toHaveLength(1));
    expect(window.confirm).toHaveBeenCalledWith('Delete "Website relaunch"?');
  });

  it("does not delete when the user cancels", async () => {
    const user = userEvent.setup();
    vi.spyOn(window, "confirm").mockReturnValue(false);
    bff.signedIn().on("GET", "/api/keystone/projects", { status: 200, body: [website] });
    renderWithApp(ProjectsPage, { path: "/projects" });

    await user.click(await screen.findByRole("button", { name: /Delete/ }));

    expect(bff.requestsTo("DELETE", "/api/keystone/projects/p1")).toHaveLength(0);
  });

  it("sends the user to re-enter their password when the BFF asks for a recent sign-in", async () => {
    const user = userEvent.setup();
    vi.spyOn(window, "confirm").mockReturnValue(true);
    const navigate = vi.spyOn(browser, "navigate").mockImplementation(() => {});
    bff
      .signedIn()
      .on("GET", "/api/keystone/projects", { status: 200, body: [website] })
      .on("DELETE", "/api/keystone/projects/p1", {
        status: 401,
        body: { error: "insufficient_user_authentication", maxAge: 300 },
      });
    renderWithApp(ProjectsPage, { path: "/projects" });

    await user.click(await screen.findByRole("button", { name: /Delete/ }));

    await waitFor(() => expect(navigate).toHaveBeenCalledWith("/api/keystone/auth/login?returnUrl=%2Fprojects&prompt=login"));
  });
});
