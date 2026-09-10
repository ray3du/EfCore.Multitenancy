namespace EfCore.MultiTenancy.EfCore.Options;

/// <summary>Configuration for <c>AddMultiTenancy</c>'s PostgreSQL wiring.</summary>
public class PostgresMultiTenancyOptions
{
    /// <summary>
    /// Connection string to the single shared database. In <see cref="TenantIsolationMode.SchemaPerTenant"/>
    /// mode (the default) this is used for both the tenant registry and every
    /// tenant's isolated schema — all requests share one Npgsql connection pool.
    /// In <see cref="TenantIsolationMode.DatabasePerTenant"/> mode this is used only
    /// for the tenant registry (the "public" store); per-tenant connection strings
    /// are derived from <see cref="DatabaseNameTemplate"/>.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    public TenantIsolationMode Mode { get; set; } = TenantIsolationMode.SchemaPerTenant;

    /// <summary>
    /// Database-mode only: a composite-format template (e.g. <c>"tenant_{0}"</c>)
    /// used to derive each tenant's database name from its <c>SchemaName</c>.
    /// </summary>
    public string DatabaseNameTemplate { get; set; } = "tenant_{0}";

    /// <summary>
    /// Assembly name EF Core should look in for <c>TenantStoreDbContext&lt;TTenant&gt;</c>'s
    /// migrations. Because that context type is defined in this library's assembly
    /// rather than your app's, EF Core defaults to expecting migrations there too —
    /// which doesn't work for a NuGet-packaged library. Set this to your app's
    /// assembly name (e.g. <c>typeof(Program).Assembly.GetName().Name</c>) so
    /// <c>dotnet ef migrations add --context TenantStoreDbContext`1</c> generates
    /// files into your app project instead. Leave null to use EF Core's default
    /// (the context's own assembly) if you don't need registry migrations at all.
    /// </summary>
    public string? StoreMigrationsAssembly { get; set; }
}
