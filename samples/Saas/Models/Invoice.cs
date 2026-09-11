namespace Saas.Models;

public enum InvoiceStatus
{
    Issued,
    Paid,
}

/// <summary>
/// Groups one or more of a customer's <see cref="Order"/>s for billing, tenant-scoped.
/// </summary>
public class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string InvoiceNumber { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public DateTimeOffset IssuedDate { get; set; } = DateTimeOffset.UtcNow;

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Issued;

    public decimal TotalAmount { get; set; }

    public ICollection<Order> Orders { get; set; } = new List<Order>();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
