namespace Saas.Models;

public enum OrderStatus
{
    Pending,
    Invoiced,
    Cancelled,
}

/// <summary>An order a customer places for themselves, tenant-scoped.</summary>
public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public DateTimeOffset OrderDate { get; set; } = DateTimeOffset.UtcNow;

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    /// <summary>Set once this order has been grouped into an invoice.</summary>
    public Guid? InvoiceId { get; set; }

    public Invoice? Invoice { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
