using EfCore.MultiTenancy.Core.Models;
using Microsoft.Extensions.Logging;

namespace EfCore.MultiTenancy.Core.Events;

/// <summary>
/// Fans a lifecycle event out to every registered <see cref="ITenantLifecycleHandler{TTenant}"/>.
/// A handler that throws is logged and does not prevent other handlers from running,
/// so one misbehaving seed handler cannot break tenant resolution or migration for
/// every other handler (or, for resolution specifically, the request itself).
/// </summary>
public sealed class TenantLifecycleDispatcher<TTenant> where TTenant : class, ITenant
{
    private readonly IEnumerable<ITenantLifecycleHandler<TTenant>> _handlers;
    private readonly ILogger<TenantLifecycleDispatcher<TTenant>> _logger;

    public TenantLifecycleDispatcher(
        IEnumerable<ITenantLifecycleHandler<TTenant>> handlers,
        ILogger<TenantLifecycleDispatcher<TTenant>> logger)
    {
        _handlers = handlers;
        _logger = logger;
    }

    public Task RaiseTenantCreatedAsync(TTenant tenant, CancellationToken cancellationToken = default) =>
        RunAllAsync(nameof(ITenantLifecycleHandler<TTenant>.OnTenantCreatedAsync),
            h => h.OnTenantCreatedAsync(tenant, cancellationToken));

    public Task RaiseTenantMigratedAsync(TTenant tenant, CancellationToken cancellationToken = default) =>
        RunAllAsync(nameof(ITenantLifecycleHandler<TTenant>.OnTenantMigratedAsync),
            h => h.OnTenantMigratedAsync(tenant, cancellationToken));

    public Task RaiseTenantResolvedAsync(TTenant tenant, CancellationToken cancellationToken = default) =>
        RunAllAsync(nameof(ITenantLifecycleHandler<TTenant>.OnTenantResolvedAsync),
            h => h.OnTenantResolvedAsync(tenant, cancellationToken));

    private async Task RunAllAsync(string eventName, Func<ITenantLifecycleHandler<TTenant>, Task> invoke)
    {
        foreach (var handler in _handlers)
        {
            try
            {
                await invoke(handler).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Tenant lifecycle handler {Handler} threw during {Event}.",
                    handler.GetType().Name, eventName);
            }
        }
    }
}
