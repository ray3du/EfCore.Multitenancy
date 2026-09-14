using EfCore.MultiTenancy.AspNetCore.DependencyInjection;
using EfCore.MultiTenancy.Core.Abstractions;
using EfCore.MultiTenancy.Core.Exceptions;
using EfCore.MultiTenancy.Core.Models;
using EfCore.MultiTenancy.EfCore.Context;
using EfCore.MultiTenancy.EfCore.Options;
using EfCore.MultiTenancy.EfCore.Provisioning;
using EfCore.MultiTenancy.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EfCore.MultiTenancy.IntegrationTests;

/// <summary>
/// Adversarial, production-oriented tests written independently of the package's own
/// test suite, targeting the specific failure modes an attacker or a careless caller
/// could trigger: forced connection-pool reuse, real concurrency (not just sequential
/// scopes), nested tenant scopes, raw SQL escape hatches, and the state a partially
/// failed provisioning run leaves behind. Each test asserts a concrete, checkable
/// consequence against a real PostgreSQL instance — not just "no exception was thrown".
/// </summary>
[Collection(PostgresCollection.Name)]
public class AdversarialIsolationTests
{
    private readonly PostgresFixture _postgres;

    public AdversarialIsolationTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    private async Task<ServiceProvider> BuildProviderAsync(int? maxPoolSize = null)
    {
        var connectionString = _postgres.ConnectionString;
        if (maxPoolSize is not null)
        {
            connectionString = new NpgsqlConnectionStringBuilder(connectionString)
            {
                MaxPoolSize = maxPoolSize.Value,
            }.ConnectionString;
        }

        var services = new ServiceCollection();
        services.AddLogging();

        services
            .AddMultiTenancy<Tenant>(db =>
            {
                db.ConnectionString = connectionString;
                db.Mode = TenantIsolationMode.SchemaPerTenant;
            })
            .AddTenantDbContext<Tenant, TestTenantDbContext>();

        var provider = services.BuildServiceProvider(validateScopes: true);

        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<TenantStoreDbContext<Tenant>>();
        await store.Database.EnsureCreatedAsync();

        return provider;
    }

    private static Tenant MakeTenant(string schemaSuffix, string namePrefix)
    {
        var schema = $"{namePrefix}_{schemaSuffix}";
        return new Tenant { Name = $"{namePrefix} {schemaSuffix}", SchemaName = schema };
    }

    private static async Task ProvisionAsync(ServiceProvider provider, params Tenant[] tenants)
    {
        using var scope = provider.CreateScope();
        var provisioningService = scope.ServiceProvider.GetRequiredService<ITenantProvisioningService<Tenant>>();
        foreach (var tenant in tenants)
        {
            await provisioningService.CreateTenantAsync(tenant, tenant.SchemaName);
        }
    }

    /// <summary>
    /// The interceptor's entire safety argument (see ARCHITECTURE.md) is that it runs
    /// "every time, no skip-if-unchanged shortcut" because Npgsql does not reset
    /// session state when a physical connection returns to the pool. This test forces
    /// that exact scenario: a pool capped at exactly one physical connection, alternated
    /// between two tenants many times, so every single open after the first is a reused
    /// physical connection that previously had the *other* tenant's search_path set.
    /// </summary>
    [Fact]
    public async Task PooledConnection_ForcedSingleConnectionReuse_NeverLeaksPriorTenantsSchema()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var provider = await BuildProviderAsync(maxPoolSize: 1);

        var tenantA = MakeTenant(suffix, "poola");
        var tenantB = MakeTenant(suffix, "poolb");
        await ProvisionAsync(provider, tenantA, tenantB);

        var scopeFactory = provider.GetRequiredService<ITenantScopeFactory<Tenant>>();

        for (var i = 0; i < 25; i++)
        {
            using (var scopeA = scopeFactory.CreateScope(tenantA))
            {
                var dbA = scopeA.ServiceProvider.GetRequiredService<TestTenantDbContext>();
                dbA.Widgets.Add(new Widget { Name = $"a-{i}" });
                await dbA.SaveChangesAsync();
                Assert.Equal(i + 1, await dbA.Widgets.CountAsync());
            }

            using (var scopeB = scopeFactory.CreateScope(tenantB))
            {
                var dbB = scopeB.ServiceProvider.GetRequiredService<TestTenantDbContext>();
                // Same single physical connection as tenant A just used above; if the
                // interceptor ever skipped re-issuing SET search_path, this would see A's rows.
                Assert.Equal(0, await dbB.Widgets.CountAsync());
            }
        }
    }

    /// <summary>
    /// Real concurrency, not sequential `using` blocks: many tasks racing in parallel
    /// across three tenants against a small (contended) connection pool, each writing a
    /// tenant-tagged marker and immediately reading back only its own tenant's rows.
    /// </summary>
    [Fact]
    public async Task HighConcurrency_ParallelRequestsAcrossThreeTenants_NeverCrossLeak()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var provider = await BuildProviderAsync(maxPoolSize: 5);

        var tenants = new[]
        {
            MakeTenant(suffix, "conc1"),
            MakeTenant(suffix, "conc2"),
            MakeTenant(suffix, "conc3"),
        };
        await ProvisionAsync(provider, tenants);

        var scopeFactory = provider.GetRequiredService<ITenantScopeFactory<Tenant>>();

        var tasks = Enumerable.Range(0, 60).Select(async i =>
        {
            var tenant = tenants[i % tenants.Length];
            using var scope = scopeFactory.CreateScope(tenant);
            var db = scope.ServiceProvider.GetRequiredService<TestTenantDbContext>();

            var marker = $"{tenant.SchemaName}-{i}";
            db.Widgets.Add(new Widget { Name = marker });
            await db.SaveChangesAsync();

            // Read back immediately, interleaved with every other in-flight task.
            var namesSeen = await db.Widgets.Select(w => w.Name).ToListAsync();
            Assert.All(namesSeen, name => Assert.StartsWith(tenant.SchemaName + "-", name, StringComparison.Ordinal));

            return tenant.SchemaName;
        });

        var schemasWritten = await Task.WhenAll(tasks);

        foreach (var tenant in tenants)
        {
            using var scope = scopeFactory.CreateScope(tenant);
            var db = scope.ServiceProvider.GetRequiredService<TestTenantDbContext>();
            var names = await db.Widgets.Select(w => w.Name).ToListAsync();

            var expectedCount = schemasWritten.Count(s => s == tenant.SchemaName);
            Assert.Equal(expectedCount, names.Count);
            Assert.All(names, name => Assert.StartsWith(tenant.SchemaName + "-", name, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// A tenant-bound scope created via <see cref="ITenantScopeFactory{TTenant}"/> while
    /// an outer scope for a *different* tenant is still open and in active use — e.g. a
    /// handler that fans work out to another tenant mid-request. Both must stay fully
    /// isolated, and the outer scope's context must remain correct after the inner one
    /// is disposed.
    /// </summary>
    [Fact]
    public async Task NestedTenantScope_DifferentTenant_IsolatedWhileOuterScopeStillOpen()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var provider = await BuildProviderAsync();

        var tenantA = MakeTenant(suffix, "outer");
        var tenantB = MakeTenant(suffix, "inner");
        await ProvisionAsync(provider, tenantA, tenantB);

        var scopeFactory = provider.GetRequiredService<ITenantScopeFactory<Tenant>>();

        using var outerScope = scopeFactory.CreateScope(tenantA);
        var dbA = outerScope.ServiceProvider.GetRequiredService<TestTenantDbContext>();
        dbA.Widgets.Add(new Widget { Name = "outer-a-1" });
        await dbA.SaveChangesAsync();

        using (var innerScope = scopeFactory.CreateScope(tenantB))
        {
            var dbB = innerScope.ServiceProvider.GetRequiredService<TestTenantDbContext>();
            Assert.Empty(await dbB.Widgets.ToListAsync());

            dbB.Widgets.Add(new Widget { Name = "inner-b-1" });
            await dbB.SaveChangesAsync();
        }

        // The outer context/scope, untouched and still alive, must be unaffected by the
        // nested tenant's scope having existed, run queries, and been disposed.
        var stillSeenByA = await dbA.Widgets.ToListAsync();
        Assert.Single(stillSeenByA);
        Assert.Equal("outer-a-1", stillSeenByA[0].Name);
    }

    [Fact]
    public async Task FromSqlRaw_UnqualifiedTableName_RespectsSearchPath()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var provider = await BuildProviderAsync();

        var tenantA = MakeTenant(suffix, "rawa");
        var tenantB = MakeTenant(suffix, "rawb");
        await ProvisionAsync(provider, tenantA, tenantB);

        var scopeFactory = provider.GetRequiredService<ITenantScopeFactory<Tenant>>();

        using (var scopeA = scopeFactory.CreateScope(tenantA))
        {
            var dbA = scopeA.ServiceProvider.GetRequiredService<TestTenantDbContext>();
            dbA.Widgets.Add(new Widget { Name = "a-widget" });
            await dbA.SaveChangesAsync();
        }

        using var scopeB = scopeFactory.CreateScope(tenantB);
        var dbB = scopeB.ServiceProvider.GetRequiredService<TestTenantDbContext>();

        // An ordinary, unqualified raw-SQL query is exactly as isolated as LINQ, because
        // isolation here is connection-level (search_path), not a per-query WHERE clause.
        var rows = await dbB.Widgets.FromSqlRaw("SELECT * FROM widgets").ToListAsync();

        Assert.Empty(rows);
    }

    /// <summary>
    /// Attack-surface documentation, not a library defect: this package has no per-query
    /// tenant filter to bypass (unlike row-level TenantId designs), but raw SQL that
    /// explicitly schema-qualifies another tenant's schema name sails straight past the
    /// search_path mechanism entirely, because the qualification in the SQL text takes
    /// precedence over the connection's search_path. Any code path that lets attacker-
    /// influenced data reach a `FromSqlRaw`/`FromSqlInterpolated`/`ExecuteSqlRaw` call
    /// (e.g. via string concatenation of a schema name) is a full cross-tenant read/write
    /// primitive. The library cannot prevent this — it is squarely an application-level
    /// SQL-injection concern — but it means "isolation is automatic" does not extend to
    /// hand-written SQL.
    /// </summary>
    [Fact]
    public async Task FromSqlRaw_ExplicitlySchemaQualifiedTableName_BypassesSearchPathIsolation()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var provider = await BuildProviderAsync();

        var tenantA = MakeTenant(suffix, "victim");
        var tenantB = MakeTenant(suffix, "attacker");
        await ProvisionAsync(provider, tenantA, tenantB);

        var scopeFactory = provider.GetRequiredService<ITenantScopeFactory<Tenant>>();

        using (var scopeA = scopeFactory.CreateScope(tenantA))
        {
            var dbA = scopeA.ServiceProvider.GetRequiredService<TestTenantDbContext>();
            dbA.Widgets.Add(new Widget { Name = "victim-secret" });
            await dbA.SaveChangesAsync();
        }

        using var scopeB = scopeFactory.CreateScope(tenantB);
        var dbB = scopeB.ServiceProvider.GetRequiredService<TestTenantDbContext>();

        // dbB's connection has search_path = "attacker", public — yet a schema-qualified
        // raw query still reaches tenant A's data.
        var leaked = await dbB.Widgets
            .FromSqlRaw($"SELECT * FROM \"{tenantA.SchemaName}\".widgets")
            .ToListAsync();

        Assert.Single(leaked);
        Assert.Equal("victim-secret", leaked[0].Name);
    }

    [Fact]
    public async Task IgnoreQueryFilters_HasNoEffect_BecauseNoGlobalFiltersAreDefined()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var provider = await BuildProviderAsync();

        var tenantA = MakeTenant(suffix, "filtera");
        var tenantB = MakeTenant(suffix, "filterb");
        await ProvisionAsync(provider, tenantA, tenantB);

        var scopeFactory = provider.GetRequiredService<ITenantScopeFactory<Tenant>>();

        using (var scopeA = scopeFactory.CreateScope(tenantA))
        {
            var dbA = scopeA.ServiceProvider.GetRequiredService<TestTenantDbContext>();
            dbA.Widgets.Add(new Widget { Name = "a-widget" });
            await dbA.SaveChangesAsync();
        }

        using var scopeB = scopeFactory.CreateScope(tenantB);
        var dbB = scopeB.ServiceProvider.GetRequiredService<TestTenantDbContext>();

        var withFilters = await dbB.Widgets.ToListAsync();
        var withoutFilters = await dbB.Widgets.IgnoreQueryFilters().ToListAsync();

        // IgnoreQueryFilters() cannot leak tenant A's row precisely because there is no
        // HasQueryFilter to ignore in the first place — isolation is not filter-based.
        Assert.Empty(withFilters);
        Assert.Empty(withoutFilters);
    }

    [Fact]
    public async Task TenantDbContext_ResolvedOutsideAnyTenantScope_ThrowsTenantContextException()
    {
        await using var provider = await BuildProviderAsync();

        using var scope = provider.CreateScope(); // SetTenant deliberately never called
        Assert.Throws<TenantContextException>(
            () => scope.ServiceProvider.GetRequiredService<TestTenantDbContext>());
    }

    /// <summary>
    /// <c>CreateTenantAsync</c> writes the registry row (IsActive = true, domain claimed)
    /// before schema provisioning/migration are confirmed durable, with no later health
    /// check or compensating action. This reproduces the end state a crash between
    /// "schema created" and "migrations applied" — or any later manual/accidental schema
    /// loss — would leave: a tenant that is still active and resolvable by domain, but
    /// whose schema no longer backs it.
    /// </summary>
    [Fact]
    public async Task TenantWithSchemaLostAfterProvisioning_StaysActiveAndDiscoverable_ButQueriesFailUngracefully()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var provider = await BuildProviderAsync();

        var tenant = MakeTenant(suffix, "ghost");
        await ProvisionAsync(provider, tenant);

        await using (var connection = new NpgsqlConnection(_postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"DROP SCHEMA \"{tenant.SchemaName}\" CASCADE";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var readScope = provider.CreateScope())
        {
            var store = readScope.ServiceProvider.GetRequiredService<ITenantStore<Tenant>>();
            var stillFound = await store.FindByDomainAsync(tenant.SchemaName);

            // The registry has no way to know the schema is gone: resolution middleware
            // would still happily route real requests to this "active" tenant.
            Assert.NotNull(stillFound);
            Assert.True(stillFound!.IsActive);
        }

        var scopeFactory = provider.GetRequiredService<ITenantScopeFactory<Tenant>>();
        using var brokenScope = scopeFactory.CreateScope(tenant);
        var db = brokenScope.ServiceProvider.GetRequiredService<TestTenantDbContext>();

        // No clean "tenant unavailable" error — a raw Postgres exception (42P01, undefined
        // table) surfaces straight from the data layer.
        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Widgets.ToListAsync());
        Assert.Equal(PostgresErrorCodes.UndefinedTable, ex.SqlState);
    }
}
