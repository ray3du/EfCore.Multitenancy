using Microsoft.EntityFrameworkCore;
using Saas.Data;
using Saas.Models;

namespace Saas.Services;

public class InvalidInvoiceRequestException : Exception
{
    public InvalidInvoiceRequestException(string message) : base(message)
    {
    }
}

/// <summary>Groups a customer's pending orders into a new invoice.</summary>
public class InvoiceService
{
    private readonly SaasTenantDbContext _dbContext;

    public InvoiceService(SaasTenantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Invoice> CreateInvoiceAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
        {
            throw new InvalidInvoiceRequestException("At least one order id is required.");
        }

        var orders = await _dbContext.Orders
            .Include(o => o.Items)
            .Where(o => orderIds.Contains(o.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (orders.Count != orderIds.Count)
        {
            throw new InvalidInvoiceRequestException("One or more orders were not found.");
        }

        if (orders.Select(o => o.CustomerId).Distinct().Count() != 1)
        {
            throw new InvalidInvoiceRequestException("All orders on an invoice must belong to the same customer.");
        }

        if (orders.Any(o => o.InvoiceId is not null))
        {
            throw new InvalidInvoiceRequestException("One or more orders are already invoiced.");
        }

        if (orders.Any(o => o.Status == OrderStatus.Cancelled))
        {
            throw new InvalidInvoiceRequestException("Cancelled orders cannot be invoiced.");
        }

        var total = orders.SelectMany(o => o.Items).Sum(i => i.Quantity * i.UnitPrice);
        var invoice = new Invoice
        {
            InvoiceNumber = await NextInvoiceNumberAsync(cancellationToken).ConfigureAwait(false),
            CustomerId = orders[0].CustomerId,
            TotalAmount = total,
        };

        foreach (var order in orders)
        {
            order.Status = OrderStatus.Invoiced;
            order.Invoice = invoice;
        }

        _dbContext.Invoices.Add(invoice);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return invoice;
    }

    private async Task<string> NextInvoiceNumberAsync(CancellationToken cancellationToken)
    {
        var count = await _dbContext.Invoices.CountAsync(cancellationToken).ConfigureAwait(false);
        return $"INV-{count + 1:D6}";
    }
}
