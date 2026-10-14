import { beforeEach, describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { Route } from "@/routes/me";
import { FakeBff } from "../fakeBff";
import { renderWithApp } from "../render";

const WhoAmIPage = Route.options.component!;

describe("Who am I page", () => {
  let bff: FakeBff;

  beforeEach(() => {
    bff = new FakeBff();
  });

  it("asks a signed-out visitor to sign in and does not call the Core API", async () => {
    bff.signedOut();

    renderWithApp(WhoAmIPage, { path: "/me" });

    expect(await screen.findByText("Sign in to see who the platform thinks you are.")).toBeInTheDocument();
    expect(bff.requestsTo("GET", "/api/keystone/whoami")).toHaveLength(0);
  });

  it("shows the session's claims next to the Core API's and marks the ones on one side only", async () => {
    bff
      .signedIn({
        claims: [
          { type: "sub", value: "alice-sub" },
          { type: "sid", value: "zitadel-session" },
        ],
      })
      .on("GET", "/api/keystone/whoami", {
        status: 200,
        body: {
          claims: [
            { type: "sub", value: "alice-sub" },
            { type: "role", value: "Admin" },
          ],
        },
      });

    renderWithApp(WhoAmIPage, { path: "/me" });

    expect(await screen.findByText("Admin")).toBeInTheDocument();
    const [session, coreApi] = screen.getAllByRole("table");
    const markedClaims = (table: HTMLElement) =>
      [...table.querySelectorAll("td")].filter((cell) => cell.textContent?.includes("only here")).map((cell) => cell.textContent);
    expect(markedClaims(session)).toEqual(["sid only here"]);
    expect(markedClaims(coreApi)).toEqual(["role only here"]);
  });

  it("shows time claims as dates", async () => {
    bff.signedIn({ claims: [{ type: "exp", value: "1792000000" }] }).on("GET", "/api/keystone/whoami", {
      status: 200,
      body: { claims: [] },
    });

    renderWithApp(WhoAmIPage, { path: "/me" });

    const expected = `${new Date(1792000000 * 1000).toLocaleString()} (1792000000)`;
    expect(await screen.findByText(expected)).toBeInTheDocument();
  });

  it("explains a failing Core API call", async () => {
    bff.signedIn().on("GET", "/api/keystone/whoami", { status: 502, body: "Bad gateway" });

    renderWithApp(WhoAmIPage, { path: "/me" });

    expect(await screen.findByText(/GET \/api\/keystone\/whoami failed with 502/)).toBeInTheDocument();
  });
});
