using EfCore.MultiTenancy.Core.Exceptions;
using EfCore.MultiTenancy.Core.Models;
using EfCore.MultiTenancy.EfCore.Context;
using EfCore.MultiTenancy.EfCore.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EfCore.MultiTenancy.EfCore.Administration;

/// <summary>Default <see cref="ITenantAdministrationService{TTenant}"/>, backed by <see cref="TenantStoreDbContext{TTenant}"/>.</summary>
public sealed class TenantAdministrationService<TTenant> : ITenantAdministrationService<TTenant>
    where TTenant : Tenant
{
    private readonly TenantStoreDbContext<TTenant> _storeDbContext;
    private readonly ITenantProvisioningService<TTenant> _provisioningService;
    private readonly ITenantSchemaProvisioner<TTenant> _schemaProvisioner;
    private readonly ILogger<TenantAdministrationService<TTenant>> _logger;

    public TenantAdministrationService(
        TenantStoreDbContext<TTenant> storeDbContext,
        ITenantProvisioningService<TTenant> provisioningService,
        ITenantSchemaProvisioner<TTenant> schemaProvisioner,
        ILogger<TenantAdministrationService<TTenant>> logger)
    {
        _storeDbContext = storeDbContext;
        _provisioningService = provisioningService;
        _schemaProvisioner = schemaProvisioner;
        _logger = logger;
    }

    public async Task<TenantCreationSummary<TTenant>> CreateTenantsAsync(
        IReadOnlyList<TenantCreationRequest<TTenant>> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
        {
            throw new ArgumentException("At least one tenant creation request is required.", nameof(requests));
        }

        var results = new List<TenantCreationResult<TTenant>>(requests.Count);
        foreach (var request in requests)
        {
            try
            {
                var created = await _provisioningService
                    .CreateTenantAsync(request.Tenant, request.Domain, cancellationToken)
                    .ConfigureAwait(false);
                results.Add(new TenantCreationResult<TTenant>(created, request.Domain, true, null));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create tenant {Name} ({Domain})", request.Tenant.Name, request.Domain);
                results.Add(new TenantCreationResult<TTenant>(request.Tenant, request.Domain, false, ex));
            }
        }

        return new TenantCreationSummary<TTenant>(results);
    }

    public async Task<TenantDeletionSummary<TTenant>> DeleteTenantsAsync(
        IReadOnlyList<TenantIdentifier> identifiers,
        TenantDeletionMode mode = TenantDeletionMode.DropSchema,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifiers);
        if (identifiers.Count == 0)
        {
            throw new ArgumentException("At least one tenant identifier is required.", nameof(identifiers));
        }

        var results = new List<TenantDeletionResult<TTenant>>(identifiers.Count);
        foreach (var identifier in identifiers)
        {
            try
            {
                var tenant = await FindTrackedAsync(identifier, cancellationToken).ConfigureAwait(false);
                if (tenant is null)
                {
                    results.Add(new TenantDeletionResult<TTenant>(
                        identifier, null, false, new TenantNotFoundException(identifier.ToString())));
                    continue;
                }

                // Deregister before dropping the schema — see the interface doc comment
                // for why this order, specifically, is what keeps a mid-failure state safe.
                _storeDbContext.Tenants.Remove(tenant);
                await _storeDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                if (mode == TenantDeletionMode.DropSchema)
                {
                    await _schemaProvisioner.DropAsync(tenant, cancellationToken).ConfigureAwait(false);
                }

                _logger.LogInformation("Deleted tenant {TenantId} (schema {SchemaName})", tenant.Id, tenant.SchemaName);
                results.Add(new TenantDeletionResult<TTenant>(identifier, tenant, true, null));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete tenant for {Identifier}", identifier);
                results.Add(new TenantDeletionResult<TTenant>(identifier, null, false, ex));
            }
        }

        return new TenantDeletionSummary<TTenant>(results);
    }

    public Task<TTenant?> FindAsync(TenantIdentifier identifier, CancellationToken cancellationToken = default) =>
        FindTrackedAsync(identifier, cancellationToken, asNoTracking: true);

    public async Task<IReadOnlyList<TTenant>> ListTenantsAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = _storeDbContext.Tenants.AsNoTracking().Include(t => t.Domains).AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(t => t.IsActive);
        }

        return await query.OrderBy(t => t.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task<TTenant?> FindTrackedAsync(TenantIdentifier identifier, CancellationToken cancellationToken, bool asNoTracking = false)
    {
        var query = asNoTracking
            ? _storeDbContext.Tenants.AsNoTracking().Include(t => t.Domains).AsQueryable()
            : _storeDbContext.Tenants.Include(t => t.Domains).AsQueryable();

        return identifier switch
        {
            { Id: { } id } => query.FirstOrDefaultAsync(t => t.Id == id, cancellationToken),
            { SchemaName: { } schema } => query.FirstOrDefaultAsync(t => t.SchemaName == schema, cancellationToken),
            { Domain: { } domain } => FindByDomainAsync(query, domain, cancellationToken),
            _ => throw new ArgumentException("A tenant identifier must specify Id, SchemaName, or Domain.", nameof(identifier)),
        };
    }

    private static Task<TTenant?> FindByDomainAsync(IQueryable<TTenant> query, string domain, CancellationToken cancellationToken)
    {
        var normalized = domain.Trim().ToLowerInvariant();
        return query.FirstOrDefaultAsync(t => t.Domains.Any(d => d.Domain == normalized), cancellationToken);
    }
}
