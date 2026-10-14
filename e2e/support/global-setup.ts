import { environment } from "./environment";

/** Fails fast, with instructions, when the stack is not running. */
export default async function globalSetup() {
  process.env.NODE_TLS_REJECT_UNAUTHORIZED ??= "0"; // the dev certificate, for this check only

  const problems: string[] = [];

  try {
    const discovery = await fetch(`${environment.zitadelUrl}/.well-known/openid-configuration`);
    if (!discovery.ok) problems.push(`Zitadel discovery answered ${discovery.status}.`);
  } catch {
    problems.push(`Zitadel is not reachable at ${environment.zitadelUrl}. Start it: cd zitadel; docker compose up -d --wait`);
  }

  try {
    const me = await fetch(`${environment.baseUrl}/api/keystone/me`);
    if (me.status !== 401) problems.push(`The BFF answered ${me.status} for an anonymous /me (expected 401).`);
  } catch {
    problems.push(`The BFF is not reachable at ${environment.baseUrl}. Start Keystone: dotnet run --project Keystone.AppHost`);
  }

  // Reading these early turns a missing setup.ps1 run into one clear message.
  try {
    void environment.password;
    void environment.clientId;
  } catch (error) {
    problems.push((error as Error).message);
  }

  if (problems.length > 0) {
    throw new Error(`The end-to-end tests need the running stack (see e2e/README.md):\n- ${problems.join("\n- ")}`);
  }
}
