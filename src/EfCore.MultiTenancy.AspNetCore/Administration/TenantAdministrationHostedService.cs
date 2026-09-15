using EfCore.MultiTenancy.Core.Models;
using EfCore.MultiTenancy.EfCore.Administration;
using Microsoft.Extensions.Hosting;

namespace EfCore.MultiTenancy.AspNetCore.Administration;

/// <summary>
/// Makes tenant administration (<c>create_tenant</c>, <c>delete_tenant</c>,
/// <c>list_tenants</c>) available automatically to any app that calls
/// <c>AddMultiTenancy&lt;TTenant&gt;()</c> — no Program.cs changes required.
/// </summary>
/// <remarks>
/// Registered by <c>AddMultiTenancy</c> as a hosted service, which the generic host
/// starts, in registration order, before it starts any hosted service the framework
/// itself adds while building a <c>WebApplication</c> (Kestrel's included) — so a
/// recognized command line runs to completion and exits the process before the web
/// server ever binds a port. Process exit is deliberate here, not just returning
/// normally: returning would let the host go on to start its remaining hosted
/// services regardless, which must never happen once a command has already run.
/// </remarks>
public sealed class TenantAdministrationHostedService<TTenant> : IHostedService
    where TTenant : Tenant, new()
{
    private readonly IServiceProvider _services;
    private readonly Func<string[]> _getArgs;
    private readonly Action<int> _exit;

    public TenantAdministrationHostedService(IServiceProvider services)
        : this(services, () => Environment.GetCommandLineArgs().Skip(1).ToArray(), Environment.Exit)
    {
    }

    /// <summary>
    /// Full constructor, primarily for tests: overriding <paramref name="getArgs"/> and
    /// <paramref name="exit"/> avoids depending on the real process's command line or
    /// actually terminating the process.
    /// </summary>
    public TenantAdministrationHostedService(IServiceProvider services, Func<string[]> getArgs, Action<int> exit)
    {
        _services = services;
        _getArgs = getArgs;
        _exit = exit;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var args = _getArgs();
        if (!TenantAdministrationCommand<TTenant>.IsCommand(args))
        {
            return;
        }

        var exitCode = await new TenantAdministrationCommand<TTenant>(_services)
            .RunAsync(args, cancellationToken)
            .ConfigureAwait(false);

        _exit(exitCode);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
