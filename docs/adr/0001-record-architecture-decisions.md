# 0001. Record architecture decisions

- **Status:** Accepted
- **Date:** 2026-10-17

## Context

Keystone is a realistic reference platform showcasing how a Backend for Frontend (BFF), OpenID Connect (OIDC), internal token exchange, and Azure hosting fit together. Architecture choices made early (such as cookie isolation, token exchange, and session storage) are fundamental to the security and operational posture of the system. Without documentation, the rationale behind why specific options were chosen and why alternatives were rejected is easily forgotten.

## Decision

Record every significant architectural decision as an Architecture Decision Record (ADR) in `docs/adr/`, numbered sequentially. Each ADR captures:
- **Context:** The problem, forces, and requirements driving the decision.
- **Decision:** The chosen approach and why it was selected over rejected alternatives.
- **Consequences:** The positive and negative implications, operational trade-offs, and downstream impacts.

## Consequences

- Architectural decisions remain transparent, searchable, and auditable.
- Any future architectural modifications must reference or supersede existing ADRs.
- `ARCHITECTURE.md` and `README.md` link directly to ADRs for detailed decision rationale.
