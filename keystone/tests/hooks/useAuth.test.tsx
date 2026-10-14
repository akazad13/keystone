import { beforeEach, describe, expect, it } from "vitest";
import { waitFor } from "@testing-library/react";
import { useAuth } from "@/hooks/useAuth";
import { FakeBff } from "../fakeBff";
import { renderHookWithQueries } from "../render";

describe("useAuth", () => {
  let bff: FakeBff;

  beforeEach(() => {
    bff = new FakeBff();
  });

  it("returns the user of the BFF session", async () => {
    bff.signedIn({ name: "Alice Example" });

    const { result } = renderHookWithQueries(() => useAuth());

    await waitFor(() => expect(result.current.isAuthenticated).toBe(true));
    expect(result.current.user?.name).toBe("Alice Example");
  });

  it("treats 401 as signed out, not as an error", async () => {
    bff.signedOut();

    const { result } = renderHookWithQueries(() => useAuth());

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.isAuthenticated).toBe(false);
    expect(result.current.user).toBeNull();
    expect(result.current.isError).toBe(false);
  });

  it("reports other failures as errors", async () => {
    bff.on("GET", "/api/keystone/me", { status: 500 });

    const { result } = renderHookWithQueries(() => useAuth());

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.isAuthenticated).toBe(false);
  });

  it("notices a session that ended elsewhere (back-channel logout, sign out everywhere)", async () => {
    bff.signedIn();
    const { result, queryClient } = renderHookWithQueries(() => useAuth());
    await waitFor(() => expect(result.current.isAuthenticated).toBe(true));

    bff.signedOut();
    await queryClient.refetchQueries({ queryKey: ["auth", "me"] });

    await waitFor(() => expect(result.current.isAuthenticated).toBe(false));
    expect(result.current.user).toBeNull();
  });

  it("signs out with a POST to the BFF", async () => {
    bff.signedOut();

    const { result } = renderHookWithQueries(() => useAuth());

    expect(result.current.logoutUrl).toBe("/api/keystone/auth/logout");
  });
});
