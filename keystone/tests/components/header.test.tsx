import { beforeEach, describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { Header } from "@/components/layout/header";
import { SignInPrompt } from "@/components/auth/SignInPrompt";
import { FakeBff } from "../fakeBff";
import { renderWithApp } from "../render";

describe("Header", () => {
  let bff: FakeBff;

  beforeEach(() => {
    bff = new FakeBff();
  });

  it("offers sign-in when there is no session", async () => {
    bff.signedOut();

    renderWithApp(Header);

    const signIn = await screen.findByRole("button", { name: "Sign in" });
    expect(signIn.closest("a")).toHaveAttribute("href", expect.stringMatching(/^\/api\/keystone\/auth\/login\?returnUrl=/));
  });

  it("shows the user and signs out with a form POST (a link would let any site sign the user out)", async () => {
    bff.signedIn({ name: "Alice Example" });

    renderWithApp(Header);

    expect(await screen.findByText("Alice Example")).toBeInTheDocument();
    expect(screen.getByText("AE")).toBeInTheDocument();
    const form = screen.getByRole("button", { name: "Sign out" }).closest("form")!;
    expect(form).toHaveAttribute("method", "post");
    expect(form).toHaveAttribute("action", "/api/keystone/auth/logout");
  });

  it("falls back to the email when the session has no name", async () => {
    bff.signedIn({ name: null, email: "alice@example.com" });

    renderWithApp(Header);

    expect(await screen.findByText("alice@example.com")).toBeInTheDocument();
  });
});

describe("SignInPrompt", () => {
  it("links to the BFF login and returns to the current page", async () => {
    new FakeBff();

    renderWithApp(() => <SignInPrompt message="Please sign in." />);

    expect(await screen.findByText("Please sign in.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sign in" }).closest("a")).toHaveAttribute(
      "href",
      `/api/keystone/auth/login?returnUrl=${encodeURIComponent(window.location.pathname)}`
    );
  });
});
