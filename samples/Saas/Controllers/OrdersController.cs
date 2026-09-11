using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saas.Auth;
using Saas.Data;
using Saas.Models;

namespace Saas.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly SaasTenantDbContext _dbContext;

    public OrdersController(SaasTenantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public record OrderItemRequest(Guid ProductId, int Quantity);

    public record CreateOrderRequest(List<OrderItemRequest> Items);

    /// <summary>Customer-only: places an order for the authenticated customer.</summary>
    [HttpPost]
    [Authorize(Policy = AuthClaims.CustomerOnlyPolicy)]
    public async Task<ActionResult<Order>> Create(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
        {
            return BadRequest("An order must have at least one item.");
        }

        var productIds = request.Items.Select(i => i.ProductId).ToList();
        var products = await _dbContext.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        if (products.Count != productIds.Distinct().Count())
        {
            return BadRequest("One or more products were not found.");
        }

        var order = new Order { CustomerId = User.GetActorId() };
        foreach (var item in request.Items)
        {
            var product = products[item.ProductId];
            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                Quantity = item.Quantity,
                UnitPrice = product.Price,
            });
        }

        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    /// <summary>Staff see every order; a customer sees only their own.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Order>>> GetAll(CancellationToken cancellationToken)
    {
        var query = _dbContext.Orders.Include(o => o.Items).AsQueryable();
        if (!User.IsStaff())
        {
            var customerId = User.GetActorId();
            query = query.Where(o => o.CustomerId == customerId);
        }

        return Ok(await query.ToListAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Order>> Get(Guid id, CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (!User.IsStaff() && order.CustomerId != User.GetActorId())
        {
            return Forbid();
        }

        return Ok(order);
    }
}
