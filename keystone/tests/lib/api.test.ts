import { beforeEach, describe, expect, it } from "vitest";
import { api, ApiError, loginUrl, needsReauthentication } from "@/lib/api";
import { FakeBff } from "../fakeBff";

describe("loginUrl", () => {
  it("starts the login at the BFF and comes back to the given page", () => {
    expect(loginUrl("/projects")).toBe("/api/keystone/auth/login?returnUrl=%2Fprojects");
  });

  it("encodes the return URL", () => {
    expect(loginUrl("/projects?tab=a&b=c")).toBe("/api/keystone/auth/login?returnUrl=%2Fprojects%3Ftab%3Da%26b%3Dc");
  });

  it("can ask the identity provider for the password again", () => {
    expect(loginUrl("/projects", "login")).toBe("/api/keystone/auth/login?returnUrl=%2Fprojects&prompt=login");
  });

  it("defaults to the current page", () => {
    expect(loginUrl()).toBe(`/api/keystone/auth/login?returnUrl=${encodeURIComponent(window.location.pathname)}`);
  });
});

describe("api", () => {
  let bff: FakeBff;

  beforeEach(() => {
    bff = new FakeBff();
  });

  it.each(["get", "post", "put", "patch", "delete"] as const)(
    "%s sends the CSRF header and the session cookie",
    async (method) => {
      bff.on(method.toUpperCase(), "/api/keystone/projects", { status: 200, body: [] });

      await api[method]("/projects");

      const [request] = bff.requests;
      expect(request.headers.get("X-CSRF")).toBe("1");
      expect(request.credentials).toBe("include");
    }
  );

  it("sends request bodies as JSON", async () => {
    bff.on("POST", "/api/keystone/projects", { status: 201, body: { id: "1" } });

    await api.post("/projects", { name: "Website" });

    expect(bff.requests[0].headers.get("Content-Type")).toBe("application/json");
    expect(bff.requests[0].body).toEqual({ name: "Website" });
  });

  it("returns the parsed response", async () => {
    bff.on("GET", "/api/keystone/projects", { status: 200, body: [{ id: "1" }] });

    await expect(api.get("/projects")).resolves.toEqual([{ id: "1" }]);
  });

  it("returns undefined for 204 No Content", async () => {
    bff.on("DELETE", "/api/keystone/projects/1", { status: 204 });

    await expect(api.delete("/projects/1")).resolves.toBeUndefined();
  });

  it("throws an ApiError with the status and body", async () => {
    bff.on("POST", "/api/keystone/projects", { status: 400, body: { title: "Invalid" } });

    const error = await api.post("/projects", {}).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(400);
    expect((error as ApiError).message).toContain("Invalid");
  });
});

describe("needsReauthentication", () => {
  it("recognises the BFF's step-up challenge", () => {
    const error = new ApiError(401, JSON.stringify({ error: "insufficient_user_authentication", maxAge: 300 }));

    expect(needsReauthentication(error)).toBe(true);
  });

  it.each([
    ["a plain 401", new ApiError(401, "")],
    ["a 403", new ApiError(403, "insufficient_user_authentication")],
    ["a non-API error", new Error("insufficient_user_authentication")],
  ])("is false for %s", (_, error) => {
    expect(needsReauthentication(error)).toBe(false);
  });
});
