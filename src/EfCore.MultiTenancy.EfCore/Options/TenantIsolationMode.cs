namespace EfCore.MultiTenancy.EfCore.Options;

public enum TenantIsolationMode
{
    /// <summary>
    /// One PostgreSQL database shared by all tenants; each tenant gets its own schema
    /// within it, selected per-connection via <c>search_path</c>. The primary,
    /// fully-tested mode this library is built around.
    /// </summary>
    SchemaPerTenant,

    /// <summary>
    /// Each tenant gets an entirely separate PostgreSQL database. Isolation is
    /// provided by Postgres itself rather than search_path. Supported as a secondary
    /// path built on the same connection-string-provider extensibility point; it sees
    /// far less exercise than schema mode, so validate it thoroughly for your
    /// workload before relying on it in production.
    /// </summary>
    DatabasePerTenant,
}
