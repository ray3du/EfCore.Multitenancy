using EfCore.MultiTenancy.Core.Models;

namespace EfCore.MultiTenancy.EfCore.Administration;

/// <summary>
/// Administrative operations over the tenant registry: create, delete, and list
/// tenants by any of their unique identifiers (schema name, id, or domain) — not
/// restricted to active tenants, unlike <see cref="Core.Abstractions.ITenantStore{TTenant}"/>.
/// Intended for admin tooling (a CLI such as <see cref="TenantAdministrationCommand{TTenant}"/>,
/// a hosted job, an internal admin endpoint) rather than request-time resolution.
/// </summary>
/// <remarks>
/// This interface performs no interactive confirmation and no dry-run preview — every
/// call does exactly what it's asked, immediately. That belongs one layer up, in
/// whatever is presenting the operation to a human (see
/// <see cref="TenantAdministrationCommand{TTenant}"/> for the interactive, confirm-before-delete
/// CLI built on top of this). A service that blocked on <c>Console.ReadLine()</c> would
/// hang forever behind an HTTP request or a hosted job.
/// </remarks>
public interface ITenantAdministrationService<TTenant> where TTenant : Tenant
{
    /// <summary>
    /// Creates every requested tenant. Each is provisioned independently — a later
    /// request failing does not roll back or block earlier successful ones — so check
    /// <see cref="TenantCreationSummary{TTenant}.AllSucceeded"/> and each result's
    /// <c>Error</c> rather than assuming an all-or-nothing outcome.
    /// </summary>
    Task<TenantCreationSummary<TTenant>> CreateTenantsAsync(
        IReadOnlyList<TenantCreationRequest<TTenant>> requests,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every tenant matching one of the given identifiers. For each: the
    /// registry row is removed first, then — unless <paramref name="mode"/> is
    /// <see cref="TenantDeletionMode.KeepSchema"/> — its schema/database is irreversibly
    /// dropped. That order is deliberate: a failure between the two steps then leaves an
    /// orphaned, deregistered schema (inert, safe to clean up later) rather than an
    /// "active" tenant with no backing schema that resolution middleware would still
    /// happily route real requests to. An identifier matching no tenant is reported as a
    /// failed result rather than thrown, so one typo in a batch doesn't abort the rest.
    /// </summary>
    Task<TenantDeletionSummary<TTenant>> DeleteTenantsAsync(
        IReadOnlyList<TenantIdentifier> identifiers,
        TenantDeletionMode mode = TenantDeletionMode.DropSchema,
        CancellationToken cancellationToken = default);

    /// <summary>Looks up a single tenant by any one identifier, active or not. Null if none matches.</summary>
    Task<TTenant?> FindAsync(TenantIdentifier identifier, CancellationToken cancellationToken = default);

    /// <summary>Lists tenants ordered by name. Includes inactive tenants only if requested.</summary>
    Task<IReadOnlyList<TTenant>> ListTenantsAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
}
