using EfCore.MultiTenancy.Core.Models;

namespace EfCore.MultiTenancy.EfCore.Administration;

/// <summary>
/// Outcome of one <see cref="TenantCreationRequest{TTenant}"/>. <see cref="Tenant"/> is
/// always the request's tenant instance — even on failure — since creation always has
/// a caller-supplied starting point, unlike a deletion lookup that can fail to resolve
/// one at all.
/// </summary>
public sealed record TenantCreationResult<TTenant>(TTenant Tenant, string Domain, bool Succeeded, Exception? Error)
    where TTenant : Tenant;

public sealed record TenantCreationSummary<TTenant>(IReadOnlyList<TenantCreationResult<TTenant>> Results)
    where TTenant : Tenant
{
    public int SuccessCount => Results.Count(r => r.Succeeded);
    public int FailureCount => Results.Count(r => !r.Succeeded);
    public bool AllSucceeded => FailureCount == 0;
}
