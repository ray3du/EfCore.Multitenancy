namespace EfCore.MultiTenancy.AspNetCore.Resolution;

/// <summary>
/// What a resolution strategy extracted from the request, to be looked up against
/// the tenant store. Exactly one of <see cref="Domain"/> or <see cref="TenantId"/>
/// is set — domain-shaped strategies (subdomain, header) use the former; identity-shaped
/// strategies (a JWT tenant-id claim) use the latter.
/// </summary>
public readonly record struct TenantLookupKey
{
    public string? Domain { get; private init; }

    public Guid? TenantId { get; private init; }

    public static TenantLookupKey ForDomain(string domain) => new() { Domain = domain };

    public static TenantLookupKey ForId(Guid tenantId) => new() { TenantId = tenantId };
}
