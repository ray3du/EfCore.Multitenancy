using EfCore.MultiTenancy.AspNetCore.Options;
using EfCore.MultiTenancy.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace EfCore.MultiTenancy.AspNetCore.Resolution;

/// <summary>
/// Resolves the tenant from the subdomain of the request host, e.g. "acme" from
/// "acme.myapp.com" when <see cref="TenantResolutionOptions.BaseDomain"/> is
/// "myapp.com". Returns null (falls through to the next strategy) for the bare base
/// domain, for hosts that aren't under the base domain at all, and for excluded
/// subdomains such as "www".
/// </summary>
public sealed class SubdomainTenantResolutionStrategy<TTenant> : ITenantResolutionStrategy<TTenant>
    where TTenant : class, ITenant
{
    private readonly TenantResolutionOptions _options;

    public SubdomainTenantResolutionStrategy(IOptions<TenantResolutionOptions> options)
    {
        _options = options.Value;
    }

    public ValueTask<TenantLookupKey?> TryResolveAsync(HttpContext context)
    {
        var baseDomain = _options.BaseDomain;
        if (string.IsNullOrWhiteSpace(baseDomain))
        {
            return ValueTask.FromResult<TenantLookupKey?>(null);
        }

        var host = context.Request.Host.Host;
        if (string.IsNullOrEmpty(host) ||
            !host.EndsWith("." + baseDomain, StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult<TenantLookupKey?>(null);
        }

        var subdomain = host[..^(baseDomain.Length + 1)];
        if (subdomain.Length == 0 ||
            subdomain.Contains('.', StringComparison.Ordinal) ||
            _options.ExcludedSubdomains.Contains(subdomain))
        {
            return ValueTask.FromResult<TenantLookupKey?>(null);
        }

        return ValueTask.FromResult<TenantLookupKey?>(TenantLookupKey.ForDomain(subdomain.ToLowerInvariant()));
    }
}
