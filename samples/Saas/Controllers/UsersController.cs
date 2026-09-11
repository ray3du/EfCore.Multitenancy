using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saas.Auth;
using Saas.Data;
using Saas.Models;

namespace Saas.Controllers;

/// <summary>Staff account management — an admin invites more staff.</summary>
[ApiController]
[Route("api/users")]
[Authorize(Policy = AuthClaims.AdminOnlyPolicy)]
public class UsersController : ControllerBase
{
    private readonly SaasTenantDbContext _dbContext;

    public UsersController(SaasTenantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public record CreateUserRequest(string Email, string Password, string FullName, UserRole Role);

    public record UserResponse(Guid Id, string Email, string FullName, UserRole Role, bool IsActive);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var users = await _dbContext.Users
            .Select(u => new UserResponse(u.Id, u.Email, u.FullName, u.Role, u.IsActive))
            .ToListAsync(cancellationToken);
        return Ok(users);
    }

    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _dbContext.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            return Conflict("A user with this email already exists.");
        }

        var user = new User
        {
            Email = email,
            PasswordHash = PasswordHasher.Hash(request.Password),
            FullName = request.FullName,
            Role = request.Role,
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new UserResponse(user.Id, user.Email, user.FullName, user.Role, user.IsActive));
    }
}
