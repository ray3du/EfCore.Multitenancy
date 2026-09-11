using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saas.Auth;
using Saas.Data;
using Saas.Models;

namespace Saas.Controllers;

/// <summary>Customer self-service registration and login, on the company's subdomain.</summary>
[ApiController]
[Route("api/auth/customers")]
public class CustomerAuthController : ControllerBase
{
    private readonly SaasTenantDbContext _dbContext;
    private readonly JwtTokenService _tokenService;

    public CustomerAuthController(SaasTenantDbContext dbContext, JwtTokenService tokenService)
    {
        _dbContext = dbContext;
        _tokenService = tokenService;
    }

    public record RegisterRequest(string Email, string Password, string Name, string? Phone, string? Address);

    public record LoginRequest(string Email, string Password);

    public record AuthResponse(string Token);

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _dbContext.Customers.AnyAsync(c => c.Email == email, cancellationToken))
        {
            return Conflict("A customer with this email is already registered.");
        }

        var customer = new Customer
        {
            Email = email,
            PasswordHash = PasswordHasher.Hash(request.Password),
            Name = request.Name,
            Phone = request.Phone,
            Address = request.Address,
        };
        _dbContext.Customers.Add(customer);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new AuthResponse(_tokenService.CreateCustomerToken(customer)));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var customer = await _dbContext.Customers.SingleOrDefaultAsync(c => c.Email == email, cancellationToken);
        if (customer is null || !PasswordHasher.Verify(request.Password, customer.PasswordHash))
        {
            return Unauthorized();
        }

        return Ok(new AuthResponse(_tokenService.CreateCustomerToken(customer)));
    }
}
