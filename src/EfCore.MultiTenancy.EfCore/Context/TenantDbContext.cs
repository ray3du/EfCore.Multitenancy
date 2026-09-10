using EfCore.MultiTenancy.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace EfCore.MultiTenancy.EfCore.Context;

/// <summary>
/// Base class for a tenant's isolated data. Derive your own DbContext from this and
/// add your <c>DbSet</c>s as usual — deliberately do <b>not</b> call
/// <c>HasDefaultSchema</c> or qualify table names with a schema here or in a derived
/// context: table names must stay unqualified so the same compiled model works
/// against every tenant's schema, with PostgreSQL's <c>search_path</c> (set per
/// connection-open by <see cref="TenantSchemaConnectionInterceptor{TTenant}"/>)
/// deciding which physical schema "Orders", "Customers", etc. resolve to.
/// </summary>
/// <remarks>
/// Register the derived context with plain <c>AddDbContext</c> — never
/// <c>AddDbContextPool</c>. Pooling reuses the same DbContext instance (and the
/// interceptor wiring captured when it was first built) across unrelated requests,
/// which is exactly the kind of cross-tenant leak this library exists to prevent.
/// <c>AddMultiTenancy</c> registers it correctly for you.
/// </remarks>
public abstract class TenantDbContext<TTenant> : DbContext where TTenant : class, ITenant
{
    protected TenantDbContext(DbContextOptions options) : base(options)
    {
    }
}
