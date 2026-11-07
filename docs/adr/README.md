# Architecture Decision Records (ADRs)

This directory documents the architectural decision records for Keystone. Each ADR explains the context, forces, trade-offs, and consequences of a significant architectural choice.

| ADR | Title | Status | Date | Primary Pattern |
| :--- | :--- | :---: | :---: | :--- |
| **[0001](0001-record-architecture-decisions.md)** | Record architecture decisions | Accepted | 2026-10-17 | Documentation Process |
| **[0002](0002-single-origin-bff-for-spas-and-apis.md)** | Single origin BFF for SPAs, APIs, and auth callbacks (D1) | Accepted | 2026-10-17 | BFF, Cookie Security |
| **[0003](0003-direct-oidc-between-bff-and-idp.md)** | Direct OpenID Connect between BFF and Identity Provider (D2) | Accepted | 2026-10-17 | OIDC, Code Flow + PKCE |
| **[0004](0004-rfc-8693-token-exchange-for-internal-services.md)** | RFC 8693 token exchange for internal services (D3) | Accepted | 2026-10-17 | Token Exchange, JWT |
| **[0005](0005-server-side-encrypted-sessions-in-redis.md)** | Server-side encrypted sessions in Redis (D4) | Accepted | 2026-10-17 | Distributed Session Store |
| **[0006](0006-external-identity-provider-across-environments.md)** | External identity provider across all environments (D5) | Accepted | 2026-10-17 | Identity Provider (Zitadel) |
| **[0007](0007-aspire-13-cloud-native-orchestration-and-deployment.md)** | Cloud-native orchestration and deployment with .NET Aspire 13 (D6) | Accepted | 2026-10-17 | .NET Aspire, Azure Bicep |
| **[0008](0008-step-up-authentication-for-destructive-operations.md)** | Step-up authentication for destructive operations | Accepted | 2026-10-17 | RFC 9470, Step-Up Auth |
| **[0009](0009-back-channel-logout-and-cross-session-revocation.md)** | Back-channel logout and cross-session revocation | Accepted | 2026-10-17 | OIDC Back-Channel Logout |
| **[0010](0010-package-modernization.md)** | Solution package modernization and dependency alignment | Accepted | 2026-11-07 | Dependency Modernization |

---

## Decision Lifecycle

ADRs move through three states:
1. **Proposed:** Under team review and evaluation.
2. **Accepted:** Approved, implemented, and enforced across the codebase.
3. **Superseded:** Replaced by a newer decision (with a link to the superseding ADR).
