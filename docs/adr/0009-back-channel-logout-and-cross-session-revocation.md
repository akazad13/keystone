# 0009. Back-channel logout and cross-session revocation

- **Status:** Accepted
- **Date:** 2026-10-17

## Context

In enterprise environments with single sign-on (SSO), a user may log out directly at the Identity Provider, or an administrator may revoke a compromised user account in the IdP admin console. Traditional front-channel logout relies on browser redirects and iframes, which fail if the user closes the browser or if third-party cookies are blocked. Without server-to-server notification, active BFF sessions on other devices or tabs continue to execute authenticated API calls until token expiration.

## Decision

Implement **OpenID Connect Back-Channel Logout 1.0**:
- Zitadel calls the BFF server-to-server at `POST /bff/backchannel-logout` with an application/x-www-form-urlencoded signed `logout_token`.
- `LogoutTokenValidator` in `Keystone.Bff` validates the logout token:
  1. Signature verification against Zitadel's JWKS.
  2. Claims verification (`iss`, `aud`, `exp`, must contain `events` with `http://schemas.openid.net/event/backchannel-logout`, and must not contain a `nonce`).
  3. Replay attack protection: checks and caches the `jti` in Redis (`keystone:bff:logout-jti:{jti}`) for token lifetime + 5 minutes.
- When `sid` is present in the token, the BFF looks up all sessions created from that Zitadel session using the Redis set `keystone:bff:session:sid:{sid}` and deletes them.
- When `sid` is absent, the BFF revokes all sessions belonging to the user (`sub`) via `keystone:bff:session:sub:{sub}`.
- Active SPA tabs identify the ended session on their next API interaction or window focus via `useAuth` hook and redirect to the unauthenticated view.

### Rejected Alternative

Relying exclusively on front-channel logout or session timeout. Rejected because front-channel logout fails when the user does not follow the full browser redirect chain, leaves open tabs on other machines, or when an administrative revocation occurs at the identity provider.

## Consequences

- SSO session terminations and administrative user blocks take effect immediately across all active BFF instances.
- Replay attacks are prevented via distributed Redis `jti` caching.
- Eliminates reliance on client-side browser cookies or iframes for logout propagation.
