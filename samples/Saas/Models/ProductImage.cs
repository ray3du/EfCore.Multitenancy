namespace Saas.Models;

/// <summary>One image in a product's gallery, tenant-scoped.</summary>
public class ProductImage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProductId { get; set; }

    public string Url { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
