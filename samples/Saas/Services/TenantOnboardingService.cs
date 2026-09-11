using EfCore.MultiTenancy.Core.Abstractions;
using EfCore.MultiTenancy.Core.Validation;
using EfCore.MultiTenancy.EfCore.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Saas.Auth;
using Saas.Data;
using Saas.Models;

namespace Saas.Services;

/// <summary>
/// Onboards a new company: provisions its tenant schema and creates its first
/// (admin) staff user. Runs outside any tenant's request scope — the whole point of
/// this service is to create one — so it opens its own tenant-bound scope for the
/// second half of the work via <see cref="ITenantScopeFactory{TTenant}"/>, exactly as
/// documented as the supported pattern for tenant-scoped work off the request path.
/// </summary>
public class TenantOnboardingService
{
    private readonly ITenantProvisioningService<Company> _provisioningService;
    private readonly ITenantStore<Company> _tenantStore;
    private readonly ITenantScopeFactory<Company> _tenantScopeFactory;

    public TenantOnboardingService(
        ITenantProvisioningService<Company> provisioningService,
        ITenantStore<Company> tenantStore,
        ITenantScopeFactory<Company> tenantScopeFactory)
    {
        _provisioningService = provisioningService;
        _tenantStore = tenantStore;
        _tenantScopeFactory = tenantScopeFactory;
    }

    public async Task<Company> OnboardAsync(
        string subdomain,
        string companyName,
        string address,
        string adminEmail,
        string adminPassword,
        string adminFullName,
        CancellationToken cancellationToken = default)
    {
        var normalizedSubdomain = subdomain.Trim().ToLowerInvariant();

        // Checked up front so a duplicate subdomain fails as a clean 409 instead of
        // an unhandled Postgres unique-violation 500. The provisioning call below is
        // still wrapped against the same race (two concurrent signups for the same
        // subdomain both passing this check) rather than relying on this alone.
        if (await _tenantStore.FindByDomainAsync(normalizedSubdomain, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new SubdomainAlreadyTakenException(normalizedSubdomain);
        }

        // The schema name is derived from the subdomain — the identifier the user
        // actually chose to be unique — rather than the company display name, which
        // different tenants could easily share (e.g. two companies both called
        // "Acme"). SchemaNameValidator.Normalize throws InvalidSchemaNameException
        // for a subdomain that can't become a valid schema name.
        var company = new Company
        {
            Name = companyName,
            Address = address,
            SchemaName = SchemaNameValidator.Normalize(normalizedSubdomain),
        };

        Company created;
        try
        {
            created = await _provisioningService.CreateTenantAsync(company, normalizedSubdomain, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new SubdomainAlreadyTakenException(normalizedSubdomain);
        }

        using var scope = _tenantScopeFactory.CreateScope(created);
        var dbContext = scope.ServiceProvider.GetRequiredService<SaasTenantDbContext>();
        dbContext.Users.Add(new User
        {
            Email = adminEmail.Trim().ToLowerInvariant(),
            PasswordHash = PasswordHasher.Hash(adminPassword),
            FullName = adminFullName,
            Role = UserRole.Admin,
        });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return created;
    }
}
