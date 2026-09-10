using EfCore.MultiTenancy.Core.Models;

namespace EfCore.MultiTenancy.EfCore.Provisioning;

/// <summary>
/// Creates and removes the physical storage (schema, or database in
/// <see cref="Options.TenantIsolationMode.DatabasePerTenant"/> mode) backing a
/// tenant. This only manipulates the container — it does not run EF migrations or
/// touch the tenant registry row; see <see cref="Migrations.ITenantMigrator{TTenant}"/>
/// and <see cref="TenantProvisioningService{TTenant}"/> for those.
/// </summary>
public interface ITenantSchemaProvisioner<TTenant> where TTenant : class, ITenant
{
    Task CreateAsync(TTenant tenant, CancellationToken cancellationToken = default);

    /// <summary>
    /// Irreversibly drops the tenant's schema/database and all data in it. Intended
    /// for test cleanup and explicit tenant offboarding — there is no confirmation
    /// or soft-delete step here, callers must gate this appropriately.
    /// </summary>
    Task DropAsync(TTenant tenant, CancellationToken cancellationToken = default);
}
