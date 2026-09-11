using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saas.Auth;
using Saas.Data;

namespace Saas.Controllers;

/// <summary>Staff login, on the company's own subdomain (e.g. acme.localhost).</summary>
[ApiController]
[Route("api/auth/staff")]
public class StaffAuthController : ControllerBase
{
    private readonly SaasTenantDbContext _dbContext;
    private readonly JwtTokenService _tokenService;

    public StaffAuthController(SaasTenantDbContext dbContext, JwtTokenService tokenService)
    {
        _dbContext = dbContext;
        _tokenService = tokenService;
    }

    public record LoginRequest(string Email, string Password);

    public record LoginResponse(string Token);

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null || !user.IsActive || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized();
        }

        return Ok(new LoginResponse(_tokenService.CreateStaffToken(user)));
    }
}
