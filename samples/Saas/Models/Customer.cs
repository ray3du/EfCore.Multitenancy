namespace Saas.Models;

/// <summary>
/// A self-service customer account, tenant-scoped. Customers register and log in
/// themselves (distinct from staff <see cref="User"/> accounts) and create their own
/// <see cref="Order"/>s.
/// </summary>
public class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public string? Address { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
