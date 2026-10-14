import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");

/**
 * The local test users' password lives in one place, zitadel/setup.ps1 ($TestPassword), which
 * creates the users. E2E_PASSWORD overrides it (e.g. for a different Zitadel).
 */
function testPassword(): string {
  if (process.env.E2E_PASSWORD) return process.env.E2E_PASSWORD;
  const script = readFileSync(resolve(repoRoot, "zitadel", "setup.ps1"), "utf8");
  const match = /^\s*\$TestPassword\s*=\s*'([^']+)'/m.exec(script);
  if (!match) throw new Error("Could not find $TestPassword in zitadel/setup.ps1; set E2E_PASSWORD instead.");
  return match[1];
}

/**
 * The BFF's client id at Zitadel, needed to end a Zitadel session directly (back-channel logout tests).
 * setup.ps1 stores it in the AppHost's user secrets; E2E_CLIENT_ID overrides it.
 */
function clientId(): string {
  if (process.env.E2E_CLIENT_ID) return process.env.E2E_CLIENT_ID;
  const secrets = execFileSync("dotnet", ["user-secrets", "list", "--project", resolve(repoRoot, "Keystone.AppHost")], {
    encoding: "utf8",
  });
  const match = /^Parameters:oidc-client-id\s*=\s*(\S+)/m.exec(secrets);
  if (!match) throw new Error("No Parameters:oidc-client-id in the AppHost user secrets; run zitadel/setup.ps1 or set E2E_CLIENT_ID.");
  return match[1];
}

export interface TestUser {
  loginName: string;
  /** What the apps show as the user's name (Zitadel's display name). */
  displayName: string;
  role: "Admin" | "User";
}

let cachedPassword: string | undefined;
let cachedClientId: string | undefined;

export const environment = {
  /** The BFF: the only origin the browser talks to for Keystone. */
  baseUrl: process.env.E2E_BASE_URL ?? "https://localhost:7003",
  zitadelUrl: process.env.E2E_ZITADEL_URL ?? "http://localhost:8080",
  browserChannel: process.env.E2E_BROWSER_CHANNEL ?? "msedge",
  get password() {
    return (cachedPassword ??= testPassword());
  },
  get clientId() {
    return (cachedClientId ??= clientId());
  },
};

/** The users zitadel/setup.ps1 creates. */
export const users = {
  alice: { loginName: "alice", displayName: "Alice Admin", role: "Admin" },
  bob: { loginName: "bob", displayName: "Bob User", role: "User" },
} satisfies Record<string, TestUser>;
