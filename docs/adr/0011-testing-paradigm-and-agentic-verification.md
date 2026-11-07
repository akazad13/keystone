# 0011. Testing Paradigm and Agentic Verification

- **Status:** Accepted
- **Date:** 2026-11-07
- **Architecture Ref:** Quality Engineering, TDD, Test Pyramid, Agentic Verification

## Context

Enterprise web architectures with strict security perimeters—such as Backend for Frontend (BFF) cookie isolation, RFC 8693 token exchange, RFC 9470 step-up authentication, and back-channel logout—demand high verification rigor. However, testing distributed identity and proxy pipelines often falls victim to two major anti-patterns:

1. **Over-Mocking Fragility:** Relying heavily on dynamic mocking frameworks (e.g., `Moq`, `NSubstitute`) couples tests to internal method call sequences rather than observable state and behavior. When refactoring internal abstractions, mock setups break even when external contracts remain intact.
2. **Monolithic Test Loops:** Requiring full orchestration stacks (Docker, live Identity Providers, real Redis clusters, and SQL Server instances) for every minor code edit drastically slows down verification loops, creating friction and flakiness for human developers and autonomous AI agents.

To enable autonomous coding agents and engineers to practice rigorous Test-Driven Development (TDD) with high velocity and deterministic verification, Keystone requires an explicitly codified test pyramid, testing invariants, and a dual-speed feedback loop.

---

## Decision

We codify the **Keystone Testing Paradigm for Agentic Development**, structured around three foundational pillars:

### 1. The Keystone Test Pyramid

The repository organizes verification across three decoupled tiers according to resource consumption, execution speed, and behavioral fidelity:

```
          ╱  ╲         Tier 3: E2E Tests (~5%)
         ╱    ╲        Playwright + Live Aspire Stack + Zitadel OIDC
        ╱──────╲
       ╱        ╲      Tier 2: Integration Tests (~15%)
      ╱          ╲     TestServer + Real YARP Proxy + Redis & SQLite Fakes/Containers
     ╱────────────╲
    ╱              ╲   Tier 1: Unit & Component Tests (~80%)
   ╱                ╲  Pure logic, token validators, session cryptors, React RTL
  ╱──────────────────╲
```

#### Tier 1: Unit & In-Memory Component Tests (Fast, Deterministic)
- **Execution Target:** Sub-second execution in-process without network I/O or external daemon dependencies.
- **In-Memory Token Validators:** `LogoutTokenValidatorTests` exercises OpenID Connect Back-Channel Logout 1.0 (section 2.6) using real cryptographic RSA/ECDsa keys, validating signatures, issuer, audience, and token expiration without external IdP round-trips.
- **Session Cryptors:** `RedisTicketStoreTests` and `TestSessions` verify DPAPI / data protection envelope encryption of access and refresh tokens, verifying that raw token strings never touch storage in plaintext.
- **Frontend Components:** Vitest and React Testing Library in `keystone/` and `keystone-admin/` test components and routing hooks using `FakeBff` and memory history.

#### Tier 2: Integration & BFF Proxy Tests (Behavioral Realism)
- **Execution Target:** 2 to 4 seconds for full backend suite hosted on ASP.NET Core `TestServer`.
- **YARP BFF Reverse Proxy:** `ProxyTestHost` hosts the real YARP forwarding pipeline with the production `bff.appsettings.json` routing configuration, verifying prefix rewriting, header stripping, token injection transforms, rate limiters, and anti-forgery protection against an in-memory recording handler.
- **Isolated State & Testcontainers:** Real StackExchange.Redis semantics simulated via stateful `FakeRedis` (using .NET `DispatchProxy` over `IDatabase`) and containerized dependencies (Testcontainers Redis & Zitadel) when live protocol verification is required.
- **Relational Integrity:** SQLite in-memory with foreign keys explicitly enabled (`DataSource=:memory:`) ensures schema constraints and cascades are enforced, replacing unrealistic EF Core `InMemory` providers.

#### Tier 3: End-to-End Tests (Complete User Journeys)
- **Execution Target:** Headless browser runs against live Aspire AppHost (`Keystone.AppHost`) and local Zitadel container.
- **Verification Scope:** Full OIDC Authorization Code Flow with PKCE, `__Host-keystone` session cookie propagation, cross-session listing, RP-initiated logout, server-to-server back-channel logout webhooks, and RFC 9470 step-up authentication prompts (`prompt=login`).

---

### 2. Architectural Invariants for Testing

All test authoring across Keystone must enforce three invariants:

1. **State Over Interaction Testing (Zero-Mock Invariant):**
   - Dynamic mocking frameworks (`Moq`, `NSubstitute`, `AutoMapper`) are strictly banned across the solution.
   - Assert on the **observable state** resulting from operations (HTTP status codes, response payloads, persisted Redis keys and TTLs, SQLite rows) rather than internal method invocation counts (`Verify()`).
   - Use hand-written, stateful fakes (`FakeRedis`, `TestDatabase`, `TestIdentityProvider`, `StubHttpHandler`) that implement actual runtime interfaces.

2. **DAMP Over DRY in Test Readability:**
   - Tests serve as living, executable documentation. Favor **DAMP (Descriptive And Meaningful Phrases)** over aggressive DRY (Don't Repeat Yourself).
   - Each test method must clearly declare its Arrange, Act, and Assert steps so failures are diagnosable at a glance without traversing complex inheritance trees.

3. **Isolated Redis Keyspaces for Parallel Execution:**
   - Both in-memory fakes and live container test harnesses must enforce isolated keyspaces or key prefixes (`keystone:bff:session:...`, `keystone:bff:logout-jti:...`).
   - Prevents cross-test state leakage and allows xUnit v3 to run test collections concurrently across CPU cores.

---

### 3. Fast Agentic Feedback Loop & TDD Cycle

Autonomous coding agents (and human engineers) must follow a disciplined Test-Driven Development (TDD) cadence:

```
    [1. RED]               [2. GREEN]             [3. REFACTOR]
 Write failing test  ──> Implement minimal  ──> Clean up code &
 (or repro test)         code to pass test      verify invariants
```

#### Dual-Speed Agentic Feedback Loop:
1. **Inner Loop (Focused Testing - Milliseconds):**
   - When modifying a service or resolving an issue, run only the targeted test class or method:
     ```powershell
     # Focused .NET test run
     dotnet test tests/Keystone.Bff.Tests --filter "FullyQualifiedName~RedisTicketStore"
     dotnet test tests/Keystone.IdentityService.Tests --filter "FullyQualifiedName~TokenService"
     
     # Focused frontend test run
     npm test --prefix keystone -- -t "AuthStatus"
     ```
   - Delivers sub-second feedback for the immediate Red-Green cycle.

2. **Outer Loop (Verification Gate - Full Suite):**
   - Before completing an agentic task or submitting commits, run the full verification gate script:
     ```powershell
     pwsh ./scripts/verify.ps1
     ```
   - This executes:
     - Quality floor and forbidden mock scans.
     - Solution-wide Release build (`dotnet build -c Release Keystone.sln`).
     - All 222 backend unit and integration tests (`dotnet test Keystone.sln`).
     - All 73 frontend tests across `keystone` and `keystone-admin`.

---

## Consequences

### Positive
- **Execution Speed:** All 222 backend tests execute in memory in under 4 seconds without external containers.
- **Deterministic Reliability:** In-memory cryptography and real YARP transforms ensure tests execute identically in local dev and CI runners without network flakes.
- **Refactoring Resilience:** Internal implementation details can be refactored safely without breaking mock expectations as long as observable behavior is preserved.
- **Agentic Autonomy:** High-speed focused test commands allow AI agents to independently prove fixes and verify edge cases without human intervention.

### Neutral / Trade-offs
- Maintaining hand-written fakes (`FakeRedis`, `TestDatabase`, `TestIdentityProvider`) requires engineering investment when underlying external library interfaces change.
- Live E2E tests with Zitadel and Aspire require Docker and run serially to avoid IdP user state collisions.
