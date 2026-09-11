namespace Saas.Models;

public enum UserRole
{
    Admin,
    Staff,
}

/// <summary>
/// A staff/admin account, tenant-scoped (lives in the company's own schema). The
/// first row is created during company signup with <see cref="UserRole.Admin"/>.
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Staff;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
