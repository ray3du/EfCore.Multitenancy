using EfCore.MultiTenancy.EfCore.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SampleApi.Models;

namespace SampleApi.Data;

/// <summary>Design-time factory for the tenant registry context. See <see cref="SampleTenantDbContextFactory"/>.</summary>
public class TenantStoreDbContextFactory : IDesignTimeDbContextFactory<TenantStoreDbContext<AppTenant>>
{
    public TenantStoreDbContext<AppTenant> CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TenantStoreDbContext<AppTenant>>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5433;Database=multitenancy_sample;Username=postgres;Password=postgres",
            npgsql => npgsql.MigrationsAssembly(typeof(TenantStoreDbContextFactory).Assembly.GetName().Name));
        return new TenantStoreDbContext<AppTenant>(optionsBuilder.Options);
    }
}
