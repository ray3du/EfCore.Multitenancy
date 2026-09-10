namespace EfCore.MultiTenancy.Core.Exceptions;

/// <summary>
/// Thrown when code that requires a resolved tenant runs without one (e.g. a
/// <c>TenantDbContext</c> is used outside a request where tenant resolution ran,
/// or a background job forgets to set an explicit tenant scope).
/// </summary>
public class TenantContextException : Exception
{
    public TenantContextException(string message) : base(message)
    {
    }
}

/// <summary>
/// Thrown when code attempts to set the current tenant on an
/// <see cref="Abstractions.ITenantContext{TTenant}"/> that has already had one set.
/// The current tenant is meant to be established once per scope (per request, or per
/// explicit background-job scope) and never mutated afterward.
/// </summary>
public class TenantAlreadySetException : Exception
{
    public TenantAlreadySetException()
        : base("The current tenant has already been set for this scope and cannot be changed. " +
               "Create a new DI scope to switch tenants.")
    {
    }
}
