# Contributing

## Prerequisites

- .NET 8 SDK
- Docker (for the integration tests and the sample's Postgres container; both use
  real PostgreSQL rather than a fake/in-memory provider)
- `dotnet-ef` for generating migrations: `dotnet tool install --global dotnet-ef`

## Getting started

```bash
git clone <repo-url>
cd EfCore.MultiTenancy
dotnet build EfCore.MultiTenancy.slnx
```

## Running the tests

```bash
dotnet test tests/EfCore.MultiTenancy.UnitTests          # fast, no database
dotnet test tests/EfCore.MultiTenancy.IntegrationTests    # spins up real Postgres via Testcontainers
```

Both must pass before opening a PR. If your change touches anything on the
isolation-critical path (the connection interceptor, `AddTenantDbContext`'s
options-building lambda, the tenant scope factory, provisioning, or migration),
**run the integration tests**, not just the unit tests. Three genuine isolation
bugs were found during this project's own development, and all three passed a
clean build and would have passed a unit test built on fakes; only running
against a real database caught them. See
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md#three-real-isolationcorrectness-bugs-found-while-building-this)
for what they were, so you don't reintroduce one of the same shapes.

## Running the sample locally

```bash
cd samples/SampleApi
docker compose up -d
dotnet ef database update --context 'TenantStoreDbContext`1'
dotnet run
```

Then create a tenant and hit its endpoint, as shown in the main
[README](README.md#running-the-sample). Useful as a manual sanity check after any
change to resolution, provisioning, or the interceptor: create two tenants, write
data to one, and confirm the other still can't see it.

## Building the packages locally

The packages aren't published to NuGet yet. Until they are, if you need a `.nupkg`
to test against a separate consuming project (rather than a `ProjectReference`),
pack them locally:

```bash
dotnet pack src/EfCore.MultiTenancy.Core -c Release
dotnet pack src/EfCore.MultiTenancy.EfCore -c Release
dotnet pack src/EfCore.MultiTenancy.AspNetCore -c Release
```

Each `.nupkg` lands in that project's `bin/Release/`. Add a local folder as a NuGet
source to consume them without a real feed:

```bash
dotnet nuget add source /path/to/local/nupkg-folder --name local-efcore-multitenancy
```

Package metadata (authors, tags, license) lives in [`Directory.Build.props`](Directory.Build.props)
at the repo root, shared by all three library projects.

## Before opening a PR

- `dotnet build EfCore.MultiTenancy.slnx` is clean (no warnings).
- Both test projects pass.
- If you touched the isolation-critical path, you ran the sample end-to-end with
  two tenants and manually confirmed no cross-tenant leakage, not just that the
  code compiles.
- New behavior in `Core`/`EfCore`/`AspNetCore` has a corresponding unit test (fast
  logic) or integration test (anything that depends on real Postgres behavior,
  e.g. `search_path`, migrations history, schema creation).
- Doc comments explain *why*, not *what*; see the existing code for the tone
  (e.g. `TenantSchemaConnectionInterceptor`, `TenantContext<TTenant>`). Don't add a
  comment that just restates the method name.
- If you change a public API surface used in the README's quick-start example,
  update the README to match.

## Code style

- No static or thread-local mutable state for "current tenant," anywhere. This is
  the one invariant the whole library exists to protect; a PR that reintroduces it
  even for convenience will be rejected.
- Prefer explicit DI wiring (`AddScoped`/`AddSingleton` with a clear reason for the
  chosen lifetime) over relying on framework auto-discovery behavior you haven't
  independently verified. One of the three isolation bugs above came from assuming
  EF Core auto-discovers DI-registered interceptors; it doesn't.
- Match the existing project structure: `Core` stays free of EF Core and ASP.NET
  Core dependencies.
