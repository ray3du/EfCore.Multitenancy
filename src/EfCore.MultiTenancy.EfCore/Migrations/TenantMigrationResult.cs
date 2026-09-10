using EfCore.MultiTenancy.Core.Models;

namespace EfCore.MultiTenancy.EfCore.Migrations;

public sealed record TenantMigrationResult<TTenant>(TTenant Tenant, bool Succeeded, Exception? Error)
    where TTenant : class, ITenant;

public sealed record TenantMigrationSummary<TTenant>(IReadOnlyList<TenantMigrationResult<TTenant>> Results)
    where TTenant : class, ITenant
{
    public int SuccessCount => Results.Count(r => r.Succeeded);
    public int FailureCount => Results.Count(r => !r.Succeeded);
    public bool AllSucceeded => FailureCount == 0;
}
