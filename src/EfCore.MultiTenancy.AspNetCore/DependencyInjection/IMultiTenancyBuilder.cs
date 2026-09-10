using EfCore.MultiTenancy.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace EfCore.MultiTenancy.AspNetCore.DependencyInjection;

/// <summary>Fluent continuation returned by <c>AddMultiTenancy</c> for chaining further configuration.</summary>
public interface IMultiTenancyBuilder<TTenant> where TTenant : Tenant
{
    IServiceCollection Services { get; }
}
