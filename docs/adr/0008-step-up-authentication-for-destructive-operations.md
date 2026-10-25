# 0008. Step-up authentication for destructive operations

- **Status:** Accepted
- **Date:** 2026-10-17

## Context

BFF session lifetimes typically span several hours (8-hour sliding sessions in Keystone). In modern office or remote environments, an unlocked laptop or unattended browser tab could allow an attacker or accidental action to trigger permanent destructive operations, such as project deletion (`DELETE /api/keystone/projects/{id}`). Standard session cookies provide no proof of recent user presence.

## Decision

Implement **RFC 9470 (OAuth 2.0 Step-Up Authentication Challenge)** at the BFF layer:
- The BFF inspects the `auth_time` claim present in the session ticket (transferred from the original Zitadel ID token).
- `RecentAuthentication` middleware (`Keystone.Bff/Middleware/RecentAuthentication.cs`) intercepts destructive operations (`DELETE`).
- If the session's `auth_time` is older than 5 minutes (300 seconds), the BFF intercepts the request and issues an RFC 9470 response:
  ```http
  HTTP/1.1 401 Unauthorized
  WWW-Authenticate: Bearer error="insufficient_user_authentication", error_description="Sign in again to continue", max_age=300
  ```
- The frontend client detects this error and initiates a step-up challenge redirect:
  `GET /api/keystone/auth/login?prompt=login&returnUrl=/projects`
- `prompt=login` forces the user to re-enter credentials at Zitadel regardless of an active SSO session, generating a fresh ID token with updated `auth_time`.

### Rejected Alternative

Requiring a separate password-confirmation endpoint in `Keystone.CoreApi`. Rejected because the Core API does not manage user passwords or credentials. The IdP owns credential verification; leveraging OIDC's `prompt=login` and `auth_time` claims keeps credential validation in the identity provider.

## Consequences

- Destructive operations require proof of fresh authentication within the last 5 minutes.
- Re-authentication happens seamlessly through Zitadel's login UI with automatic redirect back to the originating page.
- Downstream microservices remain protected without needing to implement custom password challenge flows.
