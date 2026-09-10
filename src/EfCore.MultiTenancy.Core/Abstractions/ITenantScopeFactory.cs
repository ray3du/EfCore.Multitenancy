using EfCore.MultiTenancy.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace EfCore.MultiTenancy.Core.Abstractions;

/// <summary>
/// Creates a new DI scope with the current tenant already set, for code that runs
/// outside an HTTP request — background jobs, hosted services, CLI/migration tools.
/// This is the supported way to establish "current tenant" when there is no
/// resolution middleware in the pipeline; never set a tenant by any other means
/// outside a request.
/// </summary>
public interface ITenantScopeFactory<TTenant> where TTenant : class, ITenant
{
    /// <summary>
    /// Creates a new <see cref="IServiceScope"/> whose <see cref="ITenantContext{TTenant}"/>
    /// already resolves to <paramref name="tenant"/>. Dispose the returned scope when done —
    /// each call creates an independent scope, so concurrent calls for different tenants
    /// never share state.
    /// </summary>
    IServiceScope CreateScope(TTenant tenant);
}
