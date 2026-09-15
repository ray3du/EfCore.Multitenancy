using EfCore.MultiTenancy.Core.Models;

namespace EfCore.MultiTenancy.EfCore.Administration;

/// <summary>
/// One tenant to create: an already-constructed <typeparamref name="TTenant"/> (set
/// <c>Name</c> and any application-specific fields yourself; leave <c>SchemaName</c>
/// empty to have it derived from <c>Name</c>) plus the domain to register as primary.
/// Mirrors <see cref="Provisioning.ITenantProvisioningService{TTenant}.CreateTenantAsync"/>'s
/// parameters — administration never constructs <typeparamref name="TTenant"/> itself,
/// since only the caller knows how to populate a subclass's own fields.
/// </summary>
public sealed record TenantCreationRequest<TTenant>(TTenant Tenant, string Domain)
    where TTenant : Tenant;
