using EfCore.MultiTenancy.Core.Models;

namespace SampleApi.Models;

/// <summary>
/// Demonstrates subclassing <see cref="Tenant"/> to add application-specific fields.
/// </summary>
public class AppTenant : Tenant
{
    public string PlanName { get; set; } = "free";
}
