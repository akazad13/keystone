# 0003. Direct OpenID Connect between BFF and Identity Provider

- **Status:** Accepted
- **Date:** 2026-10-17
- **Architecture Ref:** D2 ([ARCHITECTURE.md](../../ARCHITECTURE.md#7-key-decisions))

## Context

When authenticating browser users in an enterprise architecture, the system must decide which component acts as the OpenID Connect (OIDC) Relying Party (client). One common design pattern places an internal identity service as an intermediary broker between the frontend proxy and the external IdP. Another option is having the BFF communicate directly with the IdP using standard OIDC code flow with PKCE.

## Decision

The BFF (`Keystone.Bff`) communicates **directly** with the external Identity Provider (Zitadel) via standard OpenID Connect:
- Uses the **authorization code flow with PKCE** (`response_type=code`, `code_challenge_method=S256`).
- Acts as a confidential client holding a client secret.
- Relies purely on standard OIDC discovery (`/.well-known/openid-configuration`), authorization endpoint, token endpoint, userinfo endpoint, and end-session endpoint.

### Rejected Alternative

Having `Keystone.IdentityService` proxy the login flow and act as the OIDC client. Rejected because a standards-compliant OIDC intermediary is complex and error-prone to maintain. Relying on ASP.NET Core's battle-tested `OpenIdConnectHandler` in the BFF ensures full standards compliance and allows swapping Zitadel for any standard OIDC provider (e.g. Entra ID, Auth0, Keycloak) without code modifications.

## Consequences

- The BFF handles standard OIDC redirects, authorization code exchange, state/nonce validation, and PKCE verification.
- Swapping the identity provider requires only configuration adjustments in the BFF and claim mapping in the Identity Service.
- The Identity Service does not need to implement or proxy the authorization code flow.
