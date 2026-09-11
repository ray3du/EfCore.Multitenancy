using EfCore.MultiTenancy.Core.Exceptions;
using EfCore.MultiTenancy.EfCore.Migrations;
using Microsoft.AspNetCore.Mvc;
using Saas.Models;
using Saas.Services;

namespace Saas.Controllers;

/// <summary>
/// Company signup and admin/management endpoints. These are reachable with no
/// tenant resolved — called from the bare base domain, either before a company's
/// subdomain exists (signup) or because they act across every tenant (migrate-all).
/// </summary>
[ApiController]
[Route("api/companies")]
public class CompaniesController : ControllerBase
{
    private readonly TenantOnboardingService _onboardingService;
    private readonly ITenantMigrator<Company> _migrator;

    public CompaniesController(TenantOnboardingService onboardingService, ITenantMigrator<Company> migrator)
    {
        _onboardingService = onboardingService;
        _migrator = migrator;
    }

    public record SignupRequest(
        string Subdomain,
        string CompanyName,
        string Address,
        string AdminEmail,
        string AdminPassword,
        string AdminFullName);

    public record SignupResponse(Guid Id, string CompanyName, string Address, string Subdomain);

    [HttpPost("signup")]
    public async Task<ActionResult<SignupResponse>> Signup(SignupRequest request, CancellationToken cancellationToken)
    {
        Company company;
        try
        {
            company = await _onboardingService.OnboardAsync(
                request.Subdomain,
                request.CompanyName,
                request.Address,
                request.AdminEmail,
                request.AdminPassword,
                request.AdminFullName,
                cancellationToken);
        }
        catch (SubdomainAlreadyTakenException ex)
        {
            return Conflict(ex.Message);
        }
        catch (InvalidSchemaNameException ex)
        {
            return BadRequest(ex.Message);
        }

        var response = new SignupResponse(company.Id, company.Name, company.Address, request.Subdomain);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>Applies pending migrations (e.g. a newly added table) to every active tenant's schema.</summary>
    [HttpPost("migrate-all")]
    public async Task<ActionResult<TenantMigrationSummary<Company>>> MigrateAll(CancellationToken cancellationToken)
    {
        return Ok(await _migrator.MigrateAllTenantsAsync(cancellationToken));
    }
}
