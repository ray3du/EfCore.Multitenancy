using EfCore.MultiTenancy.AspNetCore.DependencyInjection;
using EfCore.MultiTenancy.Core.Abstractions;
using EfCore.MultiTenancy.Core.Models;
using EfCore.MultiTenancy.EfCore.Administration;
using EfCore.MultiTenancy.EfCore.Context;
using EfCore.MultiTenancy.EfCore.Options;
using EfCore.MultiTenancy.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EfCore.MultiTenancy.IntegrationTests;

/// <summary>
/// <see cref="ITenantAdministrationService{TTenant}"/> against a real, throwaway
/// PostgreSQL instance: batch creation/deletion semantics, identifier resolution by
/// schema name/id/domain, <see cref="TenantDeletionMode.KeepSchema"/>, and the
/// registry-visible-but-inactive lookups <see cref="Core.Abstractions.ITenantStore{TTenant}"/>
/// deliberately can't do.
/// </summary>
[Collection(PostgresCollection.Name)]
public class TenantAdministrationServiceTests
{
    private readonly PostgresFixture _postgres;

    public TenantAdministrationServiceTests(PostgresFixture postgres)
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

    private static Tenant MakeTenant(string schemaSuffix, string namePrefix) =>
        new() { Name = $"{namePrefix} {schemaSuffix}", SchemaName = $"{namePrefix}_{schemaSuffix}" };

    private static async Task<bool> SchemaExistsAsync(string connectionString, string schemaName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = @schema)";
        cmd.Parameters.AddWithValue("schema", schemaName);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task CreateTenantsAsync_BatchWithADuplicateDomain_ReportsEachIndependently()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var provider = await BuildProviderAsync();
        using var scope = provider.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<ITenantAdministrationService<Tenant>>();

        var sharedDomain = $"dup_{suffix}";
        var requests = new[]
        {
            new TenantCreationRequest<Tenant>(MakeTenant(suffix, "first"), sharedDomain),
            new TenantCreationRequest<Tenant>(MakeTenant(suffix, "second"), sharedDomain), // same domain: unique index violation
        };

        var summary = await administration.CreateTenantsAsync(requests);

        Assert.Equal(1, summary.SuccessCount);
        Assert.Equal(1, summary.FailureCount);
        Assert.False(summary.AllSucceeded);
        Assert.True(summary.Results[0].Succeeded);
        Assert.False(summary.Results[1].Succeeded);
        Assert.NotNull(summary.Results[1].Error);

        // The first tenant's creation must not have been rolled back by the second's failure.
        var stillThere = await administration.FindAsync(TenantIdentifier.ForDomain(sharedDomain));
        Assert.NotNull(stillThere);
        Assert.Equal(requests[0].Tenant.Id, stillThere!.Id);
    }

    [Fact]
    public async Task DeleteTenantsAsync_BySchemaIdAndDomain_EachResolvesAndFullyRemovesTheTenant()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var provider = await BuildProviderAsync();
        using var scope = provider.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<ITenantAdministrationService<Tenant>>();

        var bySchema = MakeTenant(suffix, "byschema");
        var byId = MakeTenant(suffix, "byid");
        var byDomain = MakeTenant(suffix, "bydomain");
        var byDomainDomain = $"domain_{suffix}";

        var created = await administration.CreateTenantsAsync(new[]
        {
            new TenantCreationRequest<Tenant>(bySchema, bySchema.SchemaName),
            new TenantCreationRequest<Tenant>(byId, byId.SchemaName),
            new TenantCreationRequest<Tenant>(byDomain, byDomainDomain),
        });
        Assert.True(created.AllSucceeded);
        var byIdCreated = created.Results[1].Tenant;

        var summary = await administration.DeleteTenantsAsync(new[]
        {
            TenantIdentifier.ForSchemaName(bySchema.SchemaName),
            TenantIdentifier.ForId(byIdCreated.Id),
            TenantIdentifier.ForDomain(byDomainDomain),
        });

        Assert.True(summary.AllSucceeded);
        Assert.Equal(3, summary.SuccessCount);

        foreach (var tenant in new[] { bySchema, byId, byDomain })
        {
            Assert.Null(await administration.FindAsync(TenantIdentifier.ForSchemaName(tenant.SchemaName)));
            Assert.False(await SchemaExistsAsync(_postgres.ConnectionString, tenant.SchemaName));
        }
    }

    [Fact]
    public async Task DeleteTenantsAsync_UnknownIdentifierInBatch_ReportsFailureWithoutAbortingTheOthers()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var provider = await BuildProviderAsync();
        using var scope = provider.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<ITenantAdministrationService<Tenant>>();

        var real = MakeTenant(suffix, "real");
        await administration.CreateTenantsAsync(new[] { new TenantCreationRequest<Tenant>(real, real.SchemaName) });

        var summary = await administration.DeleteTenantsAsync(new[]
        {
            TenantIdentifier.ForSchemaName(real.SchemaName),
            TenantIdentifier.ForSchemaName($"does_not_exist_{suffix}"),
        });

        Assert.False(summary.AllSucceeded);
        Assert.Equal(1, summary.SuccessCount);
        Assert.Equal(1, summary.FailureCount);
        Assert.True(summary.Results.Single(r => r.Identifier.SchemaName == real.SchemaName).Succeeded);
        Assert.False(summary.Results.Single(r => r.Identifier.SchemaName!.StartsWith("does_not_exist")).Succeeded);

        Assert.Null(await administration.FindAsync(TenantIdentifier.ForSchemaName(real.SchemaName)));
    }

    [Fact]
    public async Task DeleteTenantsAsync_KeepSchemaMode_RemovesRegistryRowButLeavesTheSchemaIntact()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var provider = await BuildProviderAsync();
        using var scope = provider.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<ITenantAdministrationService<Tenant>>();

        var tenant = MakeTenant(suffix, "kept");
        await administration.CreateTenantsAsync(new[] { new TenantCreationRequest<Tenant>(tenant, tenant.SchemaName) });

        var summary = await administration.DeleteTenantsAsync(
            new[] { TenantIdentifier.ForSchemaName(tenant.SchemaName) }, TenantDeletionMode.KeepSchema);

        Assert.True(summary.AllSucceeded);
        Assert.Null(await administration.FindAsync(TenantIdentifier.ForSchemaName(tenant.SchemaName)));
        Assert.True(await SchemaExistsAsync(_postgres.ConnectionString, tenant.SchemaName));
    }

    [Fact]
    public async Task ListTenantsAsync_ExcludesInactiveByDefault_IncludesThemWhenRequested()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var provider = await BuildProviderAsync();
        using var scope = provider.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<ITenantAdministrationService<Tenant>>();

        var active = MakeTenant(suffix, "active");
        var inactive = MakeTenant(suffix, "inactive");
        await administration.CreateTenantsAsync(new[]
        {
            new TenantCreationRequest<Tenant>(active, active.SchemaName),
            new TenantCreationRequest<Tenant>(inactive, inactive.SchemaName),
        });

        var storeDbContext = scope.ServiceProvider.GetRequiredService<TenantStoreDbContext<Tenant>>();
        var toDeactivate = await storeDbContext.Tenants.FirstAsync(t => t.SchemaName == inactive.SchemaName);
        toDeactivate.IsActive = false;
        await storeDbContext.SaveChangesAsync();

        var activeOnly = await administration.ListTenantsAsync(includeInactive: false);
        var all = await administration.ListTenantsAsync(includeInactive: true);

        Assert.Contains(activeOnly, t => t.SchemaName == active.SchemaName);
        Assert.DoesNotContain(activeOnly, t => t.SchemaName == inactive.SchemaName);
        Assert.Contains(all, t => t.SchemaName == active.SchemaName);
        Assert.Contains(all, t => t.SchemaName == inactive.SchemaName);
    }

    [Fact]
    public async Task FindAsync_ResolvesADeactivatedTenant_UnlikeITenantStore()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var provider = await BuildProviderAsync();
        using var scope = provider.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<ITenantAdministrationService<Tenant>>();
        var tenantStore = scope.ServiceProvider.GetRequiredService<ITenantStore<Tenant>>();

        var tenant = MakeTenant(suffix, "deactivated");
        var domain = tenant.SchemaName;
        await administration.CreateTenantsAsync(new[] { new TenantCreationRequest<Tenant>(tenant, domain) });

        var storeDbContext = scope.ServiceProvider.GetRequiredService<TenantStoreDbContext<Tenant>>();
        var toDeactivate = await storeDbContext.Tenants.FirstAsync(t => t.SchemaName == tenant.SchemaName);
        toDeactivate.IsActive = false;
        await storeDbContext.SaveChangesAsync();

        // ITenantStore is resolution-facing and only ever returns active tenants...
        Assert.Null(await tenantStore.FindByDomainAsync(domain));

        // ...while administration can still find (and therefore delete/manage) it.
        var found = await administration.FindAsync(TenantIdentifier.ForSchemaName(tenant.SchemaName));
        Assert.NotNull(found);
        Assert.False(found!.IsActive);
    }
}
