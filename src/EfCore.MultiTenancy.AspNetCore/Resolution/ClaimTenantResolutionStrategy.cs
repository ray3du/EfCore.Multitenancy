using EfCore.MultiTenancy.AspNetCore.Options;
using EfCore.MultiTenancy.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace EfCore.MultiTenancy.AspNetCore.Resolution;

/// <summary>
/// Resolves the tenant from a claim (default type "tenant_id") on the authenticated
/// user, e.g. a JWT claim set at token-issuance time. Must run after authentication
/// middleware — register <c>UseAuthentication()</c> before <c>UseTenantResolution()</c>
/// for this strategy to see <c>HttpContext.User</c>. The claim value is parsed as a
/// tenant <see cref="Guid"/> and looked up directly by id.
/// </summary>
public sealed class ClaimTenantResolutionStrategy<TTenant> : ITenantResolutionStrategy<TTenant>
    where TTenant : class, ITenant
{
    private readonly TenantResolutionOptions _options;

    public ClaimTenantResolutionStrategy(IOptions<TenantResolutionOptions> options)
    {
        _options = options.Value;
    }

    public ValueTask<TenantLookupKey?> TryResolveAsync(HttpContext context)
    {
        var claimValue = context.User.FindFirst(_options.TenantClaimType)?.Value;
        if (!string.IsNullOrWhiteSpace(claimValue) && Guid.TryParse(claimValue, out var tenantId))
        {
            return ValueTask.FromResult<TenantLookupKey?>(TenantLookupKey.ForId(tenantId));
        }

        return ValueTask.FromResult<TenantLookupKey?>(null);
    }
}
