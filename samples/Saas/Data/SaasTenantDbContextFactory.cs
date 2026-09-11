using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Saas.Data;

/// <summary>
/// Used only by `dotnet ef migrations add`/`dotnet ef database update` at design
/// time. The app's normal DI wiring (<c>AddTenantDbContext</c>) builds this
/// context's connection string from the current request's resolved tenant, which
/// does not exist at design time — EF's CLI tooling uses this factory instead. The
/// connection string only needs to be well-formed (never actually opened for
/// `migrations add`); table shape is identical for every tenant.
/// </summary>
public class SaasTenantDbContextFactory : IDesignTimeDbContextFactory<SaasTenantDbContext>
{
    public SaasTenantDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SaasTenantDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5433;Database=saas_sample;Username=postgres;Password=postgres");
        return new SaasTenantDbContext(optionsBuilder.Options);
    }
}
