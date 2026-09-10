using EfCore.MultiTenancy.Core.Exceptions;
using EfCore.MultiTenancy.Core.Models;

namespace EfCore.MultiTenancy.Core.Abstractions;

/// <summary>
/// Default scoped implementation shared by <see cref="ITenantContext{TTenant}"/> and
/// <see cref="ITenantContextSetter{TTenant}"/>. Registered once per DI scope (see
/// <c>AddMultiTenancy</c>), so every service resolved within the same HTTP request
/// (or background-job scope) sees the same instance and therefore the same tenant —
/// there is no static or thread-static state anywhere in this type.
/// </summary>
public sealed class TenantContext<TTenant> : ITenantContext<TTenant>, ITenantContextSetter<TTenant>
    where TTenant : class, ITenant
{
    private TTenant? _current;

    public TTenant? Current => _current;

    public bool HasTenant => _current is not null;

    public TTenant Require()
    {
        return _current ?? throw new TenantContextException(
            $"No tenant has been resolved for the current scope. Ensure tenant resolution " +
            $"middleware ran, or that a background job explicitly created a tenant scope, " +
            $"before resolving a service that depends on {nameof(ITenantContext<TTenant>)}<{typeof(TTenant).Name}>.");
    }

    public void SetTenant(TTenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        if (_current is not null)
        {
            throw new TenantAlreadySetException();
        }

        _current = tenant;
    }
}
