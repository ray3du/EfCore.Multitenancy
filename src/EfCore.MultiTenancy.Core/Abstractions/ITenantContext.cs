using EfCore.MultiTenancy.Core.Models;

namespace EfCore.MultiTenancy.Core.Abstractions;

/// <summary>
/// Read-only view of "who is the current tenant" for the current DI scope
/// (one per HTTP request, or one per explicitly created background-job scope).
/// Register and inject this — never <see cref="ITenantContextSetter{TTenant}"/> —
/// from application code; only the resolution middleware and background-scope
/// helpers should be able to mutate it.
/// </summary>
public interface ITenantContext<TTenant> where TTenant : class, ITenant
{
    /// <summary>The resolved tenant for this scope, or null if none has been resolved yet.</summary>
    TTenant? Current { get; }

    /// <summary>True once a tenant has been resolved for this scope.</summary>
    bool HasTenant { get; }

    /// <summary>
    /// Returns the current tenant, or throws <see cref="Exceptions.TenantContextException"/>
    /// if none has been resolved. Use this in code paths that must never run outside a
    /// tenant scope (e.g. tenant-schema repositories).
    /// </summary>
    TTenant Require();
}
