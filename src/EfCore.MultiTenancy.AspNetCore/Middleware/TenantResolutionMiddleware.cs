using EfCore.MultiTenancy.AspNetCore.Options;
using EfCore.MultiTenancy.AspNetCore.Resolution;
using EfCore.MultiTenancy.Core.Abstractions;
using EfCore.MultiTenancy.Core.Events;
using EfCore.MultiTenancy.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EfCore.MultiTenancy.AspNetCore.Middleware;

/// <summary>
/// Resolves the current tenant for the request and sets it on the scoped
/// <see cref="ITenantContextSetter{TTenant}"/> before the rest of the pipeline runs.
/// Every dependency here is resolved by ASP.NET Core from the current request's DI
/// scope (standard conventional-middleware method injection) — nothing is cached on
/// the middleware instance itself, since one instance is shared across all requests
/// for the lifetime of the app. That is what makes it safe: there is no field on
/// this class that could hold one request's tenant into the next.
/// </summary>
/// <remarks>
/// <see cref="TenantLifecycleDispatcher{TTenant}"/> is deliberately <b>not</b> an
/// <c>InvokeAsync</c> parameter, even though it would look like idiomatic method
/// injection: ASP.NET Core resolves every extra <c>InvokeAsync</c> parameter from DI
/// before the method body runs at all, which would construct the dispatcher — and
/// therefore every registered <see cref="ITenantLifecycleHandler{TTenant}"/>, which
/// may itself depend on a tenant-scoped <c>DbContext</c> — before this method has had
/// a chance to resolve and set the tenant. It is instead pulled from
/// <c>HttpContext.RequestServices</c> after <c>SetTenant</c>, once it's actually safe.
/// </remarks>
public sealed class TenantResolutionMiddleware<TTenant> where TTenant : class, ITenant
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware<TTenant>> _logger;

    public TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware<TTenant>> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IEnumerable<ITenantResolutionStrategy<TTenant>> strategies,
        ITenantStore<TTenant> tenantStore,
        ITenantContextSetter<TTenant> tenantContextSetter,
        IOptions<TenantResolutionOptions> options)
    {
        var cancellationToken = context.RequestAborted;
        TTenant? tenant = null;

        foreach (var strategy in strategies)
        {
            var key = await strategy.TryResolveAsync(context).ConfigureAwait(false);
            if (key is null)
            {
                continue;
            }

            tenant = key.Value.TenantId is { } id
                ? await tenantStore.FindByIdAsync(id, cancellationToken).ConfigureAwait(false)
                : await tenantStore.FindByDomainAsync(key.Value.Domain!, cancellationToken).ConfigureAwait(false);

            break;
        }

        if (tenant is null)
        {
            if (options.Value.RequireResolvedTenant)
            {
                _logger.LogWarning("No active tenant could be resolved for request {Path}", context.Request.Path);
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("Tenant not found.", cancellationToken).ConfigureAwait(false);
                return;
            }

            await _next(context).ConfigureAwait(false);
            return;
        }

        tenantContextSetter.SetTenant(tenant);

        var dispatcher = context.RequestServices.GetRequiredService<TenantLifecycleDispatcher<TTenant>>();
        await dispatcher.RaiseTenantResolvedAsync(tenant, cancellationToken).ConfigureAwait(false);

        await _next(context).ConfigureAwait(false);
    }
}
