# EfCore.MultiTenancy

Schema-based multi-tenancy for PostgreSQL + EF Core, in the spirit of Python's
[django-tenants](https://django-tenants.readthedocs.io/en/latest/): one PostgreSQL
database, one schema per tenant, a shared `public` schema for the tenant registry,
and automatic routing of each request to the right schema.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for how it works internally,
including three real isolation bugs found and fixed while building this. Worth
reading before you extend the isolation-critical path.

## Packages

| Project | Purpose |
|---|---|
| `EfCore.MultiTenancy.Core` | `Tenant`/`ITenant`, `ITenantContext<T>`, lifecycle events. No EF Core or ASP.NET Core dependency. |
| `EfCore.MultiTenancy.EfCore` | `TenantDbContext<T>`, the search_path connection interceptor, provisioning, migration. |
| `EfCore.MultiTenancy.AspNetCore` | Resolution middleware, resolution strategies, `AddMultiTenancy` DI wiring. |

## Install

Requires .NET 8 and PostgreSQL 12+.

Once published to NuGet, install the package(s) you need. Most apps only need
`AspNetCore` directly, since it pulls in `EfCore` and `Core` transitively:

```bash
dotnet add package EfCore.MultiTenancy.AspNetCore
```

Or, if you only need the EF Core layer without ASP.NET Core (a console tool or a
background worker):

```bash
dotnet add package EfCore.MultiTenancy.EfCore
```

Equivalent `PackageReference` entries in a `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="EfCore.MultiTenancy.AspNetCore" Version="1.0.0" />
</ItemGroup>
```

## Quick start

### 1. Define your tenant type (optional) and tenant-scoped `DbContext`

```csharp
public class AppTenant : Tenant
{
    public string PlanName { get; set; } = "free";
}

public class AppDbContext : TenantDbContext<AppTenant>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // No HasDefaultSchema, no schema-qualified ToTable calls: table names must
        // stay unqualified so the same model works against every tenant's schema.
        modelBuilder.Entity<Order>().ToTable("orders");
        base.OnModelCreating(modelBuilder);
    }
}
```

### 2. Wire it up in `Program.cs`

```csharp
builder.Services
    .AddMultiTenancy<AppTenant>(
        db => db.ConnectionString = builder.Configuration.GetConnectionString("Default")!,
        resolution => resolution.BaseDomain = "myapp.com") // acme.myapp.com -> tenant "acme"
    .AddTenantDbContext<AppTenant, AppDbContext>()
    .UseSubdomainResolution()
    .AddTenantLifecycleHandler<AppTenant, SeedDefaultDataOnTenantCreated>(); // optional

var app = builder.Build();

app.UseRouting();
app.UseTenantResolution<AppTenant>(); // after UseAuthentication() if you also use UseClaimResolution()
app.MapControllers();
app.Run();
```

### 3. Create and migrate a tenant

```csharp
app.MapPost("/tenants", async (ITenantProvisioningService<AppTenant> provisioning, CreateTenantRequest req) =>
{
    var tenant = new AppTenant { Name = req.Name };
    // Registers the tenant, CREATE SCHEMAs it, and runs pending migrations against it.
    var created = await provisioning.CreateTenantAsync(tenant, req.Subdomain);
    return Results.Created($"/tenants/{created.Id}", created);
});
```

### 4. Use tenant-scoped data from a request

```csharp
app.MapGet("/orders", async (AppDbContext db) => await db.Orders.ToListAsync());
```

Hitting `GET https://acme.myapp.com/orders` resolves the tenant from the subdomain,
and `AppDbContext` transparently reads/writes only `acme`'s schema. No manual
`WHERE TenantId = ...` filtering required, and no shared table to accidentally leak
across tenants from.

## Configuration reference

`AddMultiTenancy<TTenant>(configureDatabase, configureResolution)`:

```csharp
public class PostgresMultiTenancyOptions
{
    public string ConnectionString { get; set; }
    public TenantIsolationMode Mode { get; set; } = TenantIsolationMode.SchemaPerTenant; // or DatabasePerTenant
    public string DatabaseNameTemplate { get; set; } = "tenant_{0}"; // DatabasePerTenant mode only
    public string? StoreMigrationsAssembly { get; set; } // set to your app's assembly name
}

public class TenantResolutionOptions
{
    public string? BaseDomain { get; set; }                 // for UseSubdomainResolution()
    public HashSet<string> ExcludedSubdomains { get; set; }  // default: { "www" }
    public string TenantHeaderName { get; set; } = "X-Tenant";
    public string TenantClaimType { get; set; } = "tenant_id";
    public bool RequireResolvedTenant { get; set; } = true;  // 404 if unresolved; set false if you have public/admin routes
}
```

Builder chain methods after `AddMultiTenancy`:

- `.AddTenantDbContext<TTenant, TDbContext>()`: registers your tenant-scoped context.
- `.UseSubdomainResolution()` / `.UseHeaderResolution()` / `.UseClaimResolution()`:
  add a resolution strategy; strategies are tried in the order added, first match wins.
- `.AddResolutionStrategy<TTenant, TStrategy>()`: register a custom
  `ITenantResolutionStrategy<TTenant>` (e.g. path-segment routing).
- `.AddTenantLifecycleHandler<TTenant, THandler>()`: react to tenant
  created/migrated/resolved (see `ITenantLifecycleHandler<TTenant>`).

## Migrations

```bash
# Tenant-scoped schema (your own DbContext)
dotnet ef migrations add InitialCreate --context AppDbContext -o Migrations/Tenant

# Tenant registry (public schema); note the backtick-1 for the generic type
dotnet ef migrations add InitialCreate --context 'TenantStoreDbContext`1' -o Migrations/Store
```

You'll need an `IDesignTimeDbContextFactory<T>` for both contexts (EF's CLI tooling
builds your app's host to discover `DbContext` types, and no tenant is ever resolved
outside a request; see `SampleApi/Data/*Factory.cs` for the pattern, and
`docs/ARCHITECTURE.md` for why `AddTenantDbContext` also guards this at runtime via
`EF.IsDesignTime`).

Apply migrations to every active tenant in one call:

```csharp
var summary = await migrator.MigrateAllTenantsAsync(); // ITenantMigrator<TTenant>
// summary.AllSucceeded, summary.Results (per-tenant success/failure)
```

## Correctness notes (read before deploying)

- Register your `TenantDbContext<T>` subclass with `AddDbContext`, never
  `AddDbContextPool`. `AddTenantDbContext` does this for you; don't swap it.
- Never call `HasDefaultSchema` or a schema-qualified `ToTable` in your tenant
  context's `OnModelCreating`. That breaks the search_path routing this library
  relies on.
- `ITenantContext<TTenant>` is read-only by design; only resolution middleware and
  `ITenantScopeFactory<TTenant>` can set the current tenant. Don't try to work
  around this: it's what prevents a scope's tenant from being silently reassigned.
- Outside an HTTP request (a hosted service, a console tool), use
  `ITenantScopeFactory<TTenant>.CreateScope(tenant)` to get a properly tenant-bound
  DI scope before resolving anything tenant-scoped.

## Running the sample

```bash
cd samples/SampleApi
docker compose up -d          # Postgres on localhost:5433
dotnet ef database update --context 'TenantStoreDbContext`1'
dotnet run
```

Then:

```bash
curl -X POST http://localhost:5299/api/tenants \
  -H "Content-Type: application/json" \
  -d '{"name":"Acme Corp","domain":"acme"}'

curl http://localhost:5299/api/todos -H "Host: acme.localhost"
```

## Tests

```bash
dotnet test tests/EfCore.MultiTenancy.UnitTests          # fast, no database
dotnet test tests/EfCore.MultiTenancy.IntegrationTests    # real Postgres via Testcontainers (needs Docker)
```

## Limitations

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md#limitations-vs-django-tenants)
for the full list. In short: no built-in admin UI, no path-segment resolution
strategy out of the box (though the extension point supports adding one),
`DatabasePerTenant` mode is less exercised than the primary `SchemaPerTenant` mode,
and there's no built-in tenant-aware background job integration.

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md) for local dev setup, how to build and pack
the packages locally, and what to check before opening a PR.
