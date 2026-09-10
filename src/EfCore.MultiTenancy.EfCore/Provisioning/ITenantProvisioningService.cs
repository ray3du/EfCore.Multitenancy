using EfCore.MultiTenancy.Core.Models;

namespace EfCore.MultiTenancy.EfCore.Provisioning;

/// <summary>
/// High-level "create a tenant" entry point: registers the tenant row, provisions
/// its schema/database, and applies migrations, in that order. Use this from an
/// admin endpoint or signup flow rather than composing the lower-level pieces
/// yourself unless you need custom ordering (e.g. seeding between create and migrate).
/// </summary>
public interface ITenantProvisioningService<TTenant> where TTenant : Core.Models.Tenant
{
    /// <summary>
    /// Registers <paramref name="tenant"/> in the tenant store (schema name is
    /// validated/derived from <paramref name="tenant"/>.Name if
    /// <paramref name="tenant"/>.SchemaName is empty), creates its physical schema,
    /// and applies pending migrations. Also adds <paramref name="domain"/> as the
    /// tenant's primary domain.
    /// </summary>
    Task<TTenant> CreateTenantAsync(TTenant tenant, string domain, CancellationToken cancellationToken = default);
}
