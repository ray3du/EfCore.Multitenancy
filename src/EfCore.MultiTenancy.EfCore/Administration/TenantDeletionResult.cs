using EfCore.MultiTenancy.Core.Models;

namespace EfCore.MultiTenancy.EfCore.Administration;

/// <summary>
/// Outcome of one <see cref="TenantIdentifier"/> passed to
/// <see cref="ITenantAdministrationService{TTenant}.DeleteTenantsAsync"/>.
/// <see cref="Tenant"/> is null when the identifier matched no tenant at all — that is
/// reported here as a failed result rather than thrown, so one bad identifier in a
/// batch doesn't abort the rest.
/// </summary>
public sealed record TenantDeletionResult<TTenant>(TenantIdentifier Identifier, TTenant? Tenant, bool Succeeded, Exception? Error)
    where TTenant : Tenant;

public sealed record TenantDeletionSummary<TTenant>(IReadOnlyList<TenantDeletionResult<TTenant>> Results)
    where TTenant : Tenant
{
    public int SuccessCount => Results.Count(r => r.Succeeded);
    public int FailureCount => Results.Count(r => !r.Succeeded);
    public bool AllSucceeded => FailureCount == 0;
}
