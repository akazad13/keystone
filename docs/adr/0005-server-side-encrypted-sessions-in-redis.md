# 0005. Server-side encrypted sessions in Redis

- **Status:** Accepted
- **Date:** 2026-10-17
- **Architecture Ref:** D4 ([ARCHITECTURE.md](../../ARCHITECTURE.md#7-key-decisions))

## Context

ASP.NET Core's default cookie authentication serializes the complete authentication ticket (claims, access tokens, refresh tokens) directly into an encrypted client-side cookie. In a system utilizing OpenID Connect with multiple tokens, this cookie easily exceeds 4 KB to 8 KB, causing significant network overhead on every HTTP request. More critically, client-side cookies cannot be revoked server-side prior to expiration, making immediate session termination, multi-device management, and back-channel logout impossible.

## Decision

Store user session tickets server-side in **Redis** via `RedisTicketStore` (`ITicketStore` implementation in `Keystone.Bff`):
- The browser cookie (`__Host-keystone`) contains only an opaque, cryptographically random session identifier.
- The session payload in Redis contains claims, Zitadel access token, refresh token, and ID token, encrypted with ASP.NET Core Data Protection.
- Sessions are indexed in Redis sets by `sid` (`keystone:bff:session:sid:{sid}`) and `sub` (`keystone:bff:session:sub:{sub}`).
- Cached internal Keystone JWTs are stored alongside the session at `keystone:bff:session:{id}:core-api-token`.

### Rejected Alternative

Cookie-only storage (ASP.NET Core default). Rejected due to large cookie sizes, inability to revoke sessions on demand, and lack of support for OpenID Connect back-channel logout.

## Consequences

- The browser cookie is tiny (under 100 bytes) and never leaks tokens or claims to the client.
- Sessions can be instantly enumerated, inspected, and revoked per-session or for all sessions of a user (`POST /api/{app}/sessions/{id}/revoke`).
- Upstream back-channel logout tokens from Zitadel can locate and delete all matching sessions immediately using Redis `sid` indices.
- Requires Redis as a shared dependency for the BFF layer.
