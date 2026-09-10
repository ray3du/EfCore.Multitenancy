using EfCore.MultiTenancy.Core.Abstractions;
using EfCore.MultiTenancy.Core.Events;
using EfCore.MultiTenancy.Core.Models;
using EfCore.MultiTenancy.Core.Validation;
using EfCore.MultiTenancy.EfCore.Context;
using EfCore.MultiTenancy.EfCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EfCore.MultiTenancy.EfCore.Provisioning;

public sealed class TenantProvisioningService<TTenant> : ITenantProvisioningService<TTenant>
    where TTenant : Tenant
{
    private readonly TenantStoreDbContext<TTenant> _storeDbContext;
    private readonly ITenantSchemaProvisioner<TTenant> _schemaProvisioner;
    private readonly ITenantMigrator<TTenant> _migrator;
    private readonly ITenantScopeFactory<TTenant> _scopeFactory;
    private readonly ILogger<TenantProvisioningService<TTenant>> _logger;

    public TenantProvisioningService(
        TenantStoreDbContext<TTenant> storeDbContext,
        ITenantSchemaProvisioner<TTenant> schemaProvisioner,
        ITenantMigrator<TTenant> migrator,
        ITenantScopeFactory<TTenant> scopeFactory,
        ILogger<TenantProvisioningService<TTenant>> logger)
    {
        _storeDbContext = storeDbContext;
        _schemaProvisioner = schemaProvisioner;
        _migrator = migrator;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<TTenant> CreateTenantAsync(TTenant tenant, string domain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        if (string.IsNullOrWhiteSpace(tenant.SchemaName))
        {
            tenant.SchemaName = SchemaNameValidator.Normalize(tenant.Name);
        }
        else
        {
            SchemaNameValidator.Validate(tenant.SchemaName);
        }

        tenant.Domains.Add(new TenantDomain
        {
            TenantId = tenant.Id,
            Domain = domain.Trim().ToLowerInvariant(),
            IsPrimary = true,
        });

        // Registry row is written first, inside its own save, so a later failure in
        // schema creation/migration leaves a discoverable (if not-yet-usable) tenant
        // row rather than a schema with no registry entry pointing at it.
        _storeDbContext.Tenants.Add(tenant);
        await _storeDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _schemaProvisioner.CreateAsync(tenant, cancellationToken).ConfigureAwait(false);

        // Raised from a scope bound to `tenant` so a handler that injects
        // ITenantContext<TTenant> (or, once migrated, a tenant-scoped DbContext) sees
        // the tenant being created rather than the caller's own scope. At this point
        // the schema exists but no tables do yet — handlers needing tables should use
        // OnTenantMigratedAsync instead.
        using (var createdScope = _scopeFactory.CreateScope(tenant))
        {
            var createdDispatcher = createdScope.ServiceProvider.GetRequiredService<TenantLifecycleDispatcher<TTenant>>();
            await createdDispatcher.RaiseTenantCreatedAsync(tenant, cancellationToken).ConfigureAwait(false);
        }

        await _migrator.MigrateTenantAsync(tenant, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Provisioned tenant {TenantId} ({SchemaName}) for domain {Domain}",
            tenant.Id, tenant.SchemaName, domain);

        return tenant;
    }
}
