using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saas.Auth;
using Saas.Data;
using Saas.Models;
using Saas.Services;

namespace Saas.Controllers;

[ApiController]
[Route("api/invoices")]
[Authorize]
public class InvoicesController : ControllerBase
{
    private readonly SaasTenantDbContext _dbContext;
    private readonly InvoiceService _invoiceService;

    public InvoicesController(SaasTenantDbContext dbContext, InvoiceService invoiceService)
    {
        _dbContext = dbContext;
        _invoiceService = invoiceService;
    }

    public record CreateInvoiceRequest(List<Guid> OrderIds);

    /// <summary>Staff-only: groups one or more of a customer's orders into a new invoice.</summary>
    [HttpPost]
    [Authorize(Policy = AuthClaims.StaffOnlyPolicy)]
    public async Task<ActionResult<Invoice>> Create(CreateInvoiceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var invoice = await _invoiceService.CreateInvoiceAsync(request.OrderIds, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = invoice.Id }, invoice);
        }
        catch (InvalidInvoiceRequestException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Staff-only: every invoice for this tenant.</summary>
    [HttpGet]
    [Authorize(Policy = AuthClaims.StaffOnlyPolicy)]
    public async Task<ActionResult<IReadOnlyList<Invoice>>> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await _dbContext.Invoices.Include(i => i.Orders).ToListAsync(cancellationToken));
    }

    /// <summary>Customer-only: the authenticated customer's own invoices.</summary>
    [HttpGet("mine")]
    [Authorize(Policy = AuthClaims.CustomerOnlyPolicy)]
    public async Task<ActionResult<IReadOnlyList<Invoice>>> GetMine(CancellationToken cancellationToken)
    {
        var customerId = User.GetActorId();
        var invoices = await _dbContext.Invoices
            .Include(i => i.Orders)
            .Where(i => i.CustomerId == customerId)
            .ToListAsync(cancellationToken);
        return Ok(invoices);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthClaims.StaffOnlyPolicy)]
    public async Task<ActionResult<Invoice>> Get(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await _dbContext.Invoices.Include(i => i.Orders).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        return invoice is null ? NotFound() : Ok(invoice);
    }
}
