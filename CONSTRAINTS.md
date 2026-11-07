# Quality Constraints & Standards

This document defines the mechanical quality bar and inviolable constraints for Keystone. All autonomous AI agents and human contributors must satisfy these constraints before changes can be merged.

Do not weaken this file or lower thresholds to make a change pass.

---

## 1. The Quality Floor (Always Enforced)

The following rules apply to every commit with zero exceptions:

- **Banned Packages Guard:** `Moq`, `AutoMapper`, and `NSubstitute` are strictly banned. Use hand-written fakes (`FakeRedis`, `TestDatabase`, `TestIdentityProvider`, `FakeBff`) and explicit object mappings.
- **Central Package Management:** Every NuGet dependency version must be pinned in `Directory.Packages.props`. Project files (`.csproj`) must not contain inline or floating versions.
- **No Suppression Comments:** No new `@ts-ignore`, `@ts-nocheck`, `eslint-disable`, or `#pragma warning disable` comments in code or tests.
- **No Unimplemented Stubs:** No `throw new NotImplementedException()`, `throw new Error("Not implemented")`, or empty `catch {}` blocks.
- **No Skipped or Deleted Tests:** No `[Fact(Skip = "...")]`, `it.skip()`, or `test.skip()` without an explicit, documented reason approved in an issue or PR.
- **No Secrets in Source:** Test credentials must never be hardcoded. They are dynamically read from `zitadel/setup.ps1` or user secrets.
- **Single Public Origin (BFF):** The browser holds zero tokens. All API calls and page requests route through the BFF origin.

---

## 2. Enforced Dimensions with Concrete Thresholds

| Dimension | Rule / Threshold | Checked By | Stage |
| :--- | :--- | :--- | :--- |
| **Compiler Warnings** | 0 warnings (warnings treated as errors) | `dotnet build Keystone.sln --configuration Release /warnaserror` | Every build, CI |
| **.NET Backend Tests** | >= 222 tests passing, 0 failed | `dotnet test Keystone.sln --no-build --configuration Release` | Every task, CI |
| **Backend Test Speed** | Full backend suite runs under 5 seconds | xUnit v3 + Microsoft.Testing.Platform in-memory | Local, CI |
| **Customer Portal Tests** | >= 53 tests passing, 0 failed | `npm test --prefix keystone` | Every task, CI |
| **Admin Portal Tests** | >= 20 tests passing, 0 failed | `npm test --prefix keystone-admin` | Every task, CI |
| **Frontend Production Bundles**| Zero TypeScript and Vite bundling errors | `npm run build --prefix keystone` & `keystone-admin` | Pre-commit, CI |
| **CSRF Defense** | All mutating HTTP requests enforce `X-CSRF: 1` | `CsrfProtectionTests.cs` (BFF) & `fakeBff.ts` | Unit/Integration |
| **Multi-Tenant Scoping** | All database queries scoped to token subject (`ICurrentUserService`) | `ProjectServiceTests.cs` & `CurrentUserServiceTests.cs` | Unit/Integration |
| **End-to-End Scenarios** | 21 critical flows passing in real browser | `npm test --prefix e2e` | Nightly, CI PRs |

---

## 3. Unified Agent Verification Command

Before handing over completed work or committing changes, an AI agent must execute:

```powershell
pwsh ./scripts/verify.ps1
```

This single command executes:
1. Static scan for banned packages (`Moq`, `AutoMapper`, `NSubstitute`)
2. Static scan for skipped tests and suppression comments
3. Release build with `/warnaserror`
4. All 222 .NET tests via native MTP
5. Type-checking and production bundle builds for both SPAs
6. All 73 frontend Vitest tests

---

## 4. Ratchets (Metrics Held at or Above Current Levels)

| Metric | Current Baseline | Direction |
| :--- | :--- | :--- |
| Automated Unit/Integration Tests | 295 tests (222 .NET + 73 React) | Must not decrease |
| End-to-End Test Scenarios | 21 specs | Must not decrease |
| Code Vulnerability Severity | 0 known vulnerabilities | Must remain 0 |
