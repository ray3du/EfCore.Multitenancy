using EfCore.MultiTenancy.Core.Abstractions;
using EfCore.MultiTenancy.EfCore.Migrations;
using EfCore.MultiTenancy.EfCore.Provisioning;
using Microsoft.AspNetCore.Mvc;
using SampleApi.Models;

namespace SampleApi.Controllers;

/// <summary>
/// Admin/management endpoints. These operate on the public schema only (the tenant
/// registry) and are reachable without a resolved tenant — e.g. from the bare base
/// domain or an "admin" subdomain excluded by <c>ExcludedSubdomains</c>.
/// </summary>
[ApiController]
[Route("api/tenants")]
public class TenantsController : ControllerBase
{
    private readonly ITenantProvisioningService<AppTenant> _provisioningService;
    private readonly ITenantStore<AppTenant> _tenantStore;
    private readonly ITenantMigrator<AppTenant> _migrator;

    public TenantsController(
        ITenantProvisioningService<AppTenant> provisioningService,
        ITenantStore<AppTenant> tenantStore,
        ITenantMigrator<AppTenant> migrator)
    {
        _provisioningService = provisioningService;
        _tenantStore = tenantStore;
        _migrator = migrator;
    }

    public record CreateTenantRequest(string Name, string Domain, string? PlanName);

    [HttpPost]
    public async Task<ActionResult<AppTenant>> Create(CreateTenantRequest request, CancellationToken cancellationToken)
    {
        var tenant = new AppTenant
        {
            Name = request.Name,
            PlanName = request.PlanName ?? "free",
        };

        var created = await _provisioningService.CreateTenantAsync(tenant, request.Domain, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AppTenant>>> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await _tenantStore.GetAllActiveAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AppTenant>> Get(Guid id, CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.FindByIdAsync(id, cancellationToken);
        return tenant is null ? NotFound() : Ok(tenant);
    }

    /// <summary>Applies pending migrations to every active tenant's schema.</summary>
    [HttpPost("migrate-all")]
    public async Task<ActionResult<TenantMigrationSummary<AppTenant>>> MigrateAll(CancellationToken cancellationToken)
    {
        return Ok(await _migrator.MigrateAllTenantsAsync(cancellationToken));
    }
}
