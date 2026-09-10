using EfCore.MultiTenancy.Core.Events;
using SampleApi.Data;
using SampleApi.Models;

namespace SampleApi.Seeding;

/// <summary>
/// Seeds a welcome todo for every new tenant, right after its schema has been
/// migrated. Injecting <see cref="SampleTenantDbContext"/> here works because
/// <c>TenantProvisioningService</c>/<c>TenantMigrator</c> raise this event from a DI
/// scope already bound to the tenant being created — see the remarks on
/// <c>TenantMigrator&lt;TTenant,TDbContext&gt;.MigrateTenantAsync</c>.
/// </summary>
public class SeedTodosOnTenantCreated : ITenantLifecycleHandler<AppTenant>
{
    private readonly SampleTenantDbContext _dbContext;

    public SeedTodosOnTenantCreated(SampleTenantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task OnTenantMigratedAsync(AppTenant tenant, CancellationToken cancellationToken = default)
    {
        _dbContext.Todos.Add(new TodoItem { Title = $"Welcome to {tenant.Name}!" });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
