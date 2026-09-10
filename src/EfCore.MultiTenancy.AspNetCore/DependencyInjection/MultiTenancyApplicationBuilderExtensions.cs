using EfCore.MultiTenancy.AspNetCore.Middleware;
using EfCore.MultiTenancy.Core.Models;
using Microsoft.AspNetCore.Builder;

namespace EfCore.MultiTenancy.AspNetCore.DependencyInjection;

public static class MultiTenancyApplicationBuilderExtensions
{
    /// <summary>
    /// Adds <see cref="TenantResolutionMiddleware{TTenant}"/> to the pipeline. Place
    /// this after <c>UseRouting()</c>/<c>UseAuthentication()</c> if any registered
    /// resolution strategy needs route values or <c>HttpContext.User</c> (the claim
    /// strategy does), and before any middleware or endpoint that depends on
    /// <c>ITenantContext&lt;TTenant&gt;</c>.
    /// </summary>
    public static IApplicationBuilder UseTenantResolution<TTenant>(this IApplicationBuilder app)
        where TTenant : Tenant
    {
        return app.UseMiddleware<TenantResolutionMiddleware<TTenant>>();
    }
}
