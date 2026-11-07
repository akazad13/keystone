# AGENTS.md: AI Agent Guidance & Repository Rules

This document provides mandatory guidance for autonomous AI coding agents (Antigravity, Claude Code, GitHub Copilot, Cursor, Windsurf, Devin) operating in `akazad13/keystone`.

> **Read [CONSTRAINTS.md](CONSTRAINTS.md) before writing code. Do not weaken it to make a change pass.**

---

## 1. System Identity & Architecture

**Product**: `Keystone` — Enterprise .NET 10 + Azure web platform with end-to-end OpenID Connect (code flow + PKCE, refresh rotation, RP-initiated logout, back-channel logout, RFC 9470 step-up authentication).
- **Backend**: .NET 10, C# 13, ASP.NET Core, YARP (Reverse Proxy), Zitadel (OIDC Provider), Entity Framework Core, Redis session cache, SQL Server.
- **Frontend**: Two Vite React SPAs:
  - `keystone` (served at `/`)
  - `keystone-admin` (served at `/admin/`)
- **Orchestration**: .NET Aspire (`Keystone.AppHost`).

For deep architectural rationale, flows, and decision records (D1–D6), see [ARCHITECTURE.md](ARCHITECTURE.md) and [CLAUDE.md](CLAUDE.md).

---

## 2. Inviolable Architectural Invariants

1. **Banned Packages Guard (Zero-Tolerance)**:
   - **`Moq` and `AutoMapper` are BANNED.** Use hand-written fakes/stubs and explicit mapping code.
2. **Central Package Management**:
   - Every NuGet version is pinned exactly in `Directory.Packages.props`. `.csproj` files have `PackageReference` without versions. Never use floating versions (`9.*`, `10.*`).
3. **BFF Single Entry Point**:
   - The BFF (`Keystone.Bff`) is the ONLY public entry point for both SPAs. Tokens live in server-side Redis sessions (cookie `__Host-keystone` holds only an opaque reference).
   - YARP exchanges Zitadel access tokens at `Keystone.IdentityService` for short-lived Keystone JWTs before proxying requests to `Keystone.CoreApi`.
4. **Test Discipline**:
   - Every new feature or bug fix must include tests in the same commit.
   - Tests cover services via `TestServer` (`tests/`), React SPAs via Vitest + React Testing Library (`keystone/tests`, `keystone-admin/tests`), and E2E via Playwright (`e2e/`).

---

## 3. Commands Cheat Sheet

```bash
# Build all .NET projects
dotnet build Keystone.sln

# Run all .NET unit & integration tests (222 tests)
dotnet test Keystone.sln

# Run frontend tests
cd keystone && npm test
cd ../keystone-admin && npm test

# Run Aspire orchestrator locally
dotnet run --project Keystone.AppHost/Keystone.AppHost.csproj

# Run Playwright E2E tests
dotnet test e2e/
```

---

## 4. Testing Paradigm & Agentic Verification (ADR-0011)

Keystone enforces a strict Test-Driven Development (TDD) paradigm optimized for autonomous agent velocity and zero mock fragility (see [ADR-0011](docs/adr/0011-testing-paradigm-and-agentic-verification.md) and [docs/testing.md](docs/testing.md)):

1. **Test Pyramid**:
   - **Tier 1 (Unit & Cryptographic)**: In-memory, sub-second execution. Tests offline token validators (`LogoutTokenValidatorTests`), DPAPI session cryptors (`RedisTicketStoreTests`), request validators, and frontend React components via Vitest + RTL.
   - **Tier 2 (Integration & Proxy)**: ASP.NET Core `TestServer` hosting the real YARP forwarder pipeline (`ProxyTestHost` with `bff.appsettings.json`), stateful in-memory Redis (`FakeRedis`), containerized dependencies (Testcontainers Redis & Zitadel), and SQLite in-memory with foreign keys enabled.
   - **Tier 3 (End-to-End)**: Playwright tests (`e2e/`) driving real browser sessions against the orchestrated Aspire AppHost and Zitadel container.

2. **Core Testing Invariants**:
   - **State Over Interaction**: `Moq`, `NSubstitute`, and mock frameworks are strictly banned. Assert observable state (HTTP responses, database rows, encrypted Redis payload state) rather than method call sequences (`Verify()`).
   - **DAMP Over DRY**: Test methods should read like specifications. Prefer Descriptive And Meaningful Phrases (DAMP) over complex shared helper abstractions.
   - **Isolated Keyspaces**: All Redis test keys and test contexts must use isolated prefixes/keyspaces (`keystone:bff:...`) to allow parallel execution without cross-test interference.

3. **Fast Agentic Feedback Loop**:
   - **Inner Loop (Targeted Test Execution)**: When developing or fixing bugs, run focused tests in milliseconds:
     ```bash
     # Run a single test class or method
     dotnet test tests/Keystone.Bff.Tests --filter "FullyQualifiedName~RedisTicketStore"
     npm test --prefix keystone -- -t "AuthStatus"
     ```
   - **Outer Loop (Verification Gate)**: Before completing your turn, execute the repository verification gate:
     ```powershell
     pwsh ./scripts/verify.ps1
     ```

---

## 5. Verification Gates for AI Agents

Before submitting code changes or finishing a turn:
1. Run `pwsh ./scripts/verify.ps1` to verify all quality floor constraints, Release builds, and all 295 unit & integration tests.
2. Confirm `dotnet test Keystone.sln` passes all 222 backend tests.
3. If modifying frontend apps, confirm `npm test` passes all tests.
4. Keep `CLAUDE.md`, `ARCHITECTURE.md`, `docs/adr/`, and `docs/testing.md` updated if conventions change.
