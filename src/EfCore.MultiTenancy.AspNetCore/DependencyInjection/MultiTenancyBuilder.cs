using EfCore.MultiTenancy.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace EfCore.MultiTenancy.AspNetCore.DependencyInjection;

internal sealed class MultiTenancyBuilder<TTenant> : IMultiTenancyBuilder<TTenant> where TTenant : Tenant
{
    public IServiceCollection Services { get; }

    public MultiTenancyBuilder(IServiceCollection services)
    {
        Services = services;
    }
}
