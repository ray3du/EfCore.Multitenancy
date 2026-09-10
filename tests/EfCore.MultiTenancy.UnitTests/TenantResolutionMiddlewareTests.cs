using EfCore.MultiTenancy.AspNetCore.Middleware;
using EfCore.MultiTenancy.AspNetCore.Options;
using EfCore.MultiTenancy.AspNetCore.Resolution;
using EfCore.MultiTenancy.Core.Abstractions;
using EfCore.MultiTenancy.Core.Events;
using EfCore.MultiTenancy.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EfCore.MultiTenancy.UnitTests;

/// <summary>
/// Exercises TenantResolutionMiddleware against a real (if minimal) DI container
/// rather than hand-rolled fakes, because the one real bug found in this middleware
/// during development (see MultiTenancyBuilderExtensions' remarks on
/// TenantLifecycleDispatcher) was a DI wiring problem — a lifecycle handler
/// depending on a tenant-scoped service being constructed *before* the tenant was
/// set, because it was resolved as an InvokeAsync method parameter instead of after
/// SetTenant. A hand-rolled fake dispatcher would not have caught that.
/// </summary>
public class TenantResolutionMiddlewareTests
{
    private class FakeTenantStore : ITenantStore<Tenant>
    {
        private readonly Dictionary<string, Tenant> _byDomain;

        public FakeTenantStore(params Tenant[] tenants)
        {
            _byDomain = tenants
                .SelectMany(t => t.Domains.Select(d => (d.Domain, Tenant: t)))
                .ToDictionary(x => x.Domain, x => x.Tenant);
        }

        public Task<Tenant?> FindByDomainAsync(string domain, CancellationToken cancellationToken = default) =>
            Task.FromResult(_byDomain.GetValueOrDefault(domain));

        public Task<Tenant?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_byDomain.Values.FirstOrDefault(t => t.Id == tenantId));

        public Task<IReadOnlyList<Tenant>> GetAllActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Tenant>>(_byDomain.Values.Distinct().ToList());
    }

    private class FixedDomainStrategy : ITenantResolutionStrategy<Tenant>
    {
        private readonly string? _domain;
        public FixedDomainStrategy(string? domain) => _domain = domain;

        public ValueTask<TenantLookupKey?> TryResolveAsync(HttpContext context) =>
            ValueTask.FromResult(_domain is null ? null : (TenantLookupKey?)TenantLookupKey.ForDomain(_domain));
    }

    /// <summary>A lifecycle handler that requires a tenant already be set when constructed —
    /// standing in for "a seed handler that injects a tenant-scoped DbContext".</summary>
    private class HandlerRequiringResolvedTenant : ITenantLifecycleHandler<Tenant>
    {
        public bool ConstructedSuccessfully { get; }

        public HandlerRequiringResolvedTenant(ITenantContext<Tenant> tenantContext)
        {
            // Throws if no tenant has been set yet in this scope — mirrors a real
            // seed handler's tenant-scoped DbContext constructor.
            tenantContext.Require();
            ConstructedSuccessfully = true;
        }
    }

    private static Tenant MakeTenant(string domain)
    {
        var tenant = new Tenant { Name = domain, SchemaName = domain, Id = Guid.NewGuid() };
        tenant.Domains.Add(new TenantDomain { TenantId = tenant.Id, Domain = domain, IsPrimary = true });
        return tenant;
    }

    private static ServiceProvider BuildServiceProvider(
        Tenant[] tenants,
        string? resolvableDomain,
        bool requireResolvedTenant,
        bool includeHandlerRequiringResolvedTenant = false)
    {
        var services = new ServiceCollection();

        services.AddScoped<TenantContext<Tenant>>();
        services.AddScoped<ITenantContext<Tenant>>(sp => sp.GetRequiredService<TenantContext<Tenant>>());
        services.AddScoped<ITenantContextSetter<Tenant>>(sp => sp.GetRequiredService<TenantContext<Tenant>>());
        services.AddScoped<TenantLifecycleDispatcher<Tenant>>();
        services.AddSingleton<ITenantStore<Tenant>>(new FakeTenantStore(tenants));
        services.AddScoped<ITenantResolutionStrategy<Tenant>>(_ => new FixedDomainStrategy(resolvableDomain));
        services.AddSingleton(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(Options.Create(new TenantResolutionOptions { RequireResolvedTenant = requireResolvedTenant }));

        if (includeHandlerRequiringResolvedTenant)
        {
            services.AddScoped<ITenantLifecycleHandler<Tenant>, HandlerRequiringResolvedTenant>();
        }

        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public async Task InvokeAsync_ResolvesTenant_AndCallsNext()
    {
        var tenant = MakeTenant("acme");
        var provider = BuildServiceProvider(new[] { tenant }, resolvableDomain: "acme", requireResolvedTenant: true);
        using var scope = provider.CreateScope();

        var nextCalled = false;
        var middleware = new TenantResolutionMiddleware<Tenant>(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<TenantResolutionMiddleware<Tenant>>.Instance);

        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        await middleware.InvokeAsync(
            httpContext,
            scope.ServiceProvider.GetServices<ITenantResolutionStrategy<Tenant>>(),
            scope.ServiceProvider.GetRequiredService<ITenantStore<Tenant>>(),
            scope.ServiceProvider.GetRequiredService<ITenantContextSetter<Tenant>>(),
            scope.ServiceProvider.GetRequiredService<IOptions<TenantResolutionOptions>>());

        Assert.True(nextCalled);
        Assert.Same(tenant, scope.ServiceProvider.GetRequiredService<ITenantContext<Tenant>>().Current);
        Assert.Equal(200, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_NoTenantResolved_RequireTrue_Returns404AndDoesNotCallNext()
    {
        var provider = BuildServiceProvider(Array.Empty<Tenant>(), resolvableDomain: null, requireResolvedTenant: true);
        using var scope = provider.CreateScope();

        var nextCalled = false;
        var middleware = new TenantResolutionMiddleware<Tenant>(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<TenantResolutionMiddleware<Tenant>>.Instance);

        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        httpContext.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(
            httpContext,
            scope.ServiceProvider.GetServices<ITenantResolutionStrategy<Tenant>>(),
            scope.ServiceProvider.GetRequiredService<ITenantStore<Tenant>>(),
            scope.ServiceProvider.GetRequiredService<ITenantContextSetter<Tenant>>(),
            scope.ServiceProvider.GetRequiredService<IOptions<TenantResolutionOptions>>());

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_NoTenantResolved_RequireFalse_CallsNextWithoutSettingTenant()
    {
        var provider = BuildServiceProvider(Array.Empty<Tenant>(), resolvableDomain: null, requireResolvedTenant: false);
        using var scope = provider.CreateScope();

        var nextCalled = false;
        var middleware = new TenantResolutionMiddleware<Tenant>(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<TenantResolutionMiddleware<Tenant>>.Instance);

        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        await middleware.InvokeAsync(
            httpContext,
            scope.ServiceProvider.GetServices<ITenantResolutionStrategy<Tenant>>(),
            scope.ServiceProvider.GetRequiredService<ITenantStore<Tenant>>(),
            scope.ServiceProvider.GetRequiredService<ITenantContextSetter<Tenant>>(),
            scope.ServiceProvider.GetRequiredService<IOptions<TenantResolutionOptions>>());

        Assert.True(nextCalled);
        Assert.False(scope.ServiceProvider.GetRequiredService<ITenantContext<Tenant>>().HasTenant);
    }

    [Fact]
    public async Task InvokeAsync_DoesNotEagerlyConstructLifecycleHandlers_BeforeTenantIsSet()
    {
        // Regression test: TenantLifecycleDispatcher must not be an InvokeAsync
        // method parameter, or ASP.NET Core would construct it (and therefore every
        // ITenantLifecycleHandler, including ones needing a resolved tenant) before
        // the method body runs at all — before SetTenant has been called.
        var tenant = MakeTenant("acme");
        var provider = BuildServiceProvider(
            new[] { tenant },
            resolvableDomain: "acme",
            requireResolvedTenant: true,
            includeHandlerRequiringResolvedTenant: true);
        using var scope = provider.CreateScope();

        var middleware = new TenantResolutionMiddleware<Tenant>(
            _ => Task.CompletedTask,
            NullLogger<TenantResolutionMiddleware<Tenant>>.Instance);

        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        // Must not throw TenantContextException from HandlerRequiringResolvedTenant's constructor.
        await middleware.InvokeAsync(
            httpContext,
            scope.ServiceProvider.GetServices<ITenantResolutionStrategy<Tenant>>(),
            scope.ServiceProvider.GetRequiredService<ITenantStore<Tenant>>(),
            scope.ServiceProvider.GetRequiredService<ITenantContextSetter<Tenant>>(),
            scope.ServiceProvider.GetRequiredService<IOptions<TenantResolutionOptions>>());

        var handler = (HandlerRequiringResolvedTenant)scope.ServiceProvider
            .GetRequiredService<ITenantLifecycleHandler<Tenant>>();
        Assert.True(handler.ConstructedSuccessfully);
    }
}
