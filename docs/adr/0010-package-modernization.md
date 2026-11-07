# 0010. Solution Package Modernization and Dependency Alignment

- **Status:** Accepted
- **Date:** 2026-11-07
- **Architecture Ref:** Dependency Modernization & Security Compliance

## Context

Keystone is an enterprise identity and access management architecture utilizing .NET 9, .NET Aspire, Yarp reverse proxy, Redis, and Zitadel OIDC. A repository-wide dependency review was conducted to ensure all package references align with the latest secure releases, patch known CVEs, and optimize runtime performance across all microservices and orchestration components.

## Decision

1. **Modernize All NuGet Dependencies**: Upgraded all .NET Aspire hosting integrations, Microsoft.Extensions, Yarp.ReverseProxy, StackExchange.Redis, OpenTelemetry exporters, and test libraries (xUnit, FluentAssertions, Testcontainers) to their latest stable releases.
2. **Standardize Central Package Management**: Ensure transitive dependency trees resolve cleanly without version pinning conflicts across projects.
3. **Preserve Architectural Invariants**: Verify that token exchange protocols (RFC 8693), Redis encrypted session stores, and BFF cookie security patterns remain unaltered and pass all automated test suites.

## Consequences

- Full compatibility with the latest .NET Aspire tooling and OpenTelemetry specifications.
- Clean vulnerability audit across all dependencies.
- No breaking shifts in public BFF endpoints or token mediation pipelines.
