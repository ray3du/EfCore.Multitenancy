using EfCore.MultiTenancy.Core.Models;
using EfCore.MultiTenancy.EfCore.Administration;
using Microsoft.Extensions.DependencyInjection;

namespace EfCore.MultiTenancy.UnitTests;

/// <summary>
/// Exercises <see cref="TenantAdministrationCommand{TTenant}"/>'s argument parsing,
/// confirmation-prompt safety, and exit-code contract against a fake
/// <see cref="ITenantAdministrationService{TTenant}"/> — no database involved. The real
/// service's own behavior (registry-then-schema deletion order, etc.) is covered
/// separately against a live Postgres instance in the integration tests.
/// </summary>
public class TenantAdministrationCommandTests
{
    private sealed class FakeTenantAdministrationService : ITenantAdministrationService<Tenant>
    {
        public List<IReadOnlyList<TenantCreationRequest<Tenant>>> CreateCalls { get; } = new();
        public Func<IReadOnlyList<TenantCreationRequest<Tenant>>, TenantCreationSummary<Tenant>>? OnCreate { get; set; }

        public List<(IReadOnlyList<TenantIdentifier> Identifiers, TenantDeletionMode Mode)> DeleteCalls { get; } = new();
        public Func<IReadOnlyList<TenantIdentifier>, TenantDeletionMode, TenantDeletionSummary<Tenant>>? OnDelete { get; set; }

        public Dictionary<TenantIdentifier, Tenant?> FindResults { get; } = new();

        public IReadOnlyList<Tenant> ListResult { get; set; } = Array.Empty<Tenant>();
        public bool? LastListIncludeInactive { get; private set; }

        public Task<TenantCreationSummary<Tenant>> CreateTenantsAsync(
            IReadOnlyList<TenantCreationRequest<Tenant>> requests, CancellationToken cancellationToken = default)
        {
            CreateCalls.Add(requests);
            var summary = OnCreate?.Invoke(requests) ?? new TenantCreationSummary<Tenant>(
                requests.Select(r => new TenantCreationResult<Tenant>(r.Tenant, r.Domain, true, null)).ToList());
            return Task.FromResult(summary);
        }

        public Task<TenantDeletionSummary<Tenant>> DeleteTenantsAsync(
            IReadOnlyList<TenantIdentifier> identifiers,
            TenantDeletionMode mode = TenantDeletionMode.DropSchema,
            CancellationToken cancellationToken = default)
        {
            DeleteCalls.Add((identifiers, mode));
            // The command always resolves identifiers via FindAsync first and then calls
            // this with TenantIdentifier.ForId(...), so match on Id here (not on the
            // original identifier) — mirroring the real service, which only ever reports
            // Succeeded=true alongside a non-null Tenant.
            var summary = OnDelete?.Invoke(identifiers, mode) ?? new TenantDeletionSummary<Tenant>(
                identifiers.Select(i =>
                {
                    var tenant = FindResults.Values.FirstOrDefault(t => t?.Id == i.Id);
                    return tenant is not null
                        ? new TenantDeletionResult<Tenant>(i, tenant, true, null)
                        : new TenantDeletionResult<Tenant>(i, null, false, new InvalidOperationException("not found"));
                }).ToList());
            return Task.FromResult(summary);
        }

        public Task<Tenant?> FindAsync(TenantIdentifier identifier, CancellationToken cancellationToken = default) =>
            Task.FromResult(FindResults.GetValueOrDefault(identifier));

        public Task<IReadOnlyList<Tenant>> ListTenantsAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
        {
            LastListIncludeInactive = includeInactive;
            return Task.FromResult(ListResult);
        }
    }

    private static (TenantAdministrationCommand<Tenant> Command, FakeTenantAdministrationService Fake, StringWriter Out, StringWriter Err) MakeCommand(
        string? stdin = null)
    {
        var fake = new FakeTenantAdministrationService();
        var services = new ServiceCollection();
        services.AddSingleton<ITenantAdministrationService<Tenant>>(fake);
        var provider = services.BuildServiceProvider();

        var output = new StringWriter();
        var error = new StringWriter();
        var input = new StringReader(stdin ?? string.Empty);
        var command = new TenantAdministrationCommand<Tenant>(provider, output, error, input);
        return (command, fake, output, error);
    }

    [Theory]
    [InlineData("create_tenant")]
    [InlineData("CREATE_TENANT")]
    [InlineData("delete_tenant")]
    [InlineData("list_tenants")]
    public void IsCommand_RecognizesKnownVerbs_CaseInsensitively(string verb)
    {
        Assert.True(TenantAdministrationCommand<Tenant>.IsCommand(new[] { verb }));
    }

    [Theory]
    [InlineData("migrate")]
    [InlineData("")]
    public void IsCommand_RejectsUnknownOrEmptyArgs(string verb)
    {
        var args = verb.Length == 0 ? Array.Empty<string>() : new[] { verb };
        Assert.False(TenantAdministrationCommand<Tenant>.IsCommand(args));
    }

    [Fact]
    public async Task CreateTenant_NoTenantFlag_FailsWithoutCallingService()
    {
        var (command, fake, _, error) = MakeCommand();

        var exitCode = await command.RunAsync(new[] { "create_tenant" });

        Assert.Equal(1, exitCode);
        Assert.Empty(fake.CreateCalls);
        Assert.Contains("--tenant", error.ToString());
    }

    [Fact]
    public async Task CreateTenant_ParsesNameDomainAndSchemaFromSpec()
    {
        var (command, fake, output, _) = MakeCommand();

        var exitCode = await command.RunAsync(new[] { "create_tenant", "--tenant", "Acme Corp|acme.example.com|acme_corp" });

        Assert.Equal(0, exitCode);
        var request = Assert.Single(Assert.Single(fake.CreateCalls));
        Assert.Equal("Acme Corp", request.Tenant.Name);
        Assert.Equal("acme.example.com", request.Domain);
        Assert.Equal("acme_corp", request.Tenant.SchemaName);
        Assert.Contains("Created tenant 'Acme Corp'", output.ToString());
    }

    [Fact]
    public async Task CreateTenant_SchemaOmitted_LeavesSchemaNameEmptyForTheProvisioningServiceToDerive()
    {
        var (command, fake, _, _) = MakeCommand();

        await command.RunAsync(new[] { "create_tenant", "--tenant", "Acme Corp|acme.example.com" });

        var request = Assert.Single(Assert.Single(fake.CreateCalls));
        Assert.Equal(string.Empty, request.Tenant.SchemaName);
    }

    [Fact]
    public async Task CreateTenant_MultipleTenantSpecs_CreatesAllInOneBatch()
    {
        var (command, fake, _, _) = MakeCommand();

        var exitCode = await command.RunAsync(new[]
        {
            "create_tenant", "--tenant", "Acme|acme", "--tenant", "Globex|globex",
        });

        Assert.Equal(0, exitCode);
        var batch = Assert.Single(fake.CreateCalls);
        Assert.Equal(2, batch.Count);
    }

    [Fact]
    public async Task CreateTenant_ServiceReportsAPartialFailure_ReturnsNonZeroAndPrintsIt()
    {
        var (command, fake, output, error) = MakeCommand();
        fake.OnCreate = requests => new TenantCreationSummary<Tenant>(
            new[] { new TenantCreationResult<Tenant>(requests[0].Tenant, requests[0].Domain, false, new InvalidOperationException("domain taken")) });

        var exitCode = await command.RunAsync(new[] { "create_tenant", "--tenant", "Acme|acme" });

        Assert.Equal(1, exitCode);
        Assert.Contains("Failed to create tenant 'Acme'", output.ToString());
        Assert.Contains("domain taken", output.ToString());
        Assert.Contains("1 of 1 tenant(s) failed", error.ToString());
    }

    [Fact]
    public async Task CreateTenant_MalformedSpec_FailsWithoutCallingService()
    {
        var (command, fake, _, error) = MakeCommand();

        var exitCode = await command.RunAsync(new[] { "create_tenant", "--tenant", "no-pipe-delimiter" });

        Assert.Equal(1, exitCode);
        Assert.Empty(fake.CreateCalls);
        Assert.Contains("Invalid --tenant spec", error.ToString());
    }

    [Fact]
    public async Task DeleteTenant_NoIdentifiers_FailsWithoutCallingService()
    {
        var (command, fake, _, error) = MakeCommand();

        var exitCode = await command.RunAsync(new[] { "delete_tenant" });

        Assert.Equal(1, exitCode);
        Assert.Empty(fake.DeleteCalls);
        Assert.Contains("--schema, --id, or --domain", error.ToString());
    }

    [Fact]
    public async Task DeleteTenant_InvalidGuid_FailsWithoutCallingService()
    {
        var (command, fake, _, error) = MakeCommand();

        var exitCode = await command.RunAsync(new[] { "delete_tenant", "--id", "not-a-guid" });

        Assert.Equal(1, exitCode);
        Assert.Empty(fake.DeleteCalls);
        Assert.Contains("not a valid tenant id", error.ToString());
    }

    [Fact]
    public async Task DeleteTenant_IdentifierNotFound_ReportsItAndNeverCallsDelete()
    {
        var (command, fake, _, error) = MakeCommand("y");

        var exitCode = await command.RunAsync(new[] { "delete_tenant", "--schema", "ghost" });

        Assert.Equal(1, exitCode);
        Assert.Empty(fake.DeleteCalls);
        Assert.Contains("No tenant found for schema 'ghost'", error.ToString());
    }

    [Fact]
    public async Task DeleteTenant_WithoutForce_DeclinedConfirmation_AbortsWithoutCallingDelete()
    {
        var tenantId = Guid.NewGuid();
        var (command, fake, output, _) = MakeCommand("n");
        fake.FindResults[TenantIdentifier.ForSchemaName("acme")] =
            new Tenant { Id = tenantId, Name = "Acme", SchemaName = "acme" };

        var exitCode = await command.RunAsync(new[] { "delete_tenant", "--schema", "acme" });

        Assert.Equal(1, exitCode);
        Assert.Empty(fake.DeleteCalls);
        Assert.Contains("Aborted. No changes were made.", output.ToString());
    }

    [Fact]
    public async Task DeleteTenant_WithoutForce_ConfirmedWithY_CallsDeleteByResolvedId()
    {
        var tenantId = Guid.NewGuid();
        var (command, fake, output, _) = MakeCommand("y");
        fake.FindResults[TenantIdentifier.ForSchemaName("acme")] =
            new Tenant { Id = tenantId, Name = "Acme", SchemaName = "acme" };

        var exitCode = await command.RunAsync(new[] { "delete_tenant", "--schema", "acme" });

        Assert.Equal(0, exitCode);
        var call = Assert.Single(fake.DeleteCalls);
        Assert.Equal(TenantDeletionMode.DropSchema, call.Mode);
        Assert.Equal(TenantIdentifier.ForId(tenantId), Assert.Single(call.Identifiers));
        Assert.Contains("Deleted tenant 'Acme'", output.ToString());
    }

    [Fact]
    public async Task DeleteTenant_Force_SkipsPromptEntirely()
    {
        var tenantId = Guid.NewGuid();
        // Empty stdin: if the command tried to read a confirmation line, ReadLine would
        // return null and the command would (incorrectly) treat that as "declined".
        var (command, fake, _, _) = MakeCommand(stdin: null);
        fake.FindResults[TenantIdentifier.ForSchemaName("acme")] =
            new Tenant { Id = tenantId, Name = "Acme", SchemaName = "acme" };

        var exitCode = await command.RunAsync(new[] { "delete_tenant", "--schema", "acme", "--force" });

        Assert.Equal(0, exitCode);
        Assert.Single(fake.DeleteCalls);
    }

    [Fact]
    public async Task DeleteTenant_KeepSchema_PassesKeepSchemaModeThrough()
    {
        var tenantId = Guid.NewGuid();
        var (command, fake, _, _) = MakeCommand();
        fake.FindResults[TenantIdentifier.ForSchemaName("acme")] =
            new Tenant { Id = tenantId, Name = "Acme", SchemaName = "acme" };

        await command.RunAsync(new[] { "delete_tenant", "--schema", "acme", "--force", "--keep-schema" });

        Assert.Equal(TenantDeletionMode.KeepSchema, Assert.Single(fake.DeleteCalls).Mode);
    }

    [Fact]
    public async Task ListTenants_Default_QueriesActiveOnly()
    {
        var (command, fake, output, _) = MakeCommand();

        var exitCode = await command.RunAsync(new[] { "list_tenants" });

        Assert.Equal(0, exitCode);
        Assert.False(fake.LastListIncludeInactive);
        Assert.Contains("No active tenants found", output.ToString());
    }

    [Fact]
    public async Task ListTenants_All_QueriesIncludingInactive()
    {
        var (command, fake, _, _) = MakeCommand();

        await command.RunAsync(new[] { "list_tenants", "--all" });

        Assert.True(fake.LastListIncludeInactive);
    }

    [Fact]
    public async Task ListTenants_PrintsOneRowPerTenant()
    {
        var (command, fake, output, _) = MakeCommand();
        var tenant = new Tenant { Name = "Acme", SchemaName = "acme", IsActive = true };
        tenant.Domains.Add(new TenantDomain { TenantId = tenant.Id, Domain = "acme.example.com", IsPrimary = true });
        fake.ListResult = new[] { tenant };

        var exitCode = await command.RunAsync(new[] { "list_tenants" });

        Assert.Equal(0, exitCode);
        var text = output.ToString();
        Assert.Contains("acme", text);
        Assert.Contains("Acme", text);
        Assert.Contains("acme.example.com", text);
        Assert.Contains(tenant.Id.ToString(), text);
    }
}
