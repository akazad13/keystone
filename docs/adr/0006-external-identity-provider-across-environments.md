# 0006. External identity provider across all environments

- **Status:** Accepted
- **Date:** 2026-10-17
- **Architecture Ref:** D5 ([ARCHITECTURE.md](../../ARCHITECTURE.md#7-key-decisions))

## Context

Running and managing an enterprise identity provider requires significant operational overhead, including PostgreSQL storage, cryptographic key rotation, passkey and multi-factor authentication enrollment, email delivery, and user management consoles. For staging and production environments, the platform needs a secure identity provider without taking on the operational burden of hosting and patching an IdP internally.

## Decision

The identity provider is **external** in all deployment environments:
- **Local Development:** A local containerized instance of Zitadel v4 running via Docker Compose (`zitadel/docker-compose.yml`) configured automatically via `zitadel/setup.ps1`.
- **Azure Production:** A managed Zitadel Cloud instance (or any external enterprise OIDC provider like Microsoft Entra ID or Auth0).
- All environment-specific identity settings are passed as Aspire parameters (`oidc-authority`, `oidc-client-id`, `oidc-client-secret`).

### Rejected Alternative

Deploying Zitadel container instances directly inside Azure Container Apps environment. Rejected because running an identity provider in-cluster requires maintaining dedicated PostgreSQL databases, managing TLS certificates, executing database schema migrations, and handling high availability for authentication infrastructure.

## Consequences

- The platform relies on Zitadel Cloud's managed infrastructure in production for high availability, MFA, and user administration.
- Local development remains entirely self-contained using the local Docker Compose stack without external cloud dependencies.
- Switching between local and cloud providers is purely a parameter configuration change in `Keystone.AppHost`.
