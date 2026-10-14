import { beforeEach, describe, expect, it, vi } from "vitest";
import { api, ApiError, loginUrl } from "@/lib/api";
import { browser } from "@/lib/navigation";
import { FakeBff } from "../fakeBff";

describe("loginUrl", () => {
  it("starts the login at the BFF under the admin prefix", () => {
    expect(loginUrl("/admin/users")).toBe("/api/keystone-admin/auth/login?returnUrl=%2Fadmin%2Fusers");
  });
});

describe("api", () => {
  let bff: FakeBff;

  beforeEach(() => {
    bff = new FakeBff();
  });

  it.each(["get", "post", "put", "delete"] as const)("%s sends the CSRF header and the session cookie", async (method) => {
    bff.on(method.toUpperCase(), "/api/keystone-admin/admin/users", { status: 200, body: [] });

    await api[method]("/admin/users");

    const [request] = bff.requests;
    expect(request.headers.get("X-CSRF")).toBe("1");
    expect(request.credentials).toBe("include");
  });

  it("sends request bodies as JSON", async () => {
    bff.on("POST", "/api/keystone-admin/things", { status: 200, body: {} });

    await api.post("/things", { name: "x" });

    expect(bff.requests[0].headers.get("Content-Type")).toBe("application/json");
    expect(bff.requests[0].body).toEqual({ name: "x" });
  });

  it("throws an ApiError with the status and body", async () => {
    bff.on("GET", "/api/keystone-admin/admin/users", { status: 403, body: "Forbidden by policy" });

    const error = await api.get("/admin/users").catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(403);
    expect((error as ApiError).message).toContain("Forbidden by policy");
  });

  it("sends the user to sign in when an API call finds no session", async () => {
    const navigate = vi.spyOn(browser, "navigate").mockImplementation(() => {});
    bff.on("GET", "/api/keystone-admin/admin/users", { status: 401 });

    await expect(api.get("/admin/users")).rejects.toMatchObject({ status: 401 });

    expect(navigate).toHaveBeenCalledWith(loginUrl());
  });

  it("does not redirect for /me, so the app can show its own sign-in screen", async () => {
    const navigate = vi.spyOn(browser, "navigate").mockImplementation(() => {});
    bff.on("GET", "/api/keystone-admin/me", { status: 401 });

    await expect(api.get("/me")).rejects.toMatchObject({ status: 401 });

    expect(navigate).not.toHaveBeenCalled();
  });
});
