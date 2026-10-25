# 0002. Single origin BFF for SPAs, APIs, and auth callbacks

- **Status:** Accepted
- **Date:** 2026-10-17
- **Architecture Ref:** D1 ([ARCHITECTURE.md](../../ARCHITECTURE.md#7-key-decisions))

## Context

Modern web platforms often separate frontend Single Page Applications (SPAs) onto static CDN domains (such as Azure Static Web Apps or Netlify) while hosting backend APIs on separate subdomains. When SPAs use cookie-based authentication against a separate backend domain, cookies must be configured with `SameSite=None` and `Secure`. Modern browsers increasingly partition or block cross-site cookies, making cross-origin session cookies unreliable. Furthermore, cross-origin setups require Cross-Origin Resource Sharing (CORS) preflight requests on unsafe methods, adding latency and attack surface.

## Decision

The Backend for Frontend (`Keystone.Bff`) serves as the **single public origin** for both SPAs (`/` for customer portal and `/admin/` for admin dashboard), all proxied API endpoints (`/api/**`), and OpenID Connect callback endpoints (`/signin-oidc`, `/signout-callback-oidc`).

- In local development, the BFF uses YARP to reverse proxy requests to the Vite dev servers.
- In production (Azure Container Apps), the BFF serves pre-built static assets from `wwwroot` and `wwwroot/admin` alongside the reverse-proxied API routes.
- The session cookie uses `SameSite=Lax` and `Secure`.

### Rejected Alternative

Hosting SPAs on separate static hosting platforms (such as Azure Static Web Apps) with the BFF on a different domain. Rejected because it forces `SameSite=None`, subjects the platform to third-party cookie restrictions, requires CORS configuration, and creates multiple separate deployment targets instead of a unified deployable unit.

## Consequences

- The session cookie is always first-party and same-origin; browsers never block it as third-party tracking.
- CORS is completely unnecessary for browser-to-backend communication.
- Deployment is simplified: one container image (`bff`) packages the proxy rules, security middleware, and frontend assets.
- The BFF must handle reverse proxying to Vite dev servers during local development.
