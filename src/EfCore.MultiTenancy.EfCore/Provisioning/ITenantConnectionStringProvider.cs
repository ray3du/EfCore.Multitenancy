using EfCore.MultiTenancy.Core.Models;

namespace EfCore.MultiTenancy.EfCore.Provisioning;

/// <summary>
/// Resolves the Npgsql connection string a tenant-scoped <c>DbContext</c> should use.
/// In schema mode this is the same string for every tenant (isolation happens via
/// search_path); in database mode it swaps in a per-tenant database name.
/// </summary>
public interface ITenantConnectionStringProvider<TTenant> where TTenant : class, ITenant
{
    string GetConnectionString(TTenant tenant);
}
