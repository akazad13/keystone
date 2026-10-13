# Build along

Small features that each push you through a different part of the architecture. Do them in order: each one builds on the previous. Every feature lists what you will learn, hints about where to work, and how to know you are done. The hints point the way without giving the full solution.

Before you start: get the app running ([local-development.md](local-development.md)), and read [ARCHITECTURE.md](../ARCHITECTURE.md) sections 1 to 4.

| # | Feature | Layers touched | OIDC focus | Size |
|---|---------|----------------|------------|------|
| 1 | "Who am I?" page | SPA, BFF, Core API | Session claims vs. internal token claims | S |
| 2 | My projects, end to end | SPA, Core API, SQL, Redis cache | `sub` as the user key | M |
| 3 | Admin: real user list | Admin SPA, Core API | Roles from the IdP to the API | S |
| 4 | Keep users signed in: token refresh | BFF | Refresh tokens, rotation, races | M |
| 5 | "Your sessions" and sign out everywhere | BFF, SPA | `sid` vs `sub`, back-channel logout | M |
| 6 | Re-authenticate before deleting | BFF, SPA | `prompt`, `max_age`, `auth_time` | M |
| 7 | Ship it to Azure and watch it | AppHost, Azure | Redirect URIs in a real environment | M |

---

## 1. "Who am I?" page

**Goal.** A portal page at `/me` showing two lists side by side: the claims in the BFF session, and the claims the Core API sees in the Keystone token.

**You will learn** that there are two token worlds. The session holds Zitadel's view of the user (`sid`, `amr`, `auth_time`...). The Core API only sees what the Identity Service chose to put in the Keystone token (`role`, `aud=core-api`...).

**Hints**

* Core API: add `GET /api/me` (new file next to `Endpoints/ProjectEndpoints.cs`) that returns `user.Claims` as `{ type, value }`. Protect it with the `User` policy.
* Portal: add `src/routes/me.tsx`. The TanStack Router plugin picks up the new file and regenerates `routeTree.gen.ts` when Vite runs. Use `useAuth()` for the session claims and `api.get("/me")`... careful: `/api/keystone/me` is the **BFF's** endpoint. Name the Core API one differently (for example `GET /api/whoami`, reached as `/api/keystone/whoami`) or you will never reach the Core API.
* Add a sidebar link in `src/components/layout/sidebar.tsx`.

**Done when** both lists render, `role` appears only on the Core API side, and `sid` only on the session side. Bonus: explain why `aud` differs.

---

## 2. My projects, end to end

**Goal.** The portal's **Projects** page lists the signed-in user's projects and lets them create one. Today `POST /api/projects` trusts an `OwnerId` sent by the client; fix that.

**You will learn** how a user from the IdP becomes a row in your database, why the owner must come from the token and never from the request body, how EF Core migrations flow into Azure, and how the Redis cache in `ProjectService` must be invalidated per user.

**Hints**

* Core API: create a `CurrentUser` service that reads `sub`, `email` and `name` from `HttpContext.User` and finds or creates the `AppUser` row (`ExternalId = sub`). This is "just-in-time provisioning".
* Remove `OwnerId` from `CreateProjectRequest`; set it from `CurrentUser`. Make `GET /api/projects` return only the caller's projects (admins could see all).
* The cache key `projects:all` is now wrong (it would show one user's projects to another). Make it per user.
* Add a property, for example `DueDate`, then `dotnet ef migrations add AddProjectDueDate --project Keystone.CoreApi --output-dir Data/Migrations`. Restart: the migration applies itself.
* Portal: a `src/routes/projects.tsx` page with TanStack Query (`useQuery` for the list, `useMutation` + `invalidateQueries` for create) and a react-hook-form + zod form. `api.post` already sends `X-CSRF`.

**Done when** alice and bob each see only their own projects, a project created by alice has her `AppUser.Id` as owner, and removing the `X-CSRF` header from `api.ts` makes creation fail with 400.

---

## 3. Admin: real user list

**Goal.** The admin dashboard's **Users** page shows real users from the `Users` table instead of calling a non-existent `/users` endpoint. Only admins may see it.

**You will learn** how a role assigned in the Zitadel console travels: Zitadel userinfo > Identity Service mapping (`UserService.GetZitadelRoles`) > `role` claim in the Keystone token > `RequireAuthorization("Admin")` in the Core API.

**Hints**

* Core API: `GET /api/admin/users` with the `Admin` policy, returning the `AppUser` rows (feature 2 creates them). A `/api/admin` group makes "admin only" visible in the URL.
* Admin SPA: adapt `src/routes/users.tsx` and `src/lib/schemas.ts` to the real shape. Handle `403` with a friendly "admins only" message instead of an error.
* Sign in as bob: 403. In the Zitadel console, give bob the `Admin` role. Within 15 minutes his next exchanged token carries it, or sign out and in again to get it at once. Why the delay? (Hint: the cached Keystone token.)

**Done when** alice sees the list, bob sees the friendly message, and you can explain how long a role change takes to apply and why.

---

## 4. Keep users signed in: token refresh

**Goal.** When Zitadel's access token expires, the BFF uses the refresh token to get a new one, silently. Today `SessionTokenService` gives up and the user has to sign in again.

**You will learn** the `refresh_token` grant, refresh token rotation (Zitadel returns a *new* refresh token and the old one stops working), and the race that rotation causes when two requests refresh at once.

**Set up a way to test it.** In the Zitadel console (instance or organization settings > OIDC token lifetimes), set the access token lifetime to a few minutes.

**Hints**

* Where: `Keystone.Bff/Services/SessionTokenService.cs`, at the comment about refreshing.
* The token endpoint URL comes from discovery: inject `IOptionsMonitor<OpenIdConnectOptions>` and use `options.ConfigurationManager` (exactly like `LogoutTokenValidator`). Post `grant_type=refresh_token`, `refresh_token`, `client_id`, `client_secret`.
* Save **all** returned tokens (`access_token`, the new `refresh_token`, a new `expires_at`) back into the session. Pitfall: `HttpContext.SignInAsync` with a session store creates a **new** session id, so requests already in flight with the old cookie would fail. Update the stored ticket in place with `RedisTicketStore.RenewAsync(sessionId, ticket)` instead.
* Race: two API calls arrive together, both see an expired token, both refresh, the second refresh uses a rotated (dead) token. Prevent it with a short Redis lock per session (`SET key value NX PX 10000`), or by re-reading the session after acquiring the lock.
* If the refresh fails (`invalid_grant`), the session is over: delete it so the user gets a clean 401.

**Done when** with a 5-minute access token you can keep using the portal for 15 minutes without signing in, and the Aspire trace shows a call to Zitadel's token endpoint roughly every 5 minutes.

---

## 5. "Your sessions" and sign out everywhere

**Goal.** A portal page listing the user's active Keystone sessions (each browser where they are signed in), with buttons to end one session or all of them.

**You will learn** the difference between `sid` (one Zitadel session, shared by the apps you signed in to with it) and `sub` (the user, across all sessions), and how back-channel logout and your own revocation interact.

**Hints**

* BFF: add endpoints under each app prefix, for example `GET /api/{app}/sessions` and `POST /api/{app}/sessions/{id}/revoke` (require `X-CSRF`, already enforced for POST). `RedisTicketStore` already indexes sessions by `sub`; add a method that lists them with `Properties.IssuedUtc` and `ExpiresUtc`.
* Store the user agent at login so the list is readable (hint: `OnTicketReceived` or `OnSigningIn` events, `Properties.Items`).
* "Sign out everywhere" can call `RemoveBySubjectAsync`. Should it also end the Zitadel sessions? Find out what Zitadel's session API offers, and decide.
* Experiment: sign in from your normal browser and from a private window. End one from the other. Then end the Zitadel session from the console (Users > alice > Sessions, or an end_session call as in [oidc-guide.md 7.4](oidc-guide.md#74-try-it)) and see which Keystone sessions disappear.

**Done when** you can explain, with a Redis key listing as evidence, which sessions a `sid`-based back-channel logout removes and which a `sub`-based one would.

---

## 6. Re-authenticate before deleting

**Goal.** Deleting a project requires that the user entered their password in the last 5 minutes. Otherwise the SPA sends them through login again first.

**You will learn** the OIDC request parameters `prompt=login` and `max_age`, and the `auth_time` claim (also look up `acr` and `amr`, for "how" the user authenticated).

**Hints**

* BFF login endpoint: accept an optional `prompt=login`, passed to the OIDC handler through `AuthenticationProperties.Parameters` (`OpenIdConnectParameterNames.Prompt`).
* The ID token's `auth_time` lands in the session claims (look at feature 1's page). After a re-login it is fresh.
* Decide where to enforce: the BFF (it knows `auth_time`) or the Core API (it does not; you would need to carry `auth_time` into the Keystone token in the Identity Service). Try the BFF first: for `DELETE /api/*/projects/*`, return 401 with a body such as `{ "error": "reauthentication_required" }` when `auth_time` is older than 5 minutes.
* SPA: on that error, go to `loginUrl()` with `prompt=login` and a `returnUrl` back to the project.

**Done when** deleting right after login works, deleting after 5 minutes takes you through Zitadel's password prompt (even though you have an SSO session) and then completes.

---

## 7. Ship it to Azure and watch it

**Goal.** Run Keystone in Azure following [azure-deployment.md](azure-deployment.md), then follow one request across services in the deployed Aspire dashboard.

**You will learn** what changes between local and cloud (redirect URIs, TLS termination and forwarded headers, managed identities instead of passwords, one signing key shared by replicas) and what stays the same (all application code).

**Hints**

* Before deploying, run `aspire publish` and read the generated Bicep for `bff` and `core-api`. Find the managed identity, the role assignments for SQL and Redis, and the Container App secrets.
* After deploying, scale the BFF to 2 replicas in the portal and keep using the app. Why does your session survive whichever replica answers? (Hint: two things in Redis.)
* Trigger a back-channel logout from Zitadel Cloud's console and find the `POST /bff/backchannel-logout` trace.
* Stretch: move `identity-signing-key` into Key Vault (`AddAzureKeyVault`) and read it with the managed identity.

**Done when** a signed-in request works in Azure, you have found its trace across `bff`, `identity-service` and `core-api`, and you have run `aspire destroy`.
