namespace Saas.Models;

/// <summary>A sellable item, tenant-scoped, managed by staff.</summary>
public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
