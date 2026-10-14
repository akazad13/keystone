import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route } from "@/routes/sessions";
import type { BffSession } from "@/lib/schemas";
import { FakeBff } from "../fakeBff";
import { renderWithApp } from "../render";

const SessionsPage = Route.options.component!;

const laptop: BffSession = {
  id: "s-laptop",
  signedInAt: "2026-10-14T08:00:00Z",
  expiresAt: "2026-10-14T16:00:00Z",
  userAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Safari/537.36",
  current: true,
};

const phone: BffSession = {
  id: "s-phone",
  signedInAt: "2026-10-13T19:00:00Z",
  expiresAt: "2026-10-14T03:00:00Z",
  userAgent: "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1",
  current: false,
};

describe("Sessions page", () => {
  let bff: FakeBff;

  beforeEach(() => {
    bff = new FakeBff();
  });

  it("asks a signed-out visitor to sign in", async () => {
    bff.signedOut();

    renderWithApp(SessionsPage, { path: "/sessions" });

    expect(await screen.findByText("Sign in to see where you are signed in.")).toBeInTheDocument();
  });

  it("lists sessions with a readable device and marks this browser", async () => {
    bff.signedIn().on("GET", "/api/keystone/sessions", { status: 200, body: [laptop, phone] });

    renderWithApp(SessionsPage, { path: "/sessions" });

    expect(await screen.findByText("Chrome on Windows")).toBeInTheDocument();
    expect(screen.getByText("Safari on iOS")).toBeInTheDocument();
    expect(screen.getAllByText("This browser")).toHaveLength(1);
    expect(screen.getByRole("button", { name: "Sign out" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "End session" })).toBeInTheDocument();
  });

  it("ends another session", async () => {
    const user = userEvent.setup();
    bff
      .signedIn()
      .on("GET", "/api/keystone/sessions", { status: 200, body: [laptop, phone] })
      .on("POST", "/api/keystone/sessions/s-phone/revoke", { status: 204 });
    renderWithApp(SessionsPage, { path: "/sessions" });

    await user.click(await screen.findByRole("button", { name: "End session" }));

    await waitFor(() => expect(bff.requestsTo("POST", "/api/keystone/sessions/s-phone/revoke")).toHaveLength(1));
    expect(bff.requestsTo("POST", "/api/keystone/sessions/s-phone/revoke")[0].headers.get("X-CSRF")).toBe("1");
  });

  it("signing out everywhere ends all sessions and the page notices", async () => {
    const user = userEvent.setup();
    vi.spyOn(window, "confirm").mockReturnValue(true);
    bff
      .signedIn()
      .on("GET", "/api/keystone/sessions", { status: 200, body: [laptop, phone] })
      .on("POST", "/api/keystone/sessions/revoke-all", () => {
        bff.signedOut(); // the BFF has deleted this browser's session too
        return { status: 200, body: { removed: 2 } };
      });
    renderWithApp(SessionsPage, { path: "/sessions" });

    await user.click(await screen.findByRole("button", { name: /Sign out everywhere/ }));

    expect(await screen.findByText("Sign in to see where you are signed in.")).toBeInTheDocument();
    expect(bff.requestsTo("POST", "/api/keystone/sessions/revoke-all")).toHaveLength(1);
  });

  it("does nothing when sign out everywhere is cancelled", async () => {
    const user = userEvent.setup();
    vi.spyOn(window, "confirm").mockReturnValue(false);
    bff.signedIn().on("GET", "/api/keystone/sessions", { status: 200, body: [laptop] });
    renderWithApp(SessionsPage, { path: "/sessions" });

    await user.click(await screen.findByRole("button", { name: /Sign out everywhere/ }));

    expect(bff.requestsTo("POST", "/api/keystone/sessions/revoke-all")).toHaveLength(0);
  });

  it("shows unknown devices", async () => {
    bff.signedIn().on("GET", "/api/keystone/sessions", { status: 200, body: [{ ...laptop, userAgent: null }] });

    renderWithApp(SessionsPage, { path: "/sessions" });

    const card = (await screen.findByText("Unknown device")).closest("div")!;
    expect(within(card).getByText("This browser")).toBeInTheDocument();
  });
});
