using EfCore.MultiTenancy.Core.Models;
using EfCore.MultiTenancy.EfCore.Context;
using Microsoft.EntityFrameworkCore;

namespace EfCore.MultiTenancy.IntegrationTests.Fixtures;

/// <summary>
/// Deliberately has no HasDefaultSchema/ToTable-with-schema call — table names stay
/// unqualified, exactly like a real consumer's tenant-scoped context, so migrations
/// run through this context exercise the same search_path-dependent behavior being
/// tested.
/// </summary>
public class TestTenantDbContext : TenantDbContext<Tenant>
{
    public TestTenantDbContext(DbContextOptions<TestTenantDbContext> options) : base(options)
    {
    }

    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Widget>(builder =>
        {
            builder.ToTable("widgets");
            builder.HasKey(w => w.Id);
            builder.Property(w => w.Name).IsRequired().HasMaxLength(256);
        });

        base.OnModelCreating(modelBuilder);
    }
}
