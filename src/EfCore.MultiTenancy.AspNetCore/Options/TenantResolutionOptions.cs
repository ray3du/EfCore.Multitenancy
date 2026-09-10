namespace EfCore.MultiTenancy.AspNetCore.Options;

public class TenantResolutionOptions
{
    /// <summary>
    /// The root domain requests are subdomains of, e.g. <c>"myapp.com"</c> so that
    /// <c>acme.myapp.com</c> resolves the subdomain <c>"acme"</c>. Required for the
    /// built-in subdomain strategy.
    /// </summary>
    public string? BaseDomain { get; set; }

    /// <summary>Subdomains that never resolve to a tenant (e.g. "www", "api", "admin").</summary>
    public HashSet<string> ExcludedSubdomains { get; set; } = new(StringComparer.OrdinalIgnoreCase) { "www" };

    /// <summary>Header name used by the built-in header-based strategy.</summary>
    public string TenantHeaderName { get; set; } = "X-Tenant";

    /// <summary>Claim type used by the built-in claim-based (JWT) strategy.</summary>
    public string TenantClaimType { get; set; } = "tenant_id";

    /// <summary>
    /// When true (default), a request that no strategy can resolve to an active
    /// tenant fails with 404 before reaching the rest of the pipeline. Set to false
    /// for apps that serve some routes (e.g. marketing pages, the signup flow itself)
    /// without a tenant in scope.
    /// </summary>
    public bool RequireResolvedTenant { get; set; } = true;
}
