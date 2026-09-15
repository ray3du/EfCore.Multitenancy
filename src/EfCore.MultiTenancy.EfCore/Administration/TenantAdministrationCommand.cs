using EfCore.MultiTenancy.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace EfCore.MultiTenancy.EfCore.Administration;

/// <summary>
/// Generic, dependency-free command-line front end over <see cref="ITenantAdministrationService{TTenant}"/>,
/// in the spirit of django-tenants' <c>create_tenant</c>/<c>delete_tenant</c> management
/// commands: a <c>create_tenant</c>, <c>delete_tenant</c>, and <c>list_tenants</c> verb,
/// each operating on one or more tenants identified by schema name, id, or domain.
/// </summary>
/// <remarks>
/// <para>Wire it into any console entry point, ahead of your normal startup:</para>
/// <code>
/// if (TenantAdministrationCommand&lt;AppTenant&gt;.IsCommand(args))
/// {
///     return await new TenantAdministrationCommand&lt;AppTenant&gt;(app.Services).RunAsync(args);
/// }
/// </code>
/// <para>
/// <c>delete_tenant</c> previews exactly which tenants matched before doing anything,
/// then prompts <c>[y/N]</c> unless <c>--force</c>/<c>--noinput</c> is passed — appropriate
/// for a human at a terminal. A non-interactive caller (an HTTP endpoint, a hosted job)
/// should call <see cref="ITenantAdministrationService{TTenant}"/> directly instead and
/// decide its own confirmation policy; this class never should, and does not, get
/// resolved from DI itself.
/// </para>
/// </remarks>
public sealed class TenantAdministrationCommand<TTenant> where TTenant : Tenant, new()
{
    private static readonly string[] KnownVerbs = { "create_tenant", "delete_tenant", "list_tenants" };

    private readonly IServiceProvider _services;
    private readonly TextWriter _out;
    private readonly TextWriter _error;
    private readonly TextReader _in;

    public TenantAdministrationCommand(
        IServiceProvider services,
        TextWriter? output = null,
        TextWriter? error = null,
        TextReader? input = null)
    {
        _services = services;
        _out = output ?? Console.Out;
        _error = error ?? Console.Error;
        _in = input ?? Console.In;
    }

    /// <summary>True if <paramref name="args"/>[0] names one of this command's verbs.</summary>
    public static bool IsCommand(string[] args) =>
        args.Length > 0 && KnownVerbs.Contains(args[0], StringComparer.OrdinalIgnoreCase);

    /// <summary>Runs the verb named by <paramref name="args"/>[0]. Returns a process exit code (0 = success).</summary>
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var verb = args[0];
        var rest = args.Skip(1).ToArray();

        using var scope = _services.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<ITenantAdministrationService<TTenant>>();

        try
        {
            return verb.ToLowerInvariant() switch
            {
                "create_tenant" => await RunCreateAsync(administration, rest, cancellationToken).ConfigureAwait(false),
                "delete_tenant" => await RunDeleteAsync(administration, rest, cancellationToken).ConfigureAwait(false),
                "list_tenants" => await RunListAsync(administration, rest, cancellationToken).ConfigureAwait(false),
                _ => Unknown(verb),
            };
        }
        catch (CommandArgumentException ex)
        {
            _error.WriteLine($"{verb}: {ex.Message}");
            return 1;
        }
    }

    private int Unknown(string verb)
    {
        _error.WriteLine($"Unknown command '{verb}'.");
        PrintUsage();
        return 1;
    }

    private void PrintUsage()
    {
        _out.WriteLine("Tenant administration commands:");
        _out.WriteLine("  create_tenant --tenant \"<Name>|<Domain>[|<SchemaName>]\" [--tenant \"...\"]...");
        _out.WriteLine("  delete_tenant (--schema <name> | --id <guid> | --domain <domain>)... [--force] [--keep-schema]");
        _out.WriteLine("  list_tenants [--all]");
    }

    private async Task<int> RunCreateAsync(ITenantAdministrationService<TTenant> administration, string[] args, CancellationToken cancellationToken)
    {
        var parsed = CommandLineArgs.Parse(args);
        var specs = parsed.GetValues("tenant");
        if (specs.Count == 0)
        {
            throw new CommandArgumentException("At least one --tenant \"<Name>|<Domain>[|<SchemaName>]\" is required.");
        }

        var requests = new List<TenantCreationRequest<TTenant>>(specs.Count);
        foreach (var spec in specs)
        {
            var parts = spec.Split('|');
            if (parts.Length is < 2 or > 3 || parts[0].Trim().Length == 0 || parts[1].Trim().Length == 0)
            {
                throw new CommandArgumentException($"Invalid --tenant spec '{spec}'; expected \"<Name>|<Domain>[|<SchemaName>]\".");
            }

            var tenant = new TTenant { Name = parts[0].Trim() };
            if (parts.Length == 3 && parts[2].Trim().Length > 0)
            {
                tenant.SchemaName = parts[2].Trim();
            }

            requests.Add(new TenantCreationRequest<TTenant>(tenant, parts[1].Trim()));
        }

        var summary = await administration.CreateTenantsAsync(requests, cancellationToken).ConfigureAwait(false);
        foreach (var result in summary.Results)
        {
            _out.WriteLine(result.Succeeded
                ? $"Created tenant '{result.Tenant.Name}' (schema '{result.Tenant.SchemaName}', domain '{result.Domain}', id {result.Tenant.Id})."
                : $"Failed to create tenant '{result.Tenant.Name}' ({result.Domain}): {result.Error!.Message}");
        }

        if (!summary.AllSucceeded)
        {
            _error.WriteLine($"{summary.FailureCount} of {summary.Results.Count} tenant(s) failed to create.");
        }

        return summary.AllSucceeded ? 0 : 1;
    }

    private async Task<int> RunDeleteAsync(ITenantAdministrationService<TTenant> administration, string[] args, CancellationToken cancellationToken)
    {
        var parsed = CommandLineArgs.Parse(args);

        var identifiers = new List<TenantIdentifier>();
        identifiers.AddRange(parsed.GetValues("schema").Select(TenantIdentifier.ForSchemaName));
        identifiers.AddRange(parsed.GetValues("domain").Select(TenantIdentifier.ForDomain));
        foreach (var idText in parsed.GetValues("id"))
        {
            if (!Guid.TryParse(idText, out var id))
            {
                throw new CommandArgumentException($"'{idText}' is not a valid tenant id (expected a GUID).");
            }

            identifiers.Add(TenantIdentifier.ForId(id));
        }

        if (identifiers.Count == 0)
        {
            throw new CommandArgumentException("At least one --schema, --id, or --domain identifier is required.");
        }

        var mode = parsed.HasSwitch("keep-schema") ? TenantDeletionMode.KeepSchema : TenantDeletionMode.DropSchema;

        // Resolve everything first (read-only) so the operator sees exactly what will be
        // affected — including anything that failed to resolve — before anything destructive runs.
        var preview = new List<(TenantIdentifier Identifier, TTenant? Tenant)>();
        foreach (var identifier in identifiers)
        {
            preview.Add((identifier, await administration.FindAsync(identifier, cancellationToken).ConfigureAwait(false)));
        }

        var missing = preview.Where(p => p.Tenant is null).ToList();
        foreach (var (identifier, _) in missing)
        {
            _error.WriteLine($"No tenant found for {identifier}.");
        }

        var toDelete = preview
            .Where(p => p.Tenant is not null)
            .Select(p => p.Tenant!)
            .GroupBy(t => t.Id)
            .Select(g => g.First())
            .ToList();

        if (toDelete.Count == 0)
        {
            return missing.Count > 0 ? 1 : 0;
        }

        _out.WriteLine(mode == TenantDeletionMode.KeepSchema
            ? "The following tenants will be removed from the registry; their schemas are kept:"
            : "The following tenants and ALL THEIR DATA will be permanently deleted:");
        foreach (var tenant in toDelete)
        {
            _out.WriteLine($"  - {tenant.Name} (schema '{tenant.SchemaName}', id {tenant.Id})");
        }

        if (!parsed.HasSwitch("force") && !parsed.HasSwitch("noinput"))
        {
            _out.Write("Are you sure you want to continue? [y/N]: ");
            var confirmation = (_in.ReadLine() ?? string.Empty).Trim();
            if (!confirmation.Equals("y", StringComparison.OrdinalIgnoreCase) &&
                !confirmation.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                _out.WriteLine("Aborted. No changes were made.");
                return 1;
            }
        }

        var summary = await administration.DeleteTenantsAsync(
            toDelete.Select(t => TenantIdentifier.ForId(t.Id)).ToList(), mode, cancellationToken).ConfigureAwait(false);

        foreach (var result in summary.Results)
        {
            _out.WriteLine(result.Succeeded
                ? $"Deleted tenant '{result.Tenant!.Name}' (schema '{result.Tenant.SchemaName}')."
                : $"Failed to delete tenant for {result.Identifier}: {result.Error!.Message}");
        }

        return summary.AllSucceeded && missing.Count == 0 ? 0 : 1;
    }

    private async Task<int> RunListAsync(ITenantAdministrationService<TTenant> administration, string[] args, CancellationToken cancellationToken)
    {
        var parsed = CommandLineArgs.Parse(args);
        var tenants = await administration.ListTenantsAsync(parsed.HasSwitch("all"), cancellationToken).ConfigureAwait(false);

        if (tenants.Count == 0)
        {
            _out.WriteLine(parsed.HasSwitch("all") ? "No tenants found." : "No active tenants found. Pass --all to include inactive tenants.");
            return 0;
        }

        _out.WriteLine($"{"SCHEMA",-24} {"NAME",-28} {"DOMAIN",-28} {"ACTIVE",-7} ID");
        foreach (var tenant in tenants)
        {
            var domain = tenant.Domains.FirstOrDefault(d => d.IsPrimary)?.Domain
                ?? tenant.Domains.FirstOrDefault()?.Domain
                ?? "(none)";
            _out.WriteLine($"{tenant.SchemaName,-24} {tenant.Name,-28} {domain,-28} {(tenant.IsActive ? "yes" : "no"),-7} {tenant.Id}");
        }

        return 0;
    }
}
