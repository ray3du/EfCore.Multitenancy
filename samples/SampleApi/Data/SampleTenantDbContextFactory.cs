using EfCore.MultiTenancy.EfCore.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SampleApi.Data;

/// <summary>
/// Used only by `dotnet ef migrations add`/`dotnet ef database update` at design
/// time. The app's normal DI wiring (<c>AddTenantDbContext</c>) builds this
/// context's connection string from the current request's resolved tenant, which
/// does not exist at design time — EF's CLI tooling looks for this factory and uses
/// it instead of trying to resolve the context from the app's service provider.
/// The connection string here only needs to be well-formed (it is never actually
/// opened for `migrations add`); table shape is identical for every tenant, so any
/// placeholder schema works.
/// </summary>
public class SampleTenantDbContextFactory : IDesignTimeDbContextFactory<SampleTenantDbContext>
{
    public SampleTenantDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SampleTenantDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5433;Database=multitenancy_sample;Username=postgres;Password=postgres");
        return new SampleTenantDbContext(optionsBuilder.Options);
    }
}
