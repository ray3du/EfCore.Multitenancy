namespace EfCore.MultiTenancy.EfCore.Administration;

/// <summary>
/// Identifies a tenant for an administrative operation by exactly one of its unique
/// fields. Unlike <see cref="Core.Abstractions.ITenantStore{TTenant}"/>, lookups built
/// from this are not restricted to active tenants — administration must be able to
/// target a deactivated or partially-provisioned tenant too.
/// </summary>
public readonly record struct TenantIdentifier
{
    public Guid? Id { get; private init; }

    public string? SchemaName { get; private init; }

    public string? Domain { get; private init; }

    public static TenantIdentifier ForId(Guid id) => new() { Id = id };

    public static TenantIdentifier ForSchemaName(string schemaName) => new() { SchemaName = schemaName };

    public static TenantIdentifier ForDomain(string domain) => new() { Domain = domain };

    public override string ToString() =>
        Id is { } id ? $"id '{id}'" :
        SchemaName is { } schema ? $"schema '{schema}'" :
        Domain is { } domain ? $"domain '{domain}'" :
        "(unset)";
}
