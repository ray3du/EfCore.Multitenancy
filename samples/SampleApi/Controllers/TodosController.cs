using EfCore.MultiTenancy.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SampleApi.Data;
using SampleApi.Models;

namespace SampleApi.Controllers;

/// <summary>
/// Tenant-scoped endpoints. Reachable only once tenant resolution middleware has
/// identified a tenant (by subdomain, in this sample) — <see cref="SampleTenantDbContext"/>
/// transparently reads and writes only that tenant's schema.
/// </summary>
[ApiController]
[Route("api/todos")]
public class TodosController : ControllerBase
{
    private readonly SampleTenantDbContext _dbContext;
    private readonly ITenantContext<AppTenant> _tenantContext;

    public TodosController(SampleTenantDbContext dbContext, ITenantContext<AppTenant> tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public record CreateTodoRequest(string Title);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TodoItem>>> GetAll(CancellationToken cancellationToken)
    {
        var todos = await _dbContext.Todos.AsNoTracking().OrderBy(t => t.CreatedAt).ToListAsync(cancellationToken);
        return Ok(new { tenant = _tenantContext.Require().Name, todos });
    }

    [HttpPost]
    public async Task<ActionResult<TodoItem>> Create(CreateTodoRequest request, CancellationToken cancellationToken)
    {
        var todo = new TodoItem { Title = request.Title };
        _dbContext.Todos.Add(todo);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetAll), todo);
    }
}
