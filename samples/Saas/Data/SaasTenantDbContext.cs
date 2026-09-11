using EfCore.MultiTenancy.EfCore.Context;
using Microsoft.EntityFrameworkCore;
using Saas.Models;

namespace Saas.Data;

/// <summary>
/// The application's tenant-scoped data. No <c>HasDefaultSchema</c> call and no
/// schema passed to <c>ToTable</c> — table names stay unqualified so the same
/// compiled model and migrations run correctly against every tenant's schema, with
/// search_path (set per-request by the library) deciding which one.
/// </summary>
public class SaasTenantDbContext : TenantDbContext<Company>
{
    public SaasTenantDbContext(DbContextOptions<SaasTenantDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(builder =>
        {
            builder.ToTable("users");
            builder.HasKey(u => u.Id);
            builder.Property(u => u.Email).IsRequired().HasMaxLength(256);
            builder.HasIndex(u => u.Email).IsUnique();
            builder.Property(u => u.PasswordHash).IsRequired();
            builder.Property(u => u.FullName).IsRequired().HasMaxLength(256);
        });

        modelBuilder.Entity<Customer>(builder =>
        {
            builder.ToTable("customers");
            builder.HasKey(c => c.Id);
            builder.Property(c => c.Email).IsRequired().HasMaxLength(256);
            builder.HasIndex(c => c.Email).IsUnique();
            builder.Property(c => c.PasswordHash).IsRequired();
            builder.Property(c => c.Name).IsRequired().HasMaxLength(256);
        });

        modelBuilder.Entity<Product>(builder =>
        {
            builder.ToTable("products");
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Name).IsRequired().HasMaxLength(256);
            builder.Property(p => p.Sku).IsRequired().HasMaxLength(64);
            builder.HasIndex(p => p.Sku).IsUnique();
            builder.Property(p => p.Price).HasPrecision(18, 2);
        });

        modelBuilder.Entity<ProductImage>(builder =>
        {
            builder.ToTable("product_images");
            builder.HasKey(i => i.Id);
            builder.Property(i => i.Url).IsRequired().HasMaxLength(2048);
            builder.HasOne<Product>()
                .WithMany(p => p.Images)
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Order>(builder =>
        {
            builder.ToTable("orders");
            builder.HasKey(o => o.Id);
            builder.HasOne(o => o.Customer)
                .WithMany()
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(o => o.Invoice)
                .WithMany(i => i.Orders)
                .HasForeignKey(o => o.InvoiceId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OrderItem>(builder =>
        {
            builder.ToTable("order_items");
            builder.HasKey(i => i.Id);
            builder.Property(i => i.UnitPrice).HasPrecision(18, 2);
            builder.HasOne(i => i.Product)
                .WithMany()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.HasOne<Order>()
                .WithMany(o => o.Items)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Invoice>(builder =>
        {
            builder.ToTable("invoices");
            builder.HasKey(i => i.Id);
            builder.Property(i => i.InvoiceNumber).IsRequired().HasMaxLength(32);
            builder.HasIndex(i => i.InvoiceNumber).IsUnique();
            builder.Property(i => i.TotalAmount).HasPrecision(18, 2);
            builder.HasOne(i => i.Customer)
                .WithMany()
                .HasForeignKey(i => i.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        base.OnModelCreating(modelBuilder);
    }
}
