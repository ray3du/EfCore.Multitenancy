using EfCore.MultiTenancy.AspNetCore.Administration;
using EfCore.MultiTenancy.Core.Models;
using EfCore.MultiTenancy.EfCore.Administration;
using Microsoft.Extensions.DependencyInjection;

namespace EfCore.MultiTenancy.UnitTests;

/// <summary>
/// <see cref="TenantAdministrationHostedService{TTenant}"/> is what makes tenant
/// administration available automatically once an app calls
/// <c>AddMultiTenancy&lt;TTenant&gt;()</c> — these tests use the injectable
/// <c>getArgs</c>/<c>exit</c> overrides specifically so they never touch the real
/// process's command line or actually call <see cref="Environment.Exit"/>, which would
/// kill the test process.
/// </summary>
public class TenantAdministrationHostedServiceTests
{
    private sealed class FakeTenantAdministrationService : ITenantAdministrationService<Tenant>
    {
        public Task<TenantCreationSummary<Tenant>> CreateTenantsAsync(
            IReadOnlyList<TenantCreationRequest<Tenant>> requests, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TenantDeletionSummary<Tenant>> DeleteTenantsAsync(
            IReadOnlyList<TenantIdentifier> identifiers,
            TenantDeletionMode mode = TenantDeletionMode.DropSchema,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Tenant?> FindAsync(TenantIdentifier identifier, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Tenant>> ListTenantsAsync(bool includeInactive = false, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Tenant>>(Array.Empty<Tenant>());
    }

    private static IServiceProvider MakeServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantAdministrationService<Tenant>>(new FakeTenantAdministrationService());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task StartAsync_ArgsMatchAVerb_RunsTheCommandAndExitsWithItsCode()
    {
        var exitCalls = new List<int>();
        var hostedService = new TenantAdministrationHostedService<Tenant>(
            MakeServices(),
            getArgs: () => new[] { "list_tenants" },
            exit: exitCalls.Add);

        await hostedService.StartAsync(CancellationToken.None);

        Assert.Equal(new[] { 0 }, exitCalls);
    }

    [Fact]
    public async Task StartAsync_ArgsDoNotMatchAVerb_DoesNotExit_SoTheHostCanStartNormally()
    {
        var hostedService = new TenantAdministrationHostedService<Tenant>(
            MakeServices(),
            getArgs: () => Array.Empty<string>(),
            exit: _ => throw new InvalidOperationException("must not exit when no command was requested"));

        // Must return normally rather than throw — this is exactly the path a real
        // `dotnet run` with no CLI verb (i.e. "start the web app") takes.
        await hostedService.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_UnrelatedArgs_DoesNotExit()
    {
        var hostedService = new TenantAdministrationHostedService<Tenant>(
            MakeServices(),
            getArgs: () => new[] { "--urls", "http://localhost:5000" },
            exit: _ => throw new InvalidOperationException("must not exit for non-admin args"));

        await hostedService.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_CompletesWithoutError()
    {
        var hostedService = new TenantAdministrationHostedService<Tenant>(
            MakeServices(), () => Array.Empty<string>(), _ => { });

        await hostedService.StopAsync(CancellationToken.None);
    }
}
