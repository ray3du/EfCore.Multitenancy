using EfCore.MultiTenancy.Core.Models;

namespace EfCore.MultiTenancy.Core.Abstractions;

/// <summary>
/// Write side of the scoped tenant context. Deliberately separated from
/// <see cref="ITenantContext{TTenant}"/> so ordinary application code — which should
/// only ever inject the read-only interface — has no way to reassign the current
/// tenant mid-scope. Only the resolution middleware and the background-job tenant
/// scope factory take a dependency on this interface.
/// </summary>
public interface ITenantContextSetter<TTenant> where TTenant : class, ITenant
{
    /// <summary>
    /// Sets the current tenant for this scope. May be called at most once per scope;
    /// a second call throws <see cref="Exceptions.TenantAlreadySetException"/> to make
    /// accidental cross-tenant reuse of a scope impossible to do silently.
    /// </summary>
    void SetTenant(TTenant tenant);
}
