using EfCore.MultiTenancy.Core.Models;

namespace EfCore.MultiTenancy.EfCore.Migrations;

/// <summary>Applies pending EF Core migrations to one tenant's schema, or to all of them.</summary>
public interface ITenantMigrator<TTenant> where TTenant : class, ITenant
{
    /// <summary>
    /// Applies pending migrations to <paramref name="tenant"/>'s schema. The schema
    /// itself must already exist (see <see cref="Provisioning.ITenantSchemaProvisioner{TTenant}"/>) —
    /// <c>CREATE TABLE __EFMigrationsHistory</c> fails against a schema that hasn't
    /// been created yet.
    /// </summary>
    Task MigrateTenantAsync(TTenant tenant, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies pending migrations to every active tenant, one at a time, in its own
    /// DI scope. A failure for one tenant is captured in the returned summary rather
    /// than aborting the remaining tenants.
    /// </summary>
    Task<TenantMigrationSummary<TTenant>> MigrateAllTenantsAsync(CancellationToken cancellationToken = default);
}
