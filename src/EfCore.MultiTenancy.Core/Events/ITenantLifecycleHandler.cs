using EfCore.MultiTenancy.Core.Models;

namespace EfCore.MultiTenancy.Core.Events;

/// <summary>
/// Extensibility hook for reacting to tenant lifecycle events. Register one or more
/// implementations in DI (any scope) and they will all be invoked; a common use is
/// seeding default data in <see cref="OnTenantCreatedAsync"/>. Implement only the
/// methods you need — all have no-op default bodies.
/// </summary>
public interface ITenantLifecycleHandler<TTenant> where TTenant : class, ITenant
{
    /// <summary>
    /// Raised after a new tenant's schema has been provisioned (schema created, but
    /// before migrations run). Good place to seed reference data once migrations
    /// complete via <see cref="OnTenantMigratedAsync"/> instead, unless you need to
    /// run raw SQL before the schema has any tables.
    /// </summary>
    Task OnTenantCreatedAsync(TTenant tenant, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <summary>Raised after EF Core migrations have been applied to a tenant's schema.</summary>
    Task OnTenantMigratedAsync(TTenant tenant, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <summary>
    /// Raised after tenant resolution middleware has determined the current tenant
    /// for a request, before the rest of the pipeline runs.
    /// </summary>
    Task OnTenantResolvedAsync(TTenant tenant, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
