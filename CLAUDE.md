# Keystone: project notes for developers and AI assistants

Shared, version-controlled context for anyone working on this repository (people and Claude Code alike). The detailed explanations live in the docs; this file records the current state, the conventions and the traps that are not obvious from the code.

Keep it current: when you finish a feature, change a convention or discover a trap, update this file in the same commit.

## What this is

A learning project for understanding a secure .NET + Azure web platform end to end, with a focus on OpenID Connect (code flow + PKCE, refresh, RP-initiated logout, back-channel logout, step-up). The repository folder is still called `Multi-stage-app`; the project was renamed from MultiStageApp to **Keystone**.

| Read | For |
|------|-----|
| [README.md](README.md) | Quick start and repository layout |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Components, flows, tokens, Redis keys, configuration, decisions D1 to D6, security summary |
| [docs/local-development.md](docs/local-development.md) | Running, debugging, inspecting Redis and tokens, troubleshooting |
| [docs/oidc-guide.md](docs/oidc-guide.md) | Every OIDC flow traced through the code |
| [docs/azure-deployment.md](docs/azure-deployment.md) | Production preview and `aspire deploy` with Zitadel Cloud |
| [docs/build-along.md](docs/build-along.md) | The feature exercises 1 to 7 |

## Current state (2026-10-14)

All work is on `main` (no remote configured). One commit per feature:

| Commit | Feature | Status |
|--------|---------|--------|
| `85adab0` | Base architecture + 1. "Who am I?" page | Done, verified in the browser |
| `a9af4aa` | 2. My projects (owner-scoped CRUD, due dates) | Done, verified |
| `abdf6c5` | 3. Admin user list and live dashboard | Done, verified |
| `af55a32` | 4. Token refresh with rotation and a Redis lock | Done, verified (5 parallel calls, 1 refresh) |
| `32b8053` | 5. "Your sessions", revoke one, sign out everywhere | Done, verified |
| `25c2c4f` | 6. Step-up authentication (RFC 9470) before DELETE | Done, verified |
| `1dd7193` | 7. Production preview (`scripts/preview-production.ps1`) | Local part done |

**Not done yet:**

* The actual Azure deployment (feature 7, second half). It needs an Azure subscription and a Zitadel Cloud instance; follow `docs/azure-deployment.md` from step 1.
* Items in the hardening checklist at the end of `docs/azure-deployment.md` and the known gaps in `ARCHITECTURE.md` (for example: health probes only mapped in Development, `/connect/token` relies on network isolation only, signing key not in Key Vault).
* There are no automated tests yet. Verification so far was manual (browser + curl against local Zitadel).

## Architecture in one paragraph

The **BFF** (`Keystone.Bff`, https://localhost:7003) is the only public entry point and the single origin for both SPAs: `keystone` at `/` and `keystone-admin` at `/admin/` (proxied to Vite in Development, served from `wwwroot` in Production). It is the OIDC client of **Zitadel**, keeps tokens in server-side sessions in **Redis** (cookie `__Host-keystone` holds only a session reference), and exchanges the Zitadel access token at the internal **Identity Service** (RFC 8693) for a short-lived Keystone JWT (aud `core-api`, `at+jwt`, 15 min). YARP forwards `/api/keystone/**` and `/api/keystone-admin/**` to the **Core API** at `/api/**` with that JWT as a Bearer token. The Core API validates JWTs offline, provisions users just in time, and stores data in **SQL Server** with a Redis cache. Aspire 13.6 (`Keystone.AppHost`) wires it all locally and maps it to Azure Container Apps, Azure SQL and Azure Managed Redis.

## Conventions

* **.NET 10, central package management.** Every NuGet version is pinned exactly in `Directory.Packages.props`; `.csproj` files have `PackageReference` without versions. Do not use floating versions (`9.*` once broke restore on partially published packages).
* **Banned NuGet packages: Moq and AutoMapper.** Use hand-written fakes/stubs and explicit mapping code.
* **Writing style in code comments and docs:** no em dashes.
* **New API features** go in `Keystone.CoreApi/Endpoints`, are reached through the BFF prefixes, and are owner-scoped: the owner always comes from the token (`ICurrentUserService`), never from the request body.
* **Mutating calls from the SPAs** must send `X-CSRF: 1` (the `api` helper in `src/lib/api.ts` does it). The BFF rejects unsafe methods without it.
* **Zitadel-specific code** stays in `Keystone.IdentityService/Services/UserService.cs` (role claim mapping) so the provider can be swapped.
* **Dates:** the Core API stores UTC; a converter in `AppDbContext.ConfigureConventions` marks `DateTime` values as UTC so JSON gets a `Z`. Due dates are `DateOnly`.
* **Migrations:** `dotnet ef migrations add <Name> --project Keystone.CoreApi --output-dir Data/Migrations`; they run at Core API startup.
* **SPA routes** use TanStack Router file routes; `routeTree.gen.ts` is generated by the Vite plugin (run `npx vite build` if `tsc` complains about routes).
* **Commits:** one commit per feature or fix, message starting with the feature name.

## Traps (learned the hard way)

* **Run with the `https` launch profile.** It is first in `Keystone.AppHost/Properties/launchSettings.json` so plain `dotnet run --project Keystone.AppHost` works. The `http` profile needs `ASPIRE_ALLOW_UNSECURED_TRANSPORT`.
* **Always open the app through the BFF (port 7003),** never the Vite ports; login and API calls only work on the BFF origin.
* **Local back-channel logout** needs `ZITADEL_HTTPCLIENT_DENYLIST` relaxed in `zitadel/.env`, because Zitadel (in Docker) calls the BFF at `host.docker.internal:5003`, which its default deny list blocks.
* **Re-running `zitadel/setup.ps1` issues a new client secret** and rewrites the AppHost user secrets. Restart the AppHost afterwards.
* **Test credentials** live only in `zitadel/setup.ps1` (`$TestPassword`). Do not copy them into docs, issues or chat.
* **The login endpoint signs out an existing session first.** Otherwise a second login leaves the old Redis session orphaned.
* **`TokenRefresher` writes the renewed ticket with `ITicketStore.RenewAsync`,** not `SignInAsync`, because the request's ticket copy may be stale after another request refreshed under the lock.
* **Provisioning middleware in the Core API runs between `UseAuthentication` and `UseAuthorization`,** so `LastSeenAt` is updated even for requests that end in 403.
* **The `useAuth` hooks map a 401 from `/me` to `null`,** otherwise React Query keeps showing a user whose session ended elsewhere.
* **Admin SPA fallback in Production is `/admin/{*path:nonfile}`.** Without `nonfile`, missing JS assets get `index.html` and the admin app is blank. Use `scripts/preview-production.ps1` to test production paths before deploying.
* **Minimal API handlers returning `Task<IResult>` from a method group** may need a `(Delegate)` cast (ASP0016), otherwise the endpoint returns an empty 200.

## Local environment cheat sheet

```powershell
cd zitadel; docker compose up -d --wait; pwsh ./setup.ps1; cd ..   # first time only for setup.ps1
dotnet run --project Keystone.AppHost                               # https://localhost:7003
pwsh ./scripts/preview-production.ps1                               # Production-mode BFF locally
cd zitadel; docker compose down                                     # stop Zitadel (data kept)
```

Test users: `alice` (Admin) and `bob` (User).
