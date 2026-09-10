namespace EfCore.MultiTenancy.Core.Models;

/// <summary>
/// Minimal contract the library needs from a tenant entity. Implemented by
/// <see cref="Tenant"/>; consuming applications may subclass <see cref="Tenant"/>
/// to add custom fields while keeping this contract intact.
/// </summary>
public interface ITenant
{
    /// <summary>Stable tenant identifier.</summary>
    Guid Id { get; }

    /// <summary>
    /// The PostgreSQL schema that stores this tenant's isolated data.
    /// Must be a safe SQL identifier — see <see cref="Validation.SchemaNameValidator"/>.
    /// </summary>
    string SchemaName { get; }

    /// <summary>Whether requests may currently be routed to this tenant.</summary>
    bool IsActive { get; }
}
