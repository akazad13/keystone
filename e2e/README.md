# End-to-end tests

A real browser against the real stack: Zitadel in Docker, and Keystone (BFF, Identity Service, Core API, SQL Server, Redis, both React apps) under the Aspire AppHost. Nothing is faked; the tests sign in through Zitadel's login UI with the local test users.

The unit tests (`dotnet test`, `npm test` in each app) cover each piece in isolation. These tests check that the pieces work together: the OIDC redirects, Zitadel's back-channel logout reaching the BFF, the token exchange, cookies, and roles assigned in Zitadel.

## Run

1. Start Zitadel and configure it once (see [docs/local-development.md](../docs/local-development.md)):

   ```powershell
   cd zitadel; docker compose up -d --wait; pwsh ./setup.ps1; cd ..
   ```

2. Start Keystone and wait until <https://localhost:7003> answers:

   ```powershell
   dotnet run --project Keystone.AppHost
   ```

3. In another terminal:

   ```powershell
   cd e2e
   npm install
   npm test              # headless; npm run test:headed to watch
   npm run report        # HTML report, with traces of failed tests
   ```

The tests check the stack is up before starting and explain what is missing if it is not. A full run takes about 5 minutes.

## What is covered

| Spec | Scenarios |
|------|-----------|
| `login.spec.ts` | Sign in from a protected page and come back to it; the Core API sees the user through a Keystone token (issuer, audience, role) that describes the same `sub` as the Zitadel session; the session cookie is `__Host-`, HttpOnly, Secure and SameSite=Lax; no JWT ever reaches the browser (responses, storage); `returnUrl` cannot redirect to another site; POST without `X-CSRF` is rejected |
| `logout.spec.ts` | RP-initiated logout ends the Keystone and the Zitadel session (next login asks for the login name); the admin app signs out to the admin app; **back-channel logout**: signing out at Zitadel ends the Keystone session, only for that Zitadel session (another browser stays signed in), and an open tab notices when the user comes back to it |
| `projects.spec.ts` | Create, complete and delete a project through the UI; validation in the browser and in the Core API; another user cannot see, read or change a project even with its id |
| `admin.spec.ts` | A user without the Admin role (bob) sees "Admins only"; an admin (alice) sees live numbers and the user list with roles; deep links into the admin app |
| `sessions.spec.ts` | Two browsers listed, end the other one, sign out everywhere; ending Keystone sessions keeps the Zitadel session (signing in again is silent) |
| `step-up.spec.ts` | `prompt=login` makes Zitadel ask for credentials despite a valid SSO session, and `auth_time` moves forward; without it, signing in again is silent |

Not covered here, because they need minutes of waiting or a reconfigured Zitadel, and the unit tests cover them: the 5 minute limit before a DELETE needs a fresh sign-in (BFF `RecentAuthenticationTests`, `CoreApiProxyTests`), and refreshing an expired Zitadel access token (BFF `TokenRefresherTests`).

## How it works

* **Configuration** (`support/environment.ts`): the test users' password is read from `zitadel/setup.ps1` (`$TestPassword`) and the BFF's client id from the AppHost user secrets, both written by `setup.ps1`. Override with `E2E_PASSWORD`, `E2E_CLIENT_ID`, `E2E_BASE_URL` (default `https://localhost:7003`) and `E2E_ZITADEL_URL` (default `http://localhost:8080`).
* **Browser**: Microsoft Edge, which Windows already has, so nothing is downloaded. `E2E_BROWSER_CHANNEL=chrome` uses Chrome; `E2E_BROWSER_CHANNEL=` (empty) uses Playwright's Chromium after `npx playwright install chromium`.
* **One test at a time**: the tests share one Zitadel, Redis and database, and some end every session of a test user, so they run serially. Each test uses a fresh browser context (its own cookies and Zitadel session).
* **Helpers** (`support/keystone.ts`): `signIn` (handles both Zitadel's login UI and silent single sign-on), `signOutAtZitadel` (ends the Zitadel session directly, which triggers the back-channel logout), and `callApi` (calls the BFF from the page with the session cookie, like the apps do).
* **Data**: projects get unique names and are deleted again; the tests do not depend on what earlier runs left behind.
