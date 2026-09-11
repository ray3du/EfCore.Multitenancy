using EfCore.MultiTenancy.EfCore.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Saas.Models;

namespace Saas.Data;

/// <summary>Design-time factory for the tenant registry context. See <see cref="SaasTenantDbContextFactory"/>.</summary>
public class TenantStoreDbContextFactory : IDesignTimeDbContextFactory<TenantStoreDbContext<Company>>
{
    public TenantStoreDbContext<Company> CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TenantStoreDbContext<Company>>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5433;Database=saas_sample;Username=postgres;Password=postgres",
            npgsql => npgsql.MigrationsAssembly(typeof(TenantStoreDbContextFactory).Assembly.GetName().Name));
        return new TenantStoreDbContext<Company>(optionsBuilder.Options);
    }
}
