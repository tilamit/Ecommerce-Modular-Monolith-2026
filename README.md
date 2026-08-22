# ShopHub

A .NET 10 **modular monolith** e-commerce reference implementation: one process, one
deployable, one database - with four independently-evolvable modules and *enforced*
boundaries.

The point of this repo is the boundaries. A monolith with folders named "Modules" that all
reference each other is a layered monolith with extra steps; the architecture tests in
`tests/ShopHub.ArchitectureTests` are what make the difference non-negotiable.

**Current state: Phases 0-1 complete.** Skeleton, guardrails, host, and all cross-cutting
concerns are in place and verified. Modules are registered but empty - features land in
Phases 2-6. See [`docs/PROGRESS.md`](docs/PROGRESS.md).

---

## Prerequisites

| Tool | Version used | Check |
|---|---|---|
| .NET SDK | 10.0.301 | `dotnet --list-sdks` |
| SQL Server LocalDB | `MSSQLLocalDB` | `sqllocaldb info` |
| Node.js / npm | 26.4.0 / 11.17.0 | `node -v` - needed from Phase 7 |
| git | 2.38.1 | `git --version` |

The SDK version is pinned in `global.json` with `rollForward: latestFeature`, so a newer
10.0.x patch works without editing anything.

---

## Getting started

```bash
git clone <repo> && cd shophub
dotnet restore
dotnet build          # clean under TreatWarningsAsErrors
dotnet test           # 96 tests
```

### Database

The connection string lives in `src/Api/ShopHub.Api/appsettings.Development.json` and
points at LocalDB. It contains no credentials - it uses `Trusted_Connection=True`.

Create the database once:

```bash
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C -Q "IF DB_ID('ShopHub') IS NULL CREATE DATABASE ShopHub;"
```

From Phase 2 each module owns its own migrations and its own history table, so they are
applied per module:

```bash
# not yet applicable - no migrations exist before Phase 2
dotnet ef database update --project src/Modules/Identity/ShopHub.Modules.Identity --startup-project src/Api/ShopHub.Api
dotnet ef database update --project src/Modules/Catalog/ShopHub.Modules.Catalog   --startup-project src/Api/ShopHub.Api
dotnet ef database update --project src/Modules/Ordering/ShopHub.Modules.Ordering --startup-project src/Api/ShopHub.Api
dotnet ef database update --project src/Modules/Auditing/ShopHub.Modules.Auditing --startup-project src/Api/ShopHub.Api
```

### Secrets

Never commit credentials. Secrets go in user-secrets (the API project already has a
`UserSecretsId`):

```bash
dotnet user-secrets --project src/Api/ShopHub.Api set "Seed:AdminPassword" "<your-password>"
```

From Phase 2, startup **fails loudly** if a required seed password is missing rather than
defaulting to something guessable.

### Run the API

```bash
dotnet run --project src/Api/ShopHub.Api
```

| URL | What |
|---|---|
| `http://localhost:5069/scalar/v1` | API reference UI (Development only) |
| `http://localhost:5069/openapi/v1.json` | OpenAPI 3.1.1 document |
| `http://localhost:5069/health/live` | Liveness - runs no checks by design |
| `http://localhost:5069/health/ready` | Readiness - includes the SQL Server probe |

### Run the frontend

Not yet - Phase 7.

---

## Solution layout

```
ShopHub.slnx
├─ Directory.Build.props        # net10.0, C# 14, nullable, warnings-as-errors
├─ Directory.Packages.props     # Central Package Management - every version pinned here
├─ docs/
│  ├─ DECISIONS.md              # ADRs, incl. all spec §16 verification results
│  ├─ PROGRESS.md               # phase checklist + acceptance evidence
│  └─ MODULE_TEMPLATE.md        # how to add module #5
├─ src/
│  ├─ Api/ShopHub.Api/          # the ONLY executable
│  ├─ Shared/
│  │  ├─ ShopHub.Shared.Kernel/         # references nothing but the BCL
│  │  ├─ ShopHub.Shared.Contracts/      # integration events crossing modules
│  │  └─ ShopHub.Shared.Infrastructure/ # §6 cross-cutting concerns, IModule, IEventBus
│  └─ Modules/{Identity,Catalog,Ordering,Auditing}/
│     └─ ShopHub.Modules.X/ + ShopHub.Modules.X.Contracts/
└─ tests/{ArchitectureTests,UnitTests,IntegrationTests}/
```

---

## The boundary rules

Enforced by `tests/ShopHub.ArchitectureTests`, not by convention:

1. A module never references another module's **implementation** assembly.
2. Cross-module dependencies go through `*.Contracts` only.
3. `Shared.Kernel` references nothing but the BCL.
4. No `DbContext` is public.
5. Every entity maps to its own module's schema.
6. Endpoint classes are `internal`.
7. `*Controller` / `*Repository` names earn no exemption.

Each module exposes exactly **one** public type - its `IModule` implementation. Everything
else is `internal`, so the compiler enforces the boundary before a test has to.

These have been *proven* to fail on a real violation, not merely written. See the Phase 0
acceptance section of [`docs/PROGRESS.md`](docs/PROGRESS.md) - the exercise found a genuine
bug in the tests themselves (ADR-011).

### The migration test

At any point, one module should be extractable into its own service by swapping its
Contracts implementation for an HTTP client, moving its schema to its own database, and
replacing in-process events with a broker. If extraction would require untangling a query
or a transaction, a boundary has been violated.

---

## Request lifecycle

```
                    ┌─────────────────────────────────────────────┐
   HTTP request ───▶│ UseExceptionHandler   → RFC 9457 ProblemDetails
                    │ ForwardedHeaders      → only if configured
                    │ UseCorrelationId      → X-Correlation-Id in/out, LogContext
                    │ SerilogRequestLogging → one line: user, page, elapsed
                    │ ResponseCompression   → Brotli / Gzip
                    │ Cors                  → explicit origins + AllowCredentials
                    │ RateLimiter           → auth | anonymous | authenticated | write | checkout
                    │ OutputCache           → anonymous storefront GETs
                    └──────────────────┬──────────────────────────┘
                                       ▼
                          module endpoint (internal)
                                       ▼
                     handler ──▶ DbContext (own schema only)
                                       │
                       cross-module?    ├─ needs an answer now → other module's Contracts
                                       └─ fire-and-forget      → IEventBus, after commit
```

Error responses always carry `traceId` and a stable machine-readable `code`. Outside
Development, 5xx bodies are sanitized - the detail goes to the log, never to the client.

---

## Conventions worth knowing before contributing

- **Every list endpoint is paged.** No endpoint returns an unbounded collection, including
  admin screens and dropdowns. `PageSize` clamps to `[1, 100]`.
- **Reads project, they do not materialize.** `AsNoTracking()` + `.Select(...)` into a DTO.
- **Sorting is whitelisted per endpoint.** A client string never reaches `OrderBy`.
- **Every sort needs a tiebreaker**, or paging silently skips and duplicates rows.
- **`CancellationToken` everywhere**, endpoint to database. No `.Result`, no `.Wait()`.
- **UTC everywhere**, column names end in `Utc`. The browser converts for display.
- **Money is `decimal(18,2)` + an explicit `CurrencyCode`.** No floats.
- Cache **DTOs**, never EF entities. Never per-user mutable data.

---

## Documentation

| File | Contents |
|---|---|
| [`ECOMMERCE_MODULAR_MONOLITH_SPEC.md`](ECOMMERCE_MODULAR_MONOLITH_SPEC.md) | The full build specification |
| [`SPEC_FEASIBILITY_ASSESSMENT.md`](SPEC_FEASIBILITY_ASSESSMENT.md) | Delivery plan and why it is phased |
| [`docs/DECISIONS.md`](docs/DECISIONS.md) | ADRs - every decision, deviation, and §16 verification |
| [`docs/PROGRESS.md`](docs/PROGRESS.md) | Phase checklist with acceptance evidence |
| [`docs/MODULE_TEMPLATE.md`](docs/MODULE_TEMPLATE.md) | Adding module #5 |

`docs/MICROSERVICES_NOTES.md` - where the seams are and what extracting each module would
actually cost - arrives in Phase 11.
