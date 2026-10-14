# Local development

Everything runs on your machine: Zitadel in Docker, and Keystone under the Aspire AppHost (SQL Server and Redis containers, three .NET services, two Vite dev servers).

## Prerequisites

| Tool | Version | Check |
|------|---------|-------|
| .NET SDK | 10.0 | `dotnet --version` |
| Node.js | 22 or later | `node --version` |
| Docker Desktop | running | `docker info` |
| PowerShell | 7 or later (for `zitadel/setup.ps1`) | `pwsh --version` |
| Trusted HTTPS dev certificate | | `dotnet dev-certs https --trust` |
| Aspire CLI (optional locally, needed to deploy) | 13.x | `aspire --version` |

Install the Aspire CLI on Windows with:

```powershell
irm https://aspire.dev/install.ps1 | iex
```

## First run

### 1. Start Zitadel

```powershell
cd zitadel
docker compose up -d --wait
```

This starts Zitadel's official stack: a Traefik proxy on port 8080, the Zitadel API, the Zitadel login UI and PostgreSQL. The first start takes a minute while Zitadel initialises its database.

* Console: <http://localhost:8080/ui/console>. Zitadel's built-in administrator is `zitadel-admin@zitadel.localhost` with password `Password1!` (local defaults from the official compose file).
* Discovery document: <http://localhost:8080/.well-known/openid-configuration>

### 2. Configure Zitadel for Keystone

```powershell
pwsh ./setup.ps1
```

The script is safe to run again. It creates the **Keystone** project with roles `Admin` and `User`, the **keystone-bff** OIDC application, and two test users, then stores the client id and secret in the AppHost's user secrets. Read the script: each step corresponds to a screen in the Zitadel console.

| User | Role | Password |
|------|------|----------|
| `alice` | Admin | see `$TestPassword` in `zitadel/setup.ps1` |
| `bob` | User | same |

Running it again issues a **new** client secret and updates the user secrets.

### 3. Install the frontend dependencies

```powershell
cd ..\keystone; npm install
cd ..\keystone-admin; npm install
cd ..
```

### 4. Run Keystone

```powershell
dotnet run --project Keystone.AppHost
```

or, with the Aspire CLI, `aspire run`. The console prints the dashboard link (with a login token), for example `https://localhost:17888/login?t=...`.

Then open **<https://localhost:7003>**: the BFF. Always use this URL. The Vite dev servers have their own ports, but opening them directly bypasses the BFF, so login and API calls will not work there.

| URL | What |
|-----|------|
| <https://localhost:7003/> | Customer portal (`keystone`) |
| <https://localhost:7003/admin/> | Admin dashboard (`keystone-admin`) |
| <https://localhost:17888> | Aspire dashboard: resources, console logs, structured logs, traces, metrics |
| <http://localhost:8080/ui/console> | Zitadel console |

Hot reload works: edits to the React apps show up immediately (Vite's HMR goes through the BFF), and the .NET services can be restarted from the dashboard.

## Everyday tasks

### Stop and start

* Stop Keystone: `Ctrl+C` in the AppHost terminal. SQL data survives in a Docker volume.
* Stop Zitadel: `docker compose down` in `zitadel/`. Its data survives too.
* Reset Zitadel completely: `docker compose down -v`, then `docker compose up -d --wait` and `pwsh ./setup.ps1` again.

### Preview the production build

`pwsh ./scripts/preview-production.ps1` builds both SPAs and runs the BFF the way it runs in Azure (Production mode, static files from `artifacts/wwwroot`, no Vite proxy). See [azure-deployment.md, step 0](azure-deployment.md#step-0-preview-the-production-build-locally).

### Run the tests

```powershell
dotnet test
```

Three xUnit projects under `tests/`, all fully in memory (no Docker, Zitadel, SQL Server or Redis):

| Project | Covers | How it fakes the outside world |
|---------|--------|--------------------------------|
| `Keystone.Bff.Tests` | Session store, token refresh (incl. concurrent refresh), token exchange and its cache, logout token validation (every step of spec section 2.6), CSRF check, step-up check, the proxy (real YARP with the routes from `appsettings.json`: path rewrite, token attached, cookies and browser `Authorization` dropped), login/logout/`me`, sessions and back-channel logout endpoints | `FakeRedis` (an in-memory `IDatabase` built with `DispatchProxy`), `TestIdentityProvider` (signing key, logout tokens, stub token endpoint), test authentication handlers on a `TestServer`, a recording `IForwarderHttpClientFactory` in place of the network |
| `Keystone.IdentityService.Tests` | Signing key (dev fallback, required outside Development, PEM or base64 PEM, RFC 7638 `kid` identical across replicas, no private parts in JWKS), token issuing and validation, Zitadel userinfo and role mapping, `/connect/token` (every RFC 8693 error case), discovery, JWKS, introspection | Real key manager and token service; Zitadel replaced by a stub HTTP handler (`UserService`) or a fake `IUserService` on a `TestServer` |
| `Keystone.CoreApi.Tests` | Project service (owner isolation, cache), just-in-time user provisioning (incl. the insert race), validators, project/admin/whoami endpoints with the real policies, and access token validation (the real JwtBearer setup against the real Identity Service: accepted tokens, wrong audience/issuer/key/type, expired, unsigned, claim names, keys fetched once) | SQLite in-memory (enforces the unique and foreign keys), `MemoryDistributedCache`, a header-based test authentication scheme on a `TestServer`; for token validation, the Identity Service's discovery and JWKS endpoints on a second `TestServer` |

Mocking libraries are not used (Moq is banned in this organisation); fakes are small hand-written classes in each project's `Fakes` folder.

The React apps have their own tests (Vitest, React Testing Library, jsdom):

```powershell
cd keystone; npm test         # or: npm run test:watch
cd ..\keystone-admin; npm test
```

| App | Covers |
|-----|--------|
| `keystone/tests` | `api.ts` (CSRF header and cookie on every call, errors, step-up detection, login URLs), `useAuth` (401 means signed out; a session ended elsewhere is noticed), form schemas, header and sign-in prompt, and the Projects, Sessions and Who am I pages against a fake BFF |
| `keystone-admin/tests` | `api.ts` (CSRF header, redirect to sign-in on 401 except for `/me`), the root layout (sign-in screen vs. app), the Users and Dashboard pages including the 403 "Admins only" screen |

`tests/fakeBff.ts` replaces `fetch` with canned replies per `METHOD /path` and records every request, so tests assert what the BFF would have received. Pages are rendered with a real TanStack router and query client (`tests/render.tsx`). Full-page redirects go through `browser.navigate` (`src/lib/navigation.ts`) so tests can observe them; jsdom does not allow spying on `window.location`. `npm run build` type-checks the tests too.

With the stack running (Zitadel and the AppHost), the end-to-end tests drive a real browser through Zitadel's login UI: `cd e2e; npm install; npm test`. See [e2e/README.md](../e2e/README.md).

### Add a database migration

```powershell
dotnet ef migrations add <Name> --project Keystone.CoreApi --output-dir Data/Migrations
```

Migrations are applied when the Core API starts. `Data/DesignTimeDbContextFactory.cs` lets the EF tools create the context without running the app.

### Inspecting Redis

The Redis container uses TLS and a password (both generated by Aspire). This lists the BFF's keys:

```powershell
$redis = docker ps --filter name=redis --format '{{.Names}}' | Select-Object -First 1
docker exec $redis sh -c 'redis-cli --tls --insecure -a "$REDIS_PASSWORD" --no-auth-warning --scan --pattern "keystone:*"'
```

What the keys mean is described in [ARCHITECTURE.md](../ARCHITECTURE.md#42-redis-keys).

### Look at a token

* **Keystone tokens:** in the dashboard, find the Identity Service's HTTPS port (Resources), then post a token to `/internal/introspect` (form field `token`) to see whether it is valid and what it contains.
* **Zitadel tokens:** they never leave the BFF. To see the ID token's claims, call `GET /api/keystone/me` from the browser console while signed in.

### Use a different identity provider

Set the `Parameters:oidc-authority`, `Parameters:oidc-client-id` and `Parameters:oidc-client-secret` user secrets of `Keystone.AppHost` to any OIDC provider (Zitadel Cloud, Microsoft Entra ID, Auth0, Keycloak...). Register `https://localhost:7003/signin-oidc` and `https://localhost:7003/signout-callback-oidc` there. Roles are read from Zitadel's claim format in `Keystone.IdentityService/Services/UserService.cs`; another provider needs its own mapping there.

## How the pieces find each other locally

* The AppHost starts every service and injects the addresses and connection strings as environment variables. `http://identity-service` and `http://core-api` resolve through Aspire service discovery.
* In Development, the BFF's `appsettings.Development.json` adds two YARP routes that forward `/admin/**` and everything else to the Vite dev servers. In Azure those routes do not exist; the BFF serves the built files from `wwwroot` instead.
* Zitadel runs in Docker but calls the BFF for back-channel logout at `http://host.docker.internal:5003`. That is why `zitadel/.env` relaxes Zitadel's outbound deny list for local use (see the comment there).

## Troubleshooting

See [oidc-guide.md, section 9](oidc-guide.md#9-troubleshooting) for login problems. Other common issues:

| Symptom | Fix |
|---------|-----|
| `npm run build` fails with route type errors | Run `npx vite build` once; the TanStack Router plugin regenerates `src/routeTree.gen.ts` |
| Port 7003 already in use | Another BFF instance is running; stop it, or a previous AppHost did not shut down (check Task Manager for `Keystone.*`) |
| Core API keeps restarting at startup | SQL Server is still starting; it retries automatically. Check its logs in the dashboard. |
| Zitadel "Instance not found" | `ZITADEL_DOMAIN`, `ZITADEL_EXTERNALPORT` and `ZITADEL_EXTERNALSECURE` in `zitadel/.env` must match the URL you use (`http://localhost:8080`) |
