using EfCore.MultiTenancy.EfCore.Context;
using Microsoft.EntityFrameworkCore;
using SampleApi.Models;

namespace SampleApi.Data;

/// <summary>
/// The application's tenant-scoped data. Note there is no <c>HasDefaultSchema</c>
/// call and no schema passed to <c>ToTable</c> — table names stay unqualified so the
/// same compiled model and the same migrations run correctly against every tenant's
/// schema, with search_path (set per-request by the library) deciding which one.
/// </summary>
public class SampleTenantDbContext : TenantDbContext<AppTenant>
{
    public SampleTenantDbContext(DbContextOptions<SampleTenantDbContext> options) : base(options)
    {
    }

    public DbSet<TodoItem> Todos => Set<TodoItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TodoItem>(builder =>
        {
            builder.ToTable("todos");
            builder.HasKey(t => t.Id);
            builder.Property(t => t.Title).IsRequired().HasMaxLength(512);
        });

        base.OnModelCreating(modelBuilder);
    }
}
