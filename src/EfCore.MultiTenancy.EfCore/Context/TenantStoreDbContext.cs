using EfCore.MultiTenancy.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace EfCore.MultiTenancy.EfCore.Context;

/// <summary>
/// The tenant registry, always pinned to the "public" schema regardless of the
/// current request's tenant. Because <c>HasDefaultSchema("public")</c> is applied in
/// <see cref="OnModelCreating"/>, EF Core fully qualifies every generated statement
/// with "public." at the SQL level. That means this context's queries are correct
/// even if the pooled physical connection happens to carry a stale
/// <c>search_path</c> left over from a different tenant: unlike
/// <c>TenantDbContext&lt;TTenant&gt;</c>, this context never depends on search_path.
/// </summary>
public class TenantStoreDbContext<TTenant> : DbContext where TTenant : Tenant
{
    public const string SchemaName = "public";

    public TenantStoreDbContext(DbContextOptions<TenantStoreDbContext<TTenant>> options) : base(options)
    {
    }

    public DbSet<TTenant> Tenants => Set<TTenant>();

    public DbSet<TenantDomain> TenantDomains => Set<TenantDomain>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<TTenant>(builder =>
        {
            builder.ToTable("tenants");
            builder.HasKey(t => t.Id);
            builder.Property(t => t.SchemaName).IsRequired().HasMaxLength(63);
            builder.HasIndex(t => t.SchemaName).IsUnique();
            builder.Property(t => t.Name).IsRequired().HasMaxLength(256);
            builder.HasMany(t => t.Domains)
                .WithOne()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TenantDomain>(builder =>
        {
            builder.ToTable("tenant_domains");
            builder.HasKey(d => d.Id);
            builder.Property(d => d.Domain).IsRequired().HasMaxLength(256);
            builder.HasIndex(d => d.Domain).IsUnique();
        });

        base.OnModelCreating(modelBuilder);
    }
}
