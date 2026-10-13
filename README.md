# Keystone

A learning project for building secure web platforms on .NET and Azure: a Backend for Frontend that handles OpenID Connect (including back-channel logout), two React frontends served from a single origin, an internal token service, and a business API, all orchestrated by Aspire and deployable to Azure Container Apps with one command.

```
Browser ──cookie──> BFF ──Keystone JWT──> Core API ──> SQL / Redis
                     │  └─token exchange──> Identity Service
                     └──OIDC──> Zitadel <──back-channel logout──┘
```

## Quick start

Prerequisites: .NET 10 SDK, Node.js 22+, Docker Desktop, PowerShell 7, and a trusted dev certificate (`dotnet dev-certs https --trust`).

```powershell
# 1. Local identity provider
cd zitadel
docker compose up -d --wait
pwsh ./setup.ps1          # project, OIDC app, test users alice/bob, user secrets
cd ..

# 2. Frontend dependencies
cd keystone; npm install; cd ..
cd keystone-admin; npm install; cd ..

# 3. Run everything
dotnet run --project Keystone.AppHost
```

Open <https://localhost:7003> (portal) or <https://localhost:7003/admin/> (admin) and sign in as `alice` (Admin) or `bob` (User); the password is in `zitadel/setup.ps1`. The Aspire dashboard link is printed in the console.

## Documentation

| Read this | To |
|-----------|----|
| [ARCHITECTURE.md](ARCHITECTURE.md) | Understand the components, flows, tokens, state and the decisions behind them |
| [docs/local-development.md](docs/local-development.md) | Set up, run, debug and inspect everything locally |
| [docs/oidc-guide.md](docs/oidc-guide.md) | Follow login, API calls, logout and back-channel logout step by step, in the code and in your browser |
| [docs/azure-deployment.md](docs/azure-deployment.md) | Deploy to Azure with `aspire deploy` and Zitadel Cloud |
| [docs/build-along.md](docs/build-along.md) | Build features yourself, from a "who am I" page to token refresh and step-up authentication |

## Repository layout

```
Keystone.AppHost/            Aspire AppHost: resources, wiring, Azure mapping
Keystone.ServiceDefaults/    Telemetry, health checks, resilience, service discovery
Keystone.Bff/                Public entry point: OIDC client, sessions, proxy, static SPAs
Keystone.IdentityService/    Internal token service (RFC 8693 token exchange, JWKS)
Keystone.CoreApi/            Business API (EF Core, SQL, Redis cache)
keystone/                    Customer portal (React + Vite), served at /
keystone-admin/              Admin dashboard (React + Vite), served at /admin/
zitadel/                     Local Zitadel (official compose) and setup script
docs/                        Guides
Directory.Packages.props     All NuGet package versions (central package management)
```
