# 0011. Zero-Mock Testing Paradigm and Microsoft.Testing.Platform

- **Status:** Accepted
- **Date:** 2026-11-07
- **Architecture Ref:** Quality Engineering, In-Memory Testing, Test Runner Modernization

## Context

Enterprise architectures with distributed microservices, reverse proxies, and identity boundaries often suffer from slow, brittle, or incomplete test suites. Two common anti-patterns emerge:
1. **Over-mocking:** Relying heavily on dynamic mocking frameworks (such as Moq), which test mock configuration rather than real system behaviors and break whenever internal implementation details change.
2. **Heavy test dependencies:** Requiring live external dependencies (Docker, identity provider servers, Redis clusters, and SQL Server instances) to run basic unit or integration tests, slowing down local development and CI loops.

Additionally, with the upgrade to .NET 10 SDK, Microsoft introduced native Microsoft.Testing.Platform (MTP) execution for test projects, deprecating legacy VSTest targets for modern testing platform components.

## Decision

1. **Strict Zero-Mock Policy**:
   - Ban `Moq`, `NSubstitute`, `AutoMapper`, and mock server libraries across the entire repository.
   - Use hand-written, stateful in-memory fakes (`FakeRedis`, `TestDatabase`, `TestIdentityProvider`, `StubHttpHandler`) that implement actual runtime contracts.
   - Utilize .NET `DispatchProxy` for complex third-party interfaces (StackExchange.Redis `IDatabase`) to handle only the operations Keystone uses while failing fast on unsupported operations.
   - Use SQLite in-memory with foreign keys enabled (`DataSource=:memory:`) instead of EF Core's InMemory provider to enforce relational constraints.

2. **Full Pipeline Realism on `TestServer`**:
   - Host all backend tests on ASP.NET Core `TestServer`.
   - Run the BFF reverse proxy tests against real YARP forwarders, transforms, rate limiting policies, and the production `bff.appsettings.json` route table.
   - Run Core API JWT authorization tests against real offline ECDsa cryptographic key validation.

3. **Frontend Zero-Mock Isolation**:
   - Use Vitest and React Testing Library without Mock Service Worker (MSW).
   - Use a lightweight, in-memory `FakeBff` that stubs `fetch`, records requests, and returns explicit responses.
   - Mount components in real TanStack Router (`createMemoryHistory`) and React Query (`QueryClientProvider`) harnesses.

4. **Native Microsoft.Testing.Platform Runner Opt-In**:
   - Opt into Microsoft.Testing.Platform via solution root `global.json`:
     ```json
     {
       "test": {
         "runner": "Microsoft.Testing.Platform"
       }
     }
     ```
   - Build test assemblies as self-contained executables running on xUnit v3 and MTP for fast, parallel execution.

5. **Decoupled End-to-End Tier**:
   - Reserve live infrastructure tests (Zitadel, Redis, SQL Server, and Aspire AppHost) strictly for Playwright in `e2e/`, running against the fully orchestrated environment.

## Consequences

- **Speed:** All 222 backend tests execute entirely in memory in under 4 seconds without external containers.
- **Reliability:** Tests verify actual middleware order, HTTP status codes, routing rules, token transforms, and database constraints without mock divergence.
- **Maintainability:** Refactoring internal service implementations does not break tests as long as external contracts and behaviors remain intact.
- **CI/CD Compatibility:** Tests run seamlessly on .NET 10 SDK without VSTest runner deprecation errors.
