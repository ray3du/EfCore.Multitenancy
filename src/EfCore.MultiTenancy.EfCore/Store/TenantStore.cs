using EfCore.MultiTenancy.Core.Abstractions;
using EfCore.MultiTenancy.Core.Models;
using EfCore.MultiTenancy.EfCore.Context;
using Microsoft.EntityFrameworkCore;

namespace EfCore.MultiTenancy.EfCore.Store;

/// <summary>
/// Default <see cref="ITenantStore{TTenant}"/> backed by <see cref="TenantStoreDbContext{TTenant}"/>.
/// Registered scoped; safe to inject alongside a tenant-scoped context because it
/// always reads from the public schema regardless of the current tenant.
/// </summary>
public sealed class TenantStore<TTenant> : ITenantStore<TTenant> where TTenant : Tenant
{
    private readonly TenantStoreDbContext<TTenant> _dbContext;

    public TenantStore(TenantStoreDbContext<TTenant> dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TTenant?> FindByDomainAsync(string domain, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        var normalized = domain.Trim().ToLowerInvariant();

        return await _dbContext.Tenants
            .AsNoTracking()
            .Where(t => t.IsActive && t.Domains.Any(d => d.Domain == normalized))
            .Include(t => t.Domains)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<TTenant?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Tenants
            .AsNoTracking()
            .Where(t => t.IsActive && t.Id == tenantId)
            .Include(t => t.Domains)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TTenant>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Tenants
            .AsNoTracking()
            .Where(t => t.IsActive)
            .Include(t => t.Domains)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
