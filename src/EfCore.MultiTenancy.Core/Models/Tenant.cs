namespace EfCore.MultiTenancy.Core.Models;

/// <summary>
/// Default tenant registry entity, stored in the shared/public schema.
/// Subclass this to add application-specific fields (plan, billing id, etc.)
/// and flow the subclass through the library's generic type parameters.
/// </summary>
public class Tenant : ITenant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Human-readable tenant name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The PostgreSQL schema name that isolates this tenant's data.
    /// Immutable after creation in practice — changing it after provisioning
    /// does not rename the underlying schema.
    /// </summary>
    public string SchemaName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Domains/subdomains that resolve to this tenant.</summary>
    public ICollection<TenantDomain> Domains { get; set; } = new List<TenantDomain>();
}
