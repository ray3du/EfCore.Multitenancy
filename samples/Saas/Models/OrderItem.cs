namespace Saas.Models;

/// <summary>
/// A line item on an <see cref="Order"/>. <see cref="UnitPrice"/> is captured from
/// the product at order time, so later changes to <see cref="Product.Price"/> don't
/// retroactively change existing orders or invoices.
/// </summary>
public class OrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrderId { get; set; }

    public Guid ProductId { get; set; }

    public Product? Product { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}
