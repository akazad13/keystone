# Testing Paradigm and Architecture

Keystone enforces a strict, multi-tiered testing paradigm designed for high execution speed, behavioral fidelity, and zero tolerance for fragile mocks. Every service, component, and interaction is tested through behaviorally accurate, in-memory fakes or real integration environments.

---

## 1. Core Principles and Testing Philosophy

### The Zero-Mock Policy
Mocking libraries such as `Moq`, `NSubstitute`, or `AutoMapper` are **banned** across the entire repository. Mock frameworks frequently introduce test fragility by coupling assertions to internal method calls rather than observable system behaviors. Furthermore, third-party mocking libraries have historically introduced supply-chain and telemetry risks.

In place of mocks, Keystone mandates:
- **Hand-written fakes and stubs:** Behaviorally faithful, stateful implementations (such as `FakeRedis`, `TestDatabase`, and `TestIdentityProvider`) that implement the contracts used by production code.
- **Dynamic dispatch proxies:** Using .NET's built-in `System.Reflection.DispatchProxy` to implement complex external interfaces (like StackExchange.Redis `IDatabase` and `IConnectionMultiplexer`) while strictly handling only the operations Keystone depends on.
- **Explicit object mapping:** Explicit, handwritten mapping methods without reflection-heavy automappers.

### In-Memory Isolation and Speed
- Unit and integration tests (`tests/`) execute entirely in memory via ASP.NET Core `TestServer`.
- No Docker containers, Zitadel instances, or network sockets are required to run the 222 backend tests.
- The entire backend test suite completes in approximately 3 to 4 seconds.
- Foreign keys, unique constraints, and schema integrity are enforced in SQLite in-memory databases rather than EF Core's InMemory provider.

### Three Distinct Testing Tiers
Keystone divides automated verification into three decoupled layers:

| Tier | Scope | Frameworks | Execution Environment |
| :--- | :--- | :--- | :--- |
| **Tier 1: Backend Services** | BFF, Identity Service, Core API, YARP proxy, JWT validation | xUnit v3, Microsoft.Testing.Platform (MTP), ASP.NET Core `TestServer` | In-memory (.NET 10 process) |
| **Tier 2: Frontend SPAs** | React components, routing, query cache, authentication hooks | Vitest, React Testing Library, jsdom | In-memory (Node.js / jsdom) |
| **Tier 3: End-to-End** | Full user journeys, OIDC code flow + PKCE, back-channel logout, step-up auth | Playwright, Microsoft Edge / Chromium | Real Aspire AppHost + Zitadel in Docker |

---

## 2. Tier 1: .NET Services Testing

### Microsoft.Testing.Platform (MTP) and .NET 10 SDK
With .NET 10 SDK and xUnit v3, Keystone runs tests natively through Microsoft.Testing.Platform (MTP) rather than the legacy VSTest runner. The solution root `global.json` opts into the native platform runner:

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

This configuration ensures `dotnet test` executes test binaries as self-contained executables (`<OutputType>Exe</OutputType>`), providing faster execution and out-of-the-box parallelization.

### In-Memory Test Hosts
Each backend service has a dedicated test host built on `Microsoft.AspNetCore.TestHost.TestServer`:

```
tests/
├── Keystone.Bff.Tests/
│   ├── Fakes/
│   │   ├── BffTestHost.cs          # Real auth & session endpoints on TestServer
│   │   ├── ProxyTestHost.cs        # Real YARP proxy with real route table
│   │   ├── FakeRedis.cs            # In-memory Redis via DispatchProxy
│   │   ├── TestIdentityProvider.cs # Simulates OIDC IdP challenge redirects
│   │   └── TestSessions.cs         # Configures RedisTicketStore over FakeRedis
│   └── ...
├── Keystone.CoreApi.Tests/
│   ├── Fakes/
│   │   ├── CoreApiTestHost.cs      # Core API endpoints, middleware, SQLite
│   │   ├── JwtTestHost.cs          # Validates real offline ECDsa JWT tokens
│   │   └── TestDatabase.cs         # SQLite in-memory with foreign keys enabled
│   └── ...
└── Keystone.IdentityService.Tests/
    ├── Fakes/
    │   ├── IdentityServiceTestHost.cs # KeyManager, TokenService on TestServer
    │   └── TestSetup.cs               # ECDsa signing key configuration
    └── ...
```

#### 1. In-Memory Redis with `DispatchProxy` (`FakeRedis`)
StackExchange.Redis `IDatabase` contains hundreds of methods. Instead of using mock frameworks, `FakeRedis` uses `DispatchProxy` to intercept calls dynamically:
- Implements strings (`StringSetAsync`, `StringGetAsync`, `KeyDeleteAsync`).
- Implements Redis sets (`SetAddAsync`, `SetMembersAsync`, `SetRemoveAsync`) used for tracking active user sessions.
- Implements distributed locks (`When.NotExists`) with atomic key check and assignment.
- Records TTL expirations and allows test code to simulate cache evictions deterministically.

#### 2. Real YARP Route Table Verification (`ProxyTestHost`)
Instead of mocking the reverse proxy, `ProxyTestHost`:
- Loads the actual `bff.appsettings.json` route configuration from `Keystone.Bff`.
- Configures real YARP forwarders, transforms, and rate limiter policies.
- Intercepts outbound forwarder requests using an in-memory `RecordingCoreApi` HTTP handler, verifying that downstream headers, tokens, and transformed paths match specifications.

#### 3. Real SQLite Relational Semantics (`TestDatabase`)
EF Core's `InMemory` database provider does not enforce unique constraints, cascades, or foreign keys. Keystone uses SQLite in-memory (`DataSource=:memory:`):
- Keeps a single open connection alive for the lifetime of the test.
- Enforces relational constraints (foreign keys, unique project titles per owner).
- Ensures schema migrations and LINQ queries execute with realistic SQL semantics.

---

## 3. Tier 2: Frontend SPAs Testing

Both the customer portal (`keystone`) and admin portal (`keystone-admin`) use Vitest and React Testing Library without Mock Service Worker (MSW) or external mocking frameworks.

### Fake BFF (`tests/fakeBff.ts`)
Instead of running a mock HTTP server or intercepting network sockets at the worker level, `FakeBff` stubs global `fetch`:
- Configures exact route responders (`on(method, path, reply)`).
- Provides convenience helpers for user states (`signedIn(user)` and `signedOut()`).
- Unhandled requests immediately return HTTP 404, preventing silent test hangs.
- Captures all incoming requests (method, path, headers, CSRF headers, and payload) for explicit assertions.

### Router and Query Harness (`tests/render.tsx`)
Because TanStack Router and TanStack React Query render asynchronously and require context:
- `renderWithApp(Component)` wraps components in a fresh `QueryClientProvider` and an in-memory TanStack Router (`createMemoryHistory`).
- Query client retries are disabled (`retry: false`) so failures surface instantly without artificial timeouts.
- Route components are mounted directly using `Route.options.component`.

### Abstracted Navigation
To verify external redirects (such as redirecting to Zitadel for login or step-up authentication) without throwing errors in jsdom:
- Applications navigate using `browser.navigate(url)` from `src/lib/navigation.ts`.
- Tests spy on `browser.navigate` to assert redirect targets without triggering jsdom navigation limitations.

---

## 4. Tier 3: End-to-End Testing

End-to-end tests live in `e2e/` and use Playwright to drive real browser sessions against the complete orchestrated platform:

```
e2e/tests/
├── login.spec.ts     # OIDC code flow + PKCE, session cookie, CSRF rejection
├── logout.spec.ts    # RP-initiated logout & IdP back-channel logout
├── projects.spec.ts  # Multi-user data isolation and CRUD validation
├── admin.spec.ts     # RBAC verification (alice as admin vs bob as standard user)
├── sessions.spec.ts  # Cross-session listing and remote session revocation
└── step-up.spec.ts   # RFC 9470 step-up authentication (prompt=login)
```

### Key E2E Features
1. **Real Infrastructure:** Zitadel runs in Docker, and Keystone services run under the Aspire AppHost (`Keystone.AppHost`).
2. **Back-Channel Logout Verification:** In CI (Linux runners), `socat` forwards traffic from `172.17.0.1:5003` to `127.0.0.1:5003` so Zitadel webhooks reach the local BFF instance.
3. **Dynamic Credentials:** Test user passwords and client secrets are read dynamically from `zitadel/setup.ps1` and AppHost user secrets, preventing credentials from being hardcoded.
4. **Serial Execution:** Because tests authenticate shared IdP test accounts (`alice` and `bob`), E2E specs run serially to prevent session collision.

---

## 5. Development Workflow & TDD Loop

Keystone follows Test-Driven Development (TDD) for all bug fixes and new features:

```
    [1. RED]               [2. GREEN]             [3. REFACTOR]
 Write failing test  ──> Implement minimal  ──> Clean up code &
 (or bug repro test)      code to pass test      verify invariants
```

### Verification Checklist Before Commit
1. **Run .NET Unit & Integration Tests:**
   ```powershell
   dotnet test Keystone.sln
   ```
   All 222 backend tests must pass.
2. **Run Frontend Tests:**
   ```powershell
   npm test --prefix keystone
   npm test --prefix keystone-admin
   ```
   All 73 frontend tests (53 in keystone, 20 in keystone-admin) must pass.
3. **Check Architectural Invariants:**
   - No references to `Moq`, `NSubstitute`, or `AutoMapper`.
   - All NuGet package versions pinned in `Directory.Packages.props`.
   - All mutating requests require `X-CSRF: 1`.
   - Mutating API endpoints enforce owner scoping from the validated token.
