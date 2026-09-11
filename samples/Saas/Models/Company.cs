using EfCore.MultiTenancy.Core.Models;

namespace Saas.Models;

/// <summary>
/// The tenant registry entity, stored in the public schema. Represents the company
/// that owns a subdomain; everything else in the app (users, customers, products,
/// orders, invoices) lives in that company's own tenant schema.
/// </summary>
public class Company : Tenant
{
    public string Address { get; set; } = string.Empty;
}
