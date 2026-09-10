using EfCore.MultiTenancy.Core.Models;

namespace EfCore.MultiTenancy.Core.Abstractions;

/// <summary>
/// Read access to the tenant registry (the public/shared schema). Implemented by
/// the EF Core layer against <c>TenantStoreDbContext</c>; consumed by resolution
/// strategies so they stay storage-agnostic and transport-agnostic.
/// </summary>
public interface ITenantStore<TTenant> where TTenant : class, ITenant
{
    /// <summary>
    /// Looks up an active tenant by one of its registered domains. Comparison is
    /// case-insensitive. Returns null if no active tenant matches.
    /// </summary>
    Task<TTenant?> FindByDomainAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>Looks up an active tenant by id. Returns null if not found or inactive.</summary>
    Task<TTenant?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Enumerates every active tenant, e.g. for cross-tenant migration runs.</summary>
    Task<IReadOnlyList<TTenant>> GetAllActiveAsync(CancellationToken cancellationToken = default);
}
