using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saas.Auth;
using Saas.Data;

namespace Saas.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize]
public class CustomersController : ControllerBase
{
    private readonly SaasTenantDbContext _dbContext;

    public CustomersController(SaasTenantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Staff-only: every customer of this tenant.</summary>
    [HttpGet]
    [Authorize(Policy = AuthClaims.StaffOnlyPolicy)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await _dbContext.Customers.ToListAsync(cancellationToken));
    }

    /// <summary>Customer-only: the authenticated customer's own profile.</summary>
    [HttpGet("me")]
    [Authorize(Policy = AuthClaims.CustomerOnlyPolicy)]
    public async Task<IActionResult> GetMe(CancellationToken cancellationToken)
    {
        var customer = await _dbContext.Customers.FindAsync([User.GetActorId()], cancellationToken);
        return customer is null ? NotFound() : Ok(customer);
    }
}
