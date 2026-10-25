# 0004. RFC 8693 token exchange for internal services

- **Status:** Accepted
- **Date:** 2026-10-17
- **Architecture Ref:** D3 ([ARCHITECTURE.md](../../ARCHITECTURE.md#7-key-decisions))

## Context

Downstream microservices (such as `Keystone.CoreApi`) require user identity and authorization claims to authorize operations and enforce data isolation. If internal APIs directly accept upstream identity provider tokens (Zitadel access tokens):
1. Internal services become tightly coupled to Zitadel's token formats, claim schemas (such as Zitadel's project role payload), and public key discovery endpoints.
2. Replacing the IdP requires updating every internal microservice.
3. Upstream access tokens typically carry broad scopes and lifetimes that do not match internal security boundaries.

## Decision

Introduce `Keystone.IdentityService` to execute **RFC 8693 OAuth 2.0 Token Exchange**:
1. The BFF calls `POST /connect/token` on `Keystone.IdentityService` presenting the Zitadel access token as the `subject_token` (`subject_token_type = urn:ietf:params:oauth:token-type:access_token`).
2. The Identity Service validates the upstream token against Zitadel's userinfo endpoint, extracting user profile data and mapping Zitadel project roles into Keystone role claims (`Admin`, `User`).
3. The Identity Service issues a short-lived (15-minute) internal Keystone JWT (`typ: at+jwt`, `aud: core-api`).
4. `Keystone.CoreApi` validates Keystone JWTs offline using public keys published at `Keystone.IdentityService/.well-known/jwks.json`.

### Rejected Alternative

Issuing internal tokens from scratch or having the Identity Service maintain its own user credential store. Rejected because authentication belongs in the external IdP. Token exchange maintains a clean boundary: external tokens are translated into internal tokens in exactly one place.

## Consequences

- Internal services never see Zitadel tokens or know Zitadel claim structures.
- Internal tokens have a short lifetime (15 minutes) and are audience-bound strictly to `core-api`.
- The Identity Service isolates IdP-specific claim mappings to a single class (`UserService.cs`).
- Offline token verification in `CoreApi` requires no network hops on steady-state API calls.
