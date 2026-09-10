namespace EfCore.MultiTenancy.Core.Models;

/// <summary>
/// A single domain or subdomain (e.g. "acme.myapp.com" or "acme" when matching
/// by subdomain segment) that resolves to a tenant. A tenant may have several,
/// e.g. a subdomain plus one or more custom/vanity domains.
/// </summary>
public class TenantDomain
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>
    /// The domain value to match against the request. Stored lower-case;
    /// comparisons are case-insensitive.
    /// </summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>Marks the canonical domain for the tenant (e.g. for generating links).</summary>
    public bool IsPrimary { get; set; }
}
