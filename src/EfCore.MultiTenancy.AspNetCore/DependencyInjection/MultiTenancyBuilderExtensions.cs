using EfCore.MultiTenancy.AspNetCore.Resolution;
using EfCore.MultiTenancy.Core.Abstractions;
using EfCore.MultiTenancy.Core.Events;
using EfCore.MultiTenancy.Core.Models;
using EfCore.MultiTenancy.EfCore.Context;
using EfCore.MultiTenancy.EfCore.Migrations;
using EfCore.MultiTenancy.EfCore.Options;
using EfCore.MultiTenancy.EfCore.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EfCore.MultiTenancy.AspNetCore.DependencyInjection;

public static class MultiTenancyBuilderExtensions
{
    /// <summary>
    /// Registers your own <typeparamref name="TDbContext"/> (deriving from
    /// <see cref="TenantDbContext{TTenant}"/>) as the tenant-scoped data context, and
    /// the corresponding <see cref="ITenantMigrator{TTenant}"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses plain scoped-service semantics via <c>AddDbContext</c> — deliberately
    /// <b>not</b> <c>AddDbContextPool</c>. The
    /// connection string (and therefore, in schema mode, the search_path set by the
    /// registered interceptor) is resolved fresh from the scope's current tenant
    /// every time a new context instance is constructed; pooling would reuse an
    /// instance built for one tenant's scope on a later request for another.
    /// </para>
    /// <para>
    /// EF Core's CLI tooling (<c>dotnet ef migrations add</c>, <c>dbcontext list</c>,
    /// etc.) builds the application's host to discover <c>DbContext</c> types outside
    /// of any request, where no tenant is ever resolved — so the options callback
    /// below checks <see cref="EF.IsDesignTime"/> and uses a placeholder connection
    /// string in that case instead of calling <c>ITenantContext.Require()</c>, which
    /// would otherwise throw and abort tooling entirely. The placeholder is never
    /// opened for <c>migrations add</c>, since that only needs the model, not a live
    /// connection; add an <c>IDesignTimeDbContextFactory&lt;TDbContext&gt;</c> in your
    /// app pointing at a real reachable database for <c>database update</c>/scaffolding.
    /// </para>
    /// <para>
    /// In schema mode, the migrations history table is explicitly pinned to the
    /// current tenant's schema via <c>MigrationsHistoryTable</c>. This is not
    /// optional: Npgsql's <c>HistoryRepository</c> checks whether that table exists
    /// with a query hardcoded to the "public" schema rather than honoring
    /// search_path (confirmed against the provider's own behavior — search_path is
    /// deliberately not consulted here), so without this every tenant's migration
    /// would see the tenant *registry's* own <c>public.__EFMigrationsHistory</c> row,
    /// conclude a same-named migration was already applied, and silently skip
    /// creating that tenant's tables — a real bug caught only by an end-to-end test
    /// against a live database, not by any unit test against the generated SQL.
    /// </para>
    /// </remarks>
    public static IMultiTenancyBuilder<TTenant> AddTenantDbContext<TTenant, TDbContext>(
        this IMultiTenancyBuilder<TTenant> builder)
        where TTenant : Tenant
        where TDbContext : TenantDbContext<TTenant>
    {
        builder.Services.AddDbContext<TDbContext>((sp, options) =>
        {
            if (EF.IsDesignTime)
            {
                options.UseNpgsql("Host=localhost;Database=postgres");
                return;
            }

            var connectionStringProvider = sp.GetRequiredService<ITenantConnectionStringProvider<TTenant>>();
            var tenantContext = sp.GetRequiredService<ITenantContext<TTenant>>();
            var tenant = tenantContext.Require();
            var connectionString = connectionStringProvider.GetConnectionString(tenant);
            var isolationMode = sp.GetRequiredService<IOptions<PostgresMultiTenancyOptions>>().Value.Mode;

            options.UseNpgsql(connectionString, npgsql =>
            {
                if (isolationMode == TenantIsolationMode.SchemaPerTenant)
                {
                    npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, tenant.SchemaName);
                }
            });
            options.UseApplicationServiceProvider(sp);

            // EF Core has no automatic discovery of interceptors registered in the
            // app's DI container — it must be resolved and passed to AddInterceptors
            // explicitly. A fresh instance per DbContext is correct here (not a
            // "many internal service providers" foot-gun): IDbConnectionInterceptor is
            // not one of EF's singleton-category interceptors, so a new instance bound
            // to this scope's tenant is exactly the supported, documented pattern.
            options.AddInterceptors(sp.GetRequiredService<TenantSchemaConnectionInterceptor<TTenant>>());
        });

        builder.Services.AddScoped<ITenantMigrator<TTenant>, TenantMigrator<TTenant, TDbContext>>();

        return builder;
    }

    /// <summary>Adds subdomain-based tenant resolution (configure <c>BaseDomain</c> via <c>AddMultiTenancy</c>).</summary>
    public static IMultiTenancyBuilder<TTenant> UseSubdomainResolution<TTenant>(this IMultiTenancyBuilder<TTenant> builder)
        where TTenant : Tenant
    {
        builder.Services.AddScoped<ITenantResolutionStrategy<TTenant>, SubdomainTenantResolutionStrategy<TTenant>>();
        return builder;
    }

    /// <summary>Adds header-based tenant resolution (default header "X-Tenant").</summary>
    public static IMultiTenancyBuilder<TTenant> UseHeaderResolution<TTenant>(this IMultiTenancyBuilder<TTenant> builder)
        where TTenant : Tenant
    {
        builder.Services.AddScoped<ITenantResolutionStrategy<TTenant>, HeaderTenantResolutionStrategy<TTenant>>();
        return builder;
    }

    /// <summary>Adds claim-based tenant resolution from the authenticated user (default claim "tenant_id").</summary>
    public static IMultiTenancyBuilder<TTenant> UseClaimResolution<TTenant>(this IMultiTenancyBuilder<TTenant> builder)
        where TTenant : Tenant
    {
        builder.Services.AddScoped<ITenantResolutionStrategy<TTenant>, ClaimTenantResolutionStrategy<TTenant>>();
        return builder;
    }

    /// <summary>Registers a custom resolution strategy, tried in the order strategies were added.</summary>
    public static IMultiTenancyBuilder<TTenant> AddResolutionStrategy<TTenant, TStrategy>(this IMultiTenancyBuilder<TTenant> builder)
        where TTenant : Tenant
        where TStrategy : class, ITenantResolutionStrategy<TTenant>
    {
        builder.Services.AddScoped<ITenantResolutionStrategy<TTenant>, TStrategy>();
        return builder;
    }

    /// <summary>Registers a lifecycle handler (see <see cref="ITenantLifecycleHandler{TTenant}"/>).</summary>
    public static IMultiTenancyBuilder<TTenant> AddTenantLifecycleHandler<TTenant, THandler>(this IMultiTenancyBuilder<TTenant> builder)
        where TTenant : Tenant
        where THandler : class, ITenantLifecycleHandler<TTenant>
    {
        builder.Services.AddScoped<ITenantLifecycleHandler<TTenant>, THandler>();
        return builder;
    }
}
