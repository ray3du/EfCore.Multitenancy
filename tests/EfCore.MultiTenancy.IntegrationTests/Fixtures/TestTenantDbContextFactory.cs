using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EfCore.MultiTenancy.IntegrationTests.Fixtures;

/// <summary>Design-time factory used only to generate migrations offline; see the sample's equivalent for why this is required.</summary>
public class TestTenantDbContextFactory : IDesignTimeDbContextFactory<TestTenantDbContext>
{
    public TestTenantDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestTenantDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=postgres");
        return new TestTenantDbContext(optionsBuilder.Options);
    }
}
