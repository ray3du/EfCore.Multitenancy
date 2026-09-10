using EfCore.MultiTenancy.Core.Abstractions;
using EfCore.MultiTenancy.Core.Events;
using EfCore.MultiTenancy.Core.Models;
using EfCore.MultiTenancy.EfCore.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EfCore.MultiTenancy.EfCore.Migrations;

/// <summary>
/// Default <see cref="ITenantMigrator{TTenant}"/>. Generic over the application's own
/// <c>TenantDbContext</c> subclass (<typeparamref name="TDbContext"/>) so it can call
/// <c>Database.MigrateAsync()</c> against the app's actual model and migrations
/// assembly, not a library-owned one.
/// </summary>
public sealed class TenantMigrator<TTenant, TDbContext> : ITenantMigrator<TTenant>
    where TTenant : class, ITenant
    where TDbContext : TenantDbContext<TTenant>
{
    private readonly ITenantStore<TTenant> _tenantStore;
    private readonly ITenantScopeFactory<TTenant> _scopeFactory;
    private readonly ILogger<TenantMigrator<TTenant, TDbContext>> _logger;

    public TenantMigrator(
        ITenantStore<TTenant> tenantStore,
        ITenantScopeFactory<TTenant> scopeFactory,
        ILogger<TenantMigrator<TTenant, TDbContext>> logger)
    {
        _tenantStore = tenantStore;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task MigrateTenantAsync(TTenant tenant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        // Everything below runs inside a scope whose ITenantContext already resolves
        // to `tenant` — including the dispatcher, so an ITenantLifecycleHandler that
        // injects a tenant-scoped DbContext to seed data can safely do so instead of
        // hitting the caller's (e.g. an admin request's) unrelated or absent tenant.
        using var scope = _scopeFactory.CreateScope(tenant);
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Migrated schema {SchemaName} for tenant {TenantId}", tenant.SchemaName, tenant.Id);

        var dispatcher = scope.ServiceProvider.GetRequiredService<TenantLifecycleDispatcher<TTenant>>();
        await dispatcher.RaiseTenantMigratedAsync(tenant, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TenantMigrationSummary<TTenant>> MigrateAllTenantsAsync(CancellationToken cancellationToken = default)
    {
        var tenants = await _tenantStore.GetAllActiveAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<TenantMigrationResult<TTenant>>(tenants.Count);

        foreach (var tenant in tenants)
        {
            try
            {
                await MigrateTenantAsync(tenant, cancellationToken).ConfigureAwait(false);
                results.Add(new TenantMigrationResult<TTenant>(tenant, true, null));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to migrate tenant {TenantId} (schema {SchemaName})", tenant.Id, tenant.SchemaName);
                results.Add(new TenantMigrationResult<TTenant>(tenant, false, ex));
            }
        }

        return new TenantMigrationSummary<TTenant>(results);
    }
}
