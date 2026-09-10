using EfCore.MultiTenancy.Core.Models;
using Microsoft.AspNetCore.Http;

namespace EfCore.MultiTenancy.AspNetCore.Resolution;

/// <summary>
/// Extracts a tenant identifier from an incoming request. Register one or more via
/// <c>AddMultiTenancy</c>; the resolution middleware tries them in registration
/// order and uses the first non-null result. Implementations should be fast and
/// side-effect free — they run on every request before routing has necessarily
/// finished.
/// </summary>
public interface ITenantResolutionStrategy<TTenant> where TTenant : class, ITenant
{
    /// <summary>Returns a lookup key if this strategy found one in the request, otherwise null.</summary>
    ValueTask<TenantLookupKey?> TryResolveAsync(HttpContext context);
}
