namespace EfCore.MultiTenancy.EfCore.Administration;

public enum TenantDeletionMode
{
    /// <summary>Removes the registry row and irreversibly drops the tenant's schema/database.</summary>
    DropSchema,

    /// <summary>Removes the registry row only; the physical schema/database is left in place.</summary>
    KeepSchema,
}
