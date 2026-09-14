using EfCore.MultiTenancy.Core.Abstractions;
using EfCore.MultiTenancy.Core.Events;
using EfCore.MultiTenancy.Core.Models;
using EfCore.MultiTenancy.EfCore.Administration;
using EfCore.MultiTenancy.EfCore.Context;
using EfCore.MultiTenancy.EfCore.Options;
using EfCore.MultiTenancy.EfCore.Provisioning;
using EfCore.MultiTenancy.EfCore.Store;
using EfCore.MultiTenancy.AspNetCore.Administration;
using EfCore.MultiTenancy.AspNetCore.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EfCore.MultiTenancy.AspNetCore.DependencyInjection;

public static class MultiTenancyServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core multi-tenancy services: the scoped tenant context, the
    /// tenant store/registry (always the "public" schema), provisioning, and the
    /// PostgreSQL connection-string plumbing. Chain <c>.AddTenantDbContext&lt;TDbContext&gt;()</c>
    /// for your own tenant-scoped context and a resolution strategy
    /// (e.g. <c>.UseSubdomainResolution()</c>) before calling
    /// <c>app.UseTenantResolution&lt;TTenant&gt;()</c> in <c>Program.cs</c>.
    /// </summary>
    /// <remarks>
    /// Also registers <see cref="TenantAdministrationHostedService{TTenant}"/>, which
    /// makes <c>create_tenant</c>/<c>delete_tenant</c>/<c>list_tenants</c> available from
    /// the command line automatically — no further wiring needed. That's the reason for
    /// the <c>new()</c> constraint here (tighter than most of this library's other
    /// <c>TTenant</c>-generic methods): it needs to construct a bare
    /// <typeparamref name="TTenant"/> from a parsed <c>create_tenant --tenant "Name|Domain"</c> spec.
    /// </remarks>
    public static IMultiTenancyBuilder<TTenant> AddMultiTenancy<TTenant>(
        this IServiceCollection services,
        Action<PostgresMultiTenancyOptions> configureDatabase,
        Action<TenantResolutionOptions>? configureResolution = null)
        where TTenant : Tenant, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureDatabase);

        services.AddOptions<PostgresMultiTenancyOptions>().Configure(configureDatabase);
        services.AddOptions<TenantResolutionOptions>().Configure(configureResolution ?? (_ => { }));

        // A single scoped instance backs both the read-only ITenantContext and the
        // write-only ITenantContextSetter, so every service in the scope agrees on
        // "the" current tenant — see TenantContext<TTenant> for why this is split
        // into two interfaces instead of one mutable one.
        services.AddScoped<TenantContext<TTenant>>();
        services.AddScoped<ITenantContext<TTenant>>(sp => sp.GetRequiredService<TenantContext<TTenant>>());
        services.AddScoped<ITenantContextSetter<TTenant>>(sp => sp.GetRequiredService<TenantContext<TTenant>>());

        // Singleton, deliberately: its one dependency (IServiceScopeFactory) is
        // itself always root-resolvable, and its entire purpose is to hand a *new*
        // tenant-bound scope to code that does not have one yet — a hosted service's
        // StartAsync, a console/CLI migration tool, a timer callback. Registering it
        // Scoped would make it resolvable only from inside a scope that, by
        // definition, doesn't need it to create one.
        services.AddSingleton<ITenantScopeFactory<TTenant>, TenantScopeFactory<TTenant>>();
        services.AddScoped<TenantLifecycleDispatcher<TTenant>>();

        services.AddScoped<ITenantStore<TTenant>, TenantStore<TTenant>>();
        services.AddScoped<ITenantConnectionStringProvider<TTenant>, DefaultTenantConnectionStringProvider<TTenant>>();
        services.AddScoped<ITenantSchemaProvisioner<TTenant>, PostgresTenantSchemaProvisioner<TTenant>>();
        services.AddScoped<ITenantProvisioningService<TTenant>, TenantProvisioningService<TTenant>>();
        services.AddScoped<ITenantAdministrationService<TTenant>, TenantAdministrationService<TTenant>>();
        services.AddHostedService<TenantAdministrationHostedService<TTenant>>();

        // Resolved and wired in explicitly via options.AddInterceptors(...) inside
        // AddTenantDbContext below — EF Core has no automatic discovery of
        // interceptors registered in the application's DI container, so this
        // registration exists only so a fresh, scope-bound instance can be resolved
        // there. IDbConnectionInterceptor is not one of EF's "singleton" interceptor
        // categories (see the interceptors doc's table), so a new instance per scope
        // here is the supported pattern, not a "many service providers" foot-gun.
        services.AddScoped<TenantSchemaConnectionInterceptor<TTenant>>();

        services.AddDbContext<TenantStoreDbContext<TTenant>>((sp, options) =>
        {
            var dbOptions = sp.GetRequiredService<IOptions<PostgresMultiTenancyOptions>>().Value;
            options.UseNpgsql(dbOptions.ConnectionString, npgsql =>
            {
                if (!string.IsNullOrWhiteSpace(dbOptions.StoreMigrationsAssembly))
                {
                    npgsql.MigrationsAssembly(dbOptions.StoreMigrationsAssembly);
                }
            });
        });

        return new MultiTenancyBuilder<TTenant>(services);
    }
}
