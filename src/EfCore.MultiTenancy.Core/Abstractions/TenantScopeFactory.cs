using EfCore.MultiTenancy.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace EfCore.MultiTenancy.Core.Abstractions;

public sealed class TenantScopeFactory<TTenant> : ITenantScopeFactory<TTenant> where TTenant : class, ITenant
{
    private readonly IServiceScopeFactory _scopeFactory;

    public TenantScopeFactory(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public IServiceScope CreateScope(TTenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        var scope = _scopeFactory.CreateScope();
        try
        {
            var setter = scope.ServiceProvider.GetRequiredService<ITenantContextSetter<TTenant>>();
            setter.SetTenant(tenant);
            return scope;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
