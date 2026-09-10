using EfCore.MultiTenancy.AspNetCore.DependencyInjection;
using EfCore.MultiTenancy.Core.Abstractions;
using EfCore.MultiTenancy.Core.Models;
using EfCore.MultiTenancy.EfCore.Context;
using EfCore.MultiTenancy.EfCore.Options;
using EfCore.MultiTenancy.EfCore.Provisioning;
using EfCore.MultiTenancy.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EfCore.MultiTenancy.IntegrationTests;

/// <summary>
/// Runs the real DI wiring (<c>AddMultiTenancy</c> + <c>AddTenantDbContext</c>) against
/// a real, throwaway PostgreSQL container and real EF Core migrations — the same
/// combination that, during development, silently let one tenant's migration read
/// another schema's migrations-history row and skip creating its tables entirely
/// (see the remarks on <c>MultiTenancyBuilderExtensions.AddTenantDbContext</c>). Unit
/// tests with fakes cannot catch that class of bug; only a real database can.
/// </summary>
[Collection(PostgresCollection.Name)]
public class SchemaIsolationTests
{
    private readonly PostgresFixture _postgres;

    public SchemaIsolationTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    private async Task<ServiceProvider> BuildProviderAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services
            .AddMultiTenancy<Tenant>(db =>
            {
                db.ConnectionString = _postgres.ConnectionString;
                db.Mode = TenantIsolationMode.SchemaPerTenant;
            })
            .AddTenantDbContext<Tenant, TestTenantDbContext>();

        var provider = services.BuildServiceProvider(validateScopes: true);

        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<TenantStoreDbContext<Tenant>>();
        await store.Database.EnsureCreatedAsync();

        return provider;
    }

    // CreateTenantAsync adds the primary TenantDomain itself from the `domain`
    // argument, so the tenant returned here is intentionally domain-less.
    private static Tenant MakeTenant(string schemaSuffix, string namePrefix)
    {
        var schema = $"{namePrefix}_{schemaSuffix}";
        return new Tenant { Name = $"{namePrefix} {schemaSuffix}", SchemaName = schema };
    }

    [Fact]
    public async Task TwoTenants_MigratedIndependently_EachOnlySeesItsOwnData()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var provider = await BuildProviderAsync();

        var tenantA = MakeTenant(suffix, "acme");
        var tenantB = MakeTenant(suffix, "globex");

        using (var scope = provider.CreateScope())
        {
            var provisioningService = scope.ServiceProvider.GetRequiredService<ITenantProvisioningService<Tenant>>();
            await provisioningService.CreateTenantAsync(tenantA, tenantA.SchemaName);
            await provisioningService.CreateTenantAsync(tenantB, tenantB.SchemaName);
        }

        var scopeFactory = provider.GetRequiredService<ITenantScopeFactory<Tenant>>();

        using (var scopeA = scopeFactory.CreateScope(tenantA))
        {
            var dbA = scopeA.ServiceProvider.GetRequiredService<TestTenantDbContext>();
            dbA.Widgets.Add(new Widget { Name = "acme-only-widget" });
            await dbA.SaveChangesAsync();
        }

        using (var scopeB = scopeFactory.CreateScope(tenantB))
        {
            var dbB = scopeB.ServiceProvider.GetRequiredService<TestTenantDbContext>();
            var widgetsVisibleToB = await dbB.Widgets.ToListAsync();

            Assert.Empty(widgetsVisibleToB); // the whole point: B must not see A's data
        }

        using (var scopeA2 = scopeFactory.CreateScope(tenantA))
        {
            var dbA2 = scopeA2.ServiceProvider.GetRequiredService<TestTenantDbContext>();
            var widgetsVisibleToA = await dbA2.Widgets.ToListAsync();

            Assert.Single(widgetsVisibleToA);
            Assert.Equal("acme-only-widget", widgetsVisibleToA[0].Name);
        }
    }

    [Fact]
    public async Task MigratingSecondTenant_ActuallyCreatesItsTables_NotJustReadsFirstTenantsHistory()
    {
        // Direct regression test for the migrations-history-schema bug: assert the
        // "widgets" table physically exists in both schemas, not just that queries
        // against it return the "right" (possibly accidentally empty) results.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var provider = await BuildProviderAsync();

        var tenantA = MakeTenant(suffix, "first");
        var tenantB = MakeTenant(suffix, "second");

        using (var scope = provider.CreateScope())
        {
            var provisioningService = scope.ServiceProvider.GetRequiredService<ITenantProvisioningService<Tenant>>();
            await provisioningService.CreateTenantAsync(tenantA, tenantA.SchemaName);
            await provisioningService.CreateTenantAsync(tenantB, tenantB.SchemaName);
        }

        await using var connection = new NpgsqlConnection(_postgres.ConnectionString);
        await connection.OpenAsync();

        foreach (var schema in new[] { tenantA.SchemaName, tenantB.SchemaName })
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT EXISTS (SELECT 1 FROM information_schema.tables " +
                "WHERE table_schema = @schema AND table_name = 'widgets')";
            cmd.Parameters.AddWithValue("schema", schema);
            var exists = (bool)(await cmd.ExecuteScalarAsync())!;

            Assert.True(exists, $"Expected 'widgets' table to exist in schema '{schema}'.");
        }
    }

    [Fact]
    public async Task EachTenantSchema_HasItsOwnIndependentMigrationsHistoryTable()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var provider = await BuildProviderAsync();

        var tenant = MakeTenant(suffix, "solo");

        using (var scope = provider.CreateScope())
        {
            var provisioningService = scope.ServiceProvider.GetRequiredService<ITenantProvisioningService<Tenant>>();
            await provisioningService.CreateTenantAsync(tenant, tenant.SchemaName);
        }

        await using var connection = new NpgsqlConnection(_postgres.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables " +
            "WHERE table_schema = @schema AND table_name = '__EFMigrationsHistory')";
        cmd.Parameters.AddWithValue("schema", tenant.SchemaName);
        var exists = (bool)(await cmd.ExecuteScalarAsync())!;

        Assert.True(exists, $"Expected '__EFMigrationsHistory' inside tenant schema '{tenant.SchemaName}', " +
            "not only in 'public' — otherwise a second tenant's migration would read the first tenant's history.");
    }

    [Fact]
    public async Task TenantStore_FindByDomain_OnlyReturnsActiveMatchingTenant()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var provider = await BuildProviderAsync();
        var tenant = MakeTenant(suffix, "lookup");

        using (var scope = provider.CreateScope())
        {
            var provisioningService = scope.ServiceProvider.GetRequiredService<ITenantProvisioningService<Tenant>>();
            await provisioningService.CreateTenantAsync(tenant, tenant.SchemaName);
        }

        using var readScope = provider.CreateScope();
        var store = readScope.ServiceProvider.GetRequiredService<ITenantStore<Tenant>>();

        var found = await store.FindByDomainAsync(tenant.SchemaName);
        var notFound = await store.FindByDomainAsync("does-not-exist-" + suffix);

        Assert.NotNull(found);
        Assert.Equal(tenant.Id, found!.Id);
        Assert.Null(notFound);
    }
}
