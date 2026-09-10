using EfCore.MultiTenancy.AspNetCore.Options;
using EfCore.MultiTenancy.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace EfCore.MultiTenancy.AspNetCore.Resolution;

/// <summary>
/// Resolves the tenant from a request header (default "X-Tenant"), matched against
/// registered tenant domains. Useful for server-to-server callers and local
/// development where subdomain routing is inconvenient.
/// </summary>
public sealed class HeaderTenantResolutionStrategy<TTenant> : ITenantResolutionStrategy<TTenant>
    where TTenant : class, ITenant
{
    private readonly TenantResolutionOptions _options;

    public HeaderTenantResolutionStrategy(IOptions<TenantResolutionOptions> options)
    {
        _options = options.Value;
    }

    public ValueTask<TenantLookupKey?> TryResolveAsync(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(_options.TenantHeaderName, out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            return ValueTask.FromResult<TenantLookupKey?>(
                TenantLookupKey.ForDomain(value.ToString().Trim().ToLowerInvariant()));
        }

        return ValueTask.FromResult<TenantLookupKey?>(null);
    }
}
